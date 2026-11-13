# Elocaro auf einer Azure-VM

Anleitung für den Betrieb auf **einer** Linux-VM in Azure (Ubuntu 24.04, 2 vCPU / 4 GB, Germany West Central). Der Stack ist derselbe wie in [betrieb.md](betrieb.md); hier steht nur, wie die Azure-Teile entstehen. Die Datensicherung geht in einen Azure-Blob-Container.

Warum eine VM und nicht Container Apps und Flexible Server? Der Stack besteht aus fünf Containern mit Volumes und einem Backup-Dienst mit eigenem Zeitplan. Auf einer VM läuft er unverändert; die Azure-PaaS-Dienste bräuchten Umbauten und kosten mehr.

## Voraussetzungen

- Azure CLI (`az`), angemeldet (`az login`), Zugriff auf die Subscription
- Eine Domain mit Zugriff auf das DNS, SMTP-Zugang eines Mailanbieters
- Ausgehend ist in Azure Port 25 gesperrt; SMTP über 587 (`StartTls`) oder 465 (`SslOnConnect`) funktioniert

## 1. Ressourcen anlegen

```bash
SUB=<subscription-id>
RG=Elocaro
LOC=germanywestcentral
VM=elocaro-vm
MEINE_IP=$(curl -s https://api.ipify.org)

az account set --subscription "$SUB"
az group create -n "$RG" -l "$LOC"        # entfällt, wenn die Gruppe schon existiert

# VM mit statischer öffentlicher IP (Standard-SKU) und SSH-Schlüssel
az vm create -g "$RG" -n "$VM" -l "$LOC" \
  --image Ubuntu2404 --size Standard_B2als_v2 \
  --os-disk-size-gb 64 --storage-sku StandardSSD_LRS \
  --admin-username azureuser --ssh-key-values ~/.ssh/id_ed25519.pub \
  --public-ip-sku Standard --public-ip-address-allocation static \
  --nsg-rule NONE

# Firewall (NSG): 80/443 für alle, SSH nur von der eigenen Adresse
NSG=$(az network nic show --ids "$(az vm show -g "$RG" -n "$VM" --query 'networkProfile.networkInterfaces[0].id' -o tsv)" --query 'networkSecurityGroup.id' -o tsv | xargs basename)
az network nsg rule create -g "$RG" --nsg-name "$NSG" -n web --priority 100 --protocol Tcp --destination-port-ranges 80 443 --access Allow
az network nsg rule create -g "$RG" --nsg-name "$NSG" -n ssh --priority 110 --protocol Tcp --destination-port-ranges 22 --source-address-prefixes "$MEINE_IP" --access Allow

az vm show -g "$RG" -n "$VM" -d --query publicIps -o tsv     # diese Adresse ins DNS
```

Ändert sich die eigene IP-Adresse, die Regel `ssh` mit `az network nsg rule update --source-address-prefixes <neue-IP>` anpassen.

