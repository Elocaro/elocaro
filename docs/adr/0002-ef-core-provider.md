# ADR 0002: Datenbank PostgreSQL statt MariaDB

- Status: angenommen (ersetzt den ersten Entwurf „MariaDB mit Pomelo auf EF Core 9“)
- Datum: 2026-10-18

## Kontext

Ursprünglich war MariaDB vorgegeben. Der etablierte EF-Core-Provider Pomelo.EntityFrameworkCore.MySql unterstützt in seinem aktuellen Release (9.0.0) nur EF Core 9. Ein Mischen mit EF-Core-10-Paketen (z. B. Identity) führt zu NU1107/NU1608 und `MissingMethodException`. Es gab drei Optionen:

1. MariaDB + Pomelo, EF Core auf 9.x gepinnt
2. MariaDB + Fork `Microting.EntityFrameworkCore.MySql` (EF Core 10, kleiner Maintainer)
3. PostgreSQL + `Npgsql.EntityFrameworkCore.PostgreSQL`

## Entscheidung

**PostgreSQL (aktuelle Major-Version, offizielles Docker-Image) mit Npgsql**, Entscheidung des Nutzers nach Vergleich.

## Begründung

- Npgsql wird eng mit dem EF-Core-Team entwickelt und ist zu jeder EF-Version am ersten Tag verfügbar. Das Versions-Lag tritt nicht bei jedem .NET-Release erneut auf.
- **Zeitraum-Typen (`daterange`) und Exclusion Constraints** (`btree_gist`) erzwingen überlappungsfreie Gültigkeiten direkt in der DB, z. B. „ein aktiver Vertrag je Mietobjekt“ oder „ein Mietbestandteil je Art und Zeitpunkt“. Das ist zentral für Elocaro, weil fast alles historisiert ist.
- **Transaktionale DDL:** Eine fehlgeschlagene Migration wird vollständig zurückgerollt.
- Strenge Typen und `numeric` ohne stilles Abschneiden. `jsonb` für Rechenprotokolle, OCR-Ergebnisse und Audit-Diffs.
- Betrieb (Docker, `pg_dump`, Testcontainers) ist gleichwertig einfach.

## Konsequenzen

- Alle Pakete auf EF Core 10 / .NET 10, keine Pins nötig.
- Docker Compose: Service `postgres` statt `mariadb`. Backup via `pg_dump`.
- Quartz.NET nutzt den PostgreSQL-ADO-Store.
- Kollation: deutsche Sortierung über ICU-Kollation (`de-DE-x-icu`) für Namensspalten, wo nötig.
- Die Vorgabe MariaDB aus dem Interview ist damit aufgehoben.
