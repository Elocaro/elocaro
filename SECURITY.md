# Sicherheit

MietHahn verarbeitet personenbezogene Daten von Mietern und Bankdaten. Sicherheitslücken bitte **nicht** als öffentliches Issue melden.

## Meldeweg

- Über [GitHub Security Advisories](https://github.com/TheR00st3r/MietHahn/security/advisories/new) („Report a vulnerability“), oder
- per E-Mail an den Maintainer (Adresse im GitHub-Profil von TheR00st3r).

Bitte beschreiben Sie Version (siehe „Über MietHahn“), Schritte zum Nachstellen und die vermutete Auswirkung. Sie bekommen in der Regel innerhalb von sieben Tagen eine erste Antwort. Eine Behebung wird mit einem Hinweis im [Changelog](CHANGELOG.md) veröffentlicht, auf Wunsch mit Nennung.

## Unterstützte Versionen

Sicherheitskorrekturen gibt es für die jeweils neueste Version. Aktualisieren Sie wie in [docs/betrieb.md](docs/betrieb.md#update) beschrieben.

## Hinweise zum Betrieb

Betrieb, Updates und Datensicherung liegen bei Ihnen. Die wichtigsten Maßnahmen (Zwei-Faktor-Pflicht, Netztrennung von Datenbank und Gotenberg, TLS über Caddy, Backup mit Verschlüsselung) stehen in [docs/betrieb.md](docs/betrieb.md).
