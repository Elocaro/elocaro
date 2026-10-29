# Drittanbieter-Komponenten

Elocaro selbst steht unter der AGPL-3.0 (siehe `LICENSE`). Die folgenden Komponenten sind nach erster Durchsicht mit ihr vereinbar (keine Rechtsberatung).

Elocaro nutzt folgende Open-Source-Komponenten (als NuGet-Pakete bzw. Container-Images, unverändert):

| Komponente | Lizenz | Verwendung |
|---|---|---|
| .NET / ASP.NET Core / Entity Framework Core | MIT | Laufzeit, Web, ORM |
| Npgsql, Npgsql.EntityFrameworkCore.PostgreSQL | PostgreSQL License | Datenbanktreiber |
| EFCore.NamingConventions | Apache-2.0 | snake_case-Namen |
| Quartz.NET | Apache-2.0 | Hintergrundjobs. Das Datenbankschema `src/Elocaro.Infrastructure/Persistenz/Sql/quartz_3_22_6_postgres.sql` stammt aus dem Quartz.NET-Repository |
| MailKit / MimeKit | MIT | SMTP-Versand |
| libfintx.FinTS | LGPL-3.0 | FinTS-Direktabruf der Kontoumsätze. Unverändert als NuGet-Paket eingebunden (dynamisches Linken); Quellcode: <https://github.com/libfintx/libfintx> |
| Scriban | BSD-2-Clause | Vorlagen |
| SkiaSharp | MIT | Bildaufbereitung |
| QRCoder | MIT | QR-Code für die Zwei-Faktor-Einrichtung |
| bUnit, AngleSharp (nur Tests) | MIT | Komponenten- und HTML-Tests |
| PostgreSQL (Container) | PostgreSQL License | Datenbank |
| Gotenberg (Container) | MIT | HTML → PDF (Chromium, LibreOffice für PDF/A) |
| Mailpit (Container, nur Entwicklung/Test) | MIT | Mail-Testserver |
| PdfPig (nur Tests) | Apache-2.0 | PDF-Prüfung in Tests |
| xUnit, Testcontainers, NetArchTest | Apache-2.0 / MIT | Tests |
