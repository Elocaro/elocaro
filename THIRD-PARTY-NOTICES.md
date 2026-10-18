# Drittanbieter-Komponenten

MietHahn nutzt folgende Open-Source-Komponenten (als NuGet-Pakete bzw. Container-Images, unverändert):

| Komponente | Lizenz | Verwendung |
|---|---|---|
| .NET / ASP.NET Core / Entity Framework Core | MIT | Laufzeit, Web, ORM |
| Npgsql, Npgsql.EntityFrameworkCore.PostgreSQL | PostgreSQL License | Datenbanktreiber |
| EFCore.NamingConventions | Apache-2.0 | snake_case-Namen |
| Quartz.NET | Apache-2.0 | Hintergrundjobs. Das Datenbankschema `src/MietHahn.Infrastructure/Persistenz/Sql/quartz_3_22_6_postgres.sql` stammt aus dem Quartz.NET-Repository |
| MailKit / MimeKit | MIT | SMTP-Versand |
| Scriban | BSD-2-Clause | Vorlagen |
| SkiaSharp | MIT | Bildaufbereitung |
| PostgreSQL (Container) | PostgreSQL License | Datenbank |
| Mailpit (Container, nur Entwicklung/Test) | MIT | Mail-Testserver |
| xUnit, Testcontainers, NetArchTest | Apache-2.0 / MIT | Tests |
