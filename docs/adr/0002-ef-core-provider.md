# ADR 0002: EF-Core-Provider für MariaDB

- Status: angenommen
- Datum: 2026-10-18

## Kontext

Vorgabe ist MariaDB. Der etablierte Provider ist Pomelo.EntityFrameworkCore.MySql. Dessen aktuelles Release (9.0.0) unterstützt nur EF Core 9 (`[9.0.0, 9.0.999]`). Werden EF-Core-10-Pakete (z. B. `Microsoft.AspNetCore.Identity.EntityFrameworkCore` 10.x) gemischt, gibt es NU1107/NU1608 und `MissingMethodException` zur Laufzeit.

## Entscheidung

- Laufzeit .NET 10 bleibt.
- Alle `Microsoft.EntityFrameworkCore.*`- und `Microsoft.AspNetCore.Identity.EntityFrameworkCore`-Pakete werden über Central Package Management auf **9.0.x** gepinnt.
- Sobald Pomelo ein EF-10-Release veröffentlicht, wird gemeinsam aktualisiert (eigenes Issue).
- Alternative für den Notfall: der Fork `Microting.EntityFrameworkCore.MySql` (folgt den EF-Releases). Er wird nicht standardmäßig genutzt, weil er weniger etabliert ist.

## Konsequenzen

Keine EF-10-Features (z. B. neue LINQ-Übersetzungen), bis Pomelo nachzieht. Eine CI-Prüfung verhindert versehentliche Paketmischung.