Dann im DNS einen **A-Eintrag** für die Domain auf die IP setzen (Let's Encrypt braucht ihn für das Zertifikat).

## 2. Datensicherung: Blob-Container

```bash
SA=elocarobackup$RANDOM          # global eindeutig, 3-24 Zeichen, nur Kleinbuchstaben und Ziffern
az storage account create -g "$RG" -n "$SA" -l "$LOC" --sku Standard_LRS --kind StorageV2 --access-tier Cool \
  --min-tls-version TLS1_2 --allow-blob-public-access false
KEY=$(az storage account keys list -g "$RG" -n "$SA" --query '[0].value' -o tsv)
az storage container create --account-name "$SA" --account-key "$KEY" -n elocaro-backup
echo "$SA"; echo "$KEY"
```

Für zusätzliche Sicherheit gegen Verlust der ganzen Ressourcengruppe das Konto in eine eigene Gruppe legen oder `Standard_GRS` wählen. Den Schlüssel nur in `deploy/.env` auf dem Server eintragen.

## 3. Budget-Warnung

Im Portal unter *Kostenverwaltung → Budgets* ein Monatsbudget mit Warnmail anlegen (z. B. bei 80 % und 100 %). Per CLI geht es mit `az consumption budget create`. Läuft die Subscription mit einem zeitlich begrenzten Guthaben, das Ablaufdatum notieren: danach wird nach Verbrauch abgerechnet.

## 4. Docker und Elocaro auf der VM

```bash
ssh azureuser@<ip>
curl -fsSL https://get.docker.com | sudo sh && sudo usermod -aG docker azureuser   # danach neu anmelden
git clone https://github.com/elocaro/elocaro.git && cd Elocaro
./deploy/install.sh --nur-env  # fragt Domain, Kontakt-Mail und SMTP ab und schreibt nur deploy/.env
```

Danach `deploy/.env` anpassen (Sicherungsziel und Images):

```
RESTIC_REPOSITORY=azure:elocaro-backup:/
AZURE_ACCOUNT_NAME=<speicherkonto>
AZURE_ACCOUNT_KEY=<schlüssel>
```

**Images:** Der Release-Workflow veröffentlicht sie in GHCR, dort genügt `ELOCARO_IMAGE=ghcr.io/elocaro/elocaro:<version>` und `ELOCARO_BACKUP_IMAGE=…/elocaro-backup:<version>`; sind die Pakete privat, vorher `docker login ghcr.io` mit einem GitHub-Token (`read:packages`). Nur ohne Registry (eigener Stand) baut der Server sie selbst aus dem Repository, das braucht auf kleinen VMs viel Speicher:

```
ELOCARO_IMAGE=elocaro:0.1.0
ELOCARO_BACKUP_IMAGE=elocaro-backup:0.1.0
ELOCARO_VERSION=0.1.0
```

```bash
DC="docker compose -f deploy/docker-compose.yml --env-file deploy/.env"
$DC build && $DC up -d
```

Ein Update ist in [Update auf der VM](#update-auf-der-vm-ohne-git-auf-dem-server) beschrieben. Die erste Sicherung von Hand anstoßen und prüfen, wie unter [Backup](betrieb.md#backup) beschrieben.

## Update auf der VM (ohne Git auf dem Server)

So wird eine Instanz auf der VM aktualisiert. Die VM hat kein `.git`, der Quellstand wird vom Arbeitsrechner hochgeladen; `deploy/.env` bleibt dabei erhalten. Auf Windows läuft das in Git Bash (`.gitattributes` erzwingt LF für Shell-Skripte, Dockerfile, Caddyfile und YAML, sonst scheitert `install.sh` mit `bash\r`).

```bash
# 1. Auf dem Arbeitsrechner: Release festlegen (der Release-Workflow muss grün durch sein,
#    damit die Images in GHCR liegen)
git pull
V=0.5.0
C=$(git rev-parse --short v$V)

# 2. Quelltext in ein neues Verzeichnis hochladen (alte Dateien bleiben nicht liegen),
#    .env übernehmen, vorher Sicherung anstoßen und das alte Image als Rückfallebene taggen
git archive --format=tar v$V | ssh azureuser@<ip> "set -e
  rm -rf ~/Elocaro.neu ~/Elocaro.alt; mkdir ~/Elocaro.neu
  tar -x -C ~/Elocaro.neu; echo $C > ~/Elocaro.neu/.deployed-commit
  cp -p ~/Elocaro/deploy/.env ~/Elocaro.neu/deploy/.env
  cd ~/Elocaro/deploy && docker compose --env-file .env run --rm backup backup | tail -3
  docker tag \$(docker inspect -f '{{.Image}}' elocaro-app-1) elocaro:vorher
  cd ~ && mv Elocaro Elocaro.alt && mv Elocaro.neu Elocaro"

# 3. Versionen in der .env setzen, Images aus GHCR ziehen (auf der VM wird nichts gebaut) und neu starten
ssh azureuser@<ip> "cd ~/Elocaro \
  && sed -i -e \"s#^ELOCARO_IMAGE=.*#ELOCARO_IMAGE=ghcr.io/elocaro/elocaro:$V#\" \
    -e \"s#^ELOCARO_BACKUP_IMAGE=.*#ELOCARO_BACKUP_IMAGE=ghcr.io/elocaro/elocaro-backup:$V#\" \
    -e \"s#^ELOCARO_VERSION=.*#ELOCARO_VERSION=$V#\" deploy/.env \
  && DC='docker compose -f deploy/docker-compose.yml --env-file deploy/.env' \
  && \$DC pull app backup && \$DC up -d --no-build"
```

Hinweise:

- Das Verzeichnis muss `Elocaro` heißen: Daraus leitet Compose den Projektnamen und damit die Namen der Volumes ab.
- Der Server baut nichts selbst: Die Images kommen aus GHCR (`ghcr.io/elocaro/elocaro` und `…/elocaro-backup`, öffentlich). Ein Build auf der VM (2 Kerne, 4 GB RAM, kein Swap) hat sie schon einmal so überlastet, dass sie nicht mehr antwortete. Der Quelltext wird nur für `docker-compose.yml`, Caddyfile und Skripte hochgeladen. Hat sich nur die Caddyfile geändert (sie ist als einzelne Datei eingebunden), zusätzlich `$DC up -d --force-recreate caddy`.
- Nach dem Start meldet Caddy `503`, bis die nächste Gesundheitsprüfung durch ist (`health_interval 30s`). Das ist normal und vergeht nach höchstens einer halben Minute.
- Datenbankmigrationen laufen beim Start der App, deshalb vorher die Sicherung (Schritt 2).
- Rückgängig machen: `cd ~/Elocaro.alt && $DC up -d --no-build` (die `.env` dort nennt die frühere Version; deren Images liegen noch auf der VM). Bei Modelländerungen mit Migration zusätzlich die Datenbank aus der Sicherung zurückspielen.
- Prüfen: `docker ps` (alle Container `healthy`/`Up`), `docker exec elocaro-caddy-1 wget -qO- http://app:8080/health` (`{"status":"Healthy"}`; welche Prüfung abweicht, steht im App-Log), außerhalb `https://<ihre-domain>/_framework/blazor.web.js` muss `200` liefern (fehlt die Datei, funktioniert keine interaktive Seite).
- Der genaue Stand auf dem Server steht in `~/Elocaro/.deployed-commit`.

## 5. Betrieb

- **Updates des Systems:** `sudo apt update && sudo apt upgrade`, automatisch mit `unattended-upgrades` (bei Ubuntu vorinstalliert).
- **Update von Elocaro:** siehe [Update](betrieb.md#update).
- **Umzug** auf einen anderen Anbieter: Installation auf dem neuen Server, dann `restore` aus dem Blob-Container (siehe [Auf einem neuen Server](betrieb.md#auf-einem-neuen-server)).
- **Kosten sparen:** Die VM nicht stoppen, Elocaro muss erreichbar sein. Eine kleinere Größe (2 GB) ist mit Gotenberg zu knapp.
