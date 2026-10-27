# MietHahn auf einer Azure-VM

Anleitung für den Betrieb auf **einer** Linux-VM in Azure (Ubuntu 24.04, 2 vCPU / 4 GB, Germany West Central). Der Stack ist derselbe wie in [betrieb.md](betrieb.md); hier steht nur, wie die Azure-Teile entstehen. Die Datensicherung geht in einen Azure-Blob-Container.

Warum eine VM und nicht Container Apps und Flexible Server? Der Stack besteht aus fünf Containern mit Volumes und einem Backup-Dienst mit eigenem Zeitplan. Auf einer VM läuft er unverändert; die Azure-PaaS-Dienste bräuchten Umbauten und kosten mehr.

## Voraussetzungen

- Azure CLI (`az`), angemeldet (`az login`), Zugriff auf die Subscription
- Eine Domain mit Zugriff auf das DNS, SMTP-Zugang eines Mailanbieters
- Ausgehend ist in Azure Port 25 gesperrt; SMTP über 587 (`StartTls`) oder 465 (`SslOnConnect`) funktioniert

## 1. Ressourcen anlegen

```bash
SUB=<subscription-id>
RG=MietHahn
LOC=germanywestcentral
VM=miethahn-vm
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
SA=miethahnbackup$RANDOM          # global eindeutig, 3-24 Zeichen, nur Kleinbuchstaben und Ziffern
az storage account create -g "$RG" -n "$SA" -l "$LOC" --sku Standard_LRS --kind StorageV2 --access-tier Cool \
  --min-tls-version TLS1_2 --allow-blob-public-access false
KEY=$(az storage account keys list -g "$RG" -n "$SA" --query '[0].value' -o tsv)
az storage container create --account-name "$SA" --account-key "$KEY" -n miethahn-backup
echo "$SA"; echo "$KEY"
```

Für zusätzliche Sicherheit gegen Verlust der ganzen Ressourcengruppe das Konto in eine eigene Gruppe legen oder `Standard_GRS` wählen. Den Schlüssel nur in `deploy/.env` auf dem Server eintragen.

## 3. Budget-Warnung

Im Portal unter *Kostenverwaltung → Budgets* ein Monatsbudget mit Warnmail anlegen (z. B. bei 80 % und 100 %). Per CLI geht es mit `az consumption budget create`. Läuft die Subscription mit einem zeitlich begrenzten Guthaben, das Ablaufdatum notieren: danach wird nach Verbrauch abgerechnet.

## 4. Docker und MietHahn auf der VM

```bash
ssh azureuser@<ip>
curl -fsSL https://get.docker.com | sudo sh && sudo usermod -aG docker azureuser   # danach neu anmelden
git clone https://github.com/TheR00st3r/MietHahn.git && cd MietHahn
./deploy/install.sh            # fragt Domain, Kontakt-Mail und SMTP ab
```

Danach `deploy/.env` anpassen:

```
MIETHAHN_IMAGE=ghcr.io/ther00st3r/miethahn:<version>
MIETHAHN_BACKUP_IMAGE=ghcr.io/ther00st3r/miethahn-backup:<version>
RESTIC_REPOSITORY=azure:miethahn-backup:/
AZURE_ACCOUNT_NAME=<speicherkonto>
AZURE_ACCOUNT_KEY=<schlüssel>
```

und neu starten (`docker compose -f deploy/docker-compose.yml --env-file deploy/.env up -d`). Sind die GHCR-Pakete privat, vorher `docker login ghcr.io` mit einem GitHub-Token (`read:packages`). Die erste Sicherung von Hand anstoßen und prüfen, wie unter [Backup](betrieb.md#backup) beschrieben.

## 5. Betrieb

- **Updates des Systems:** `sudo apt update && sudo apt upgrade`, automatisch mit `unattended-upgrades` (bei Ubuntu vorinstalliert).
- **Update von MietHahn:** siehe [Update](betrieb.md#update).
- **Umzug** auf einen anderen Anbieter: Installation auf dem neuen Server, dann `restore` aus dem Blob-Container (siehe [Auf einem neuen Server](betrieb.md#auf-einem-neuen-server)).
- **Kosten sparen:** Die VM nicht stoppen, MietHahn muss erreichbar sein. Eine kleinere Größe (2 GB) ist mit Gotenberg zu knapp.
