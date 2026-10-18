using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MietHahn.Application.Dateien;
using MietHahn.Domain.Dateien;
using MietHahn.Infrastructure.Persistenz;

namespace MietHahn.Infrastructure.Dateien;

/// <summary>
/// Inhaltsadressierte Ablage im Dateisystem: <c>{Pfad}/{sha[0..2]}/{sha}</c>. Identische Inhalte werden einmal
/// gespeichert. Geschrieben wird atomar (temporäre Datei + Umbenennen).
/// </summary>
internal sealed class LokaleDateiablage(MietHahnDbContext db, IOptions<DateiablageOptionen> optionen) : IDateiablage
{
    public async Task<Datei> SpeichernAsync(Stream inhalt, string dateiname, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inhalt);
        var o = optionen.Value;

        var bytes = await LesenMitLimitAsync(inhalt, (long)o.MaxGroesseMb * 1024 * 1024, cancellationToken);
        var typ = DateiTypErkennung.Erkennen(bytes.AsSpan(0, Math.Min(bytes.Length, DateiTypErkennung.BenoetigteBytes)))
            ?? throw new DateiAbgelehntException("Dieser Dateityp ist nicht erlaubt. Erlaubt sind PDF, JPG, PNG und HEIC.");

        if (typ.IstBild)
        {
            bytes = BildAufbereitung.Aufbereiten(bytes, typ.MimeTyp, o.MaxBildKante, o.JpegQualitaet) ?? bytes;
        }

        var sha = Convert.ToHexStringLower(SHA256.HashData(bytes));
        await AblegenAsync(sha, bytes, cancellationToken);

        var datei = new Datei(DateinameBereinigen(dateiname, typ.Endung), typ.MimeTyp, bytes.LongLength, sha);
        db.Add(datei);
        await db.SaveChangesAsync(cancellationToken);
        return datei;
    }

    public async Task<GeoeffneteDatei?> OeffnenAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var datei = await db.Set<Datei>().AsNoTracking().SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (datei is null)
        {
            return null;
        }

        var pfad = Speicherpfad(datei.Sha256);
        if (!File.Exists(pfad))
        {
            throw new FileNotFoundException($"Inhalt der Datei {id} fehlt in der Ablage.", pfad);
        }

        return new GeoeffneteDatei(datei, new FileStream(pfad, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true));
    }

    internal string Speicherpfad(string sha256) => Speicherpfad(optionen.Value, sha256);

    internal static string Speicherpfad(DateiablageOptionen optionen, string sha256) => Path.Combine(Path.GetFullPath(optionen.Pfad), sha256[..2], sha256);

    private async Task AblegenAsync(string sha, byte[] bytes, CancellationToken cancellationToken)
    {
        var ziel = Speicherpfad(sha);
        if (File.Exists(ziel))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(ziel)!);
        var temp = ziel + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllBytesAsync(temp, bytes, cancellationToken);
        try
        {
            File.Move(temp, ziel, overwrite: false);
        }
        catch (IOException) when (File.Exists(ziel))
        {
            // Paralleler Upload desselben Inhalts: bereits vorhanden, gleicher Inhalt.
            File.Delete(temp);
        }
    }

    private static async Task<byte[]> LesenMitLimitAsync(Stream inhalt, long limit, CancellationToken cancellationToken)
    {
        using var puffer = new MemoryStream();
        var block = new byte[81920];
        int gelesen;
        while ((gelesen = await inhalt.ReadAsync(block, cancellationToken)) > 0)
        {
            if (puffer.Length + gelesen > limit)
            {
                throw new DateiAbgelehntException($"Die Datei ist zu groß (maximal {limit / 1024 / 1024} MB).");
            }

            puffer.Write(block, 0, gelesen);
        }

        if (puffer.Length == 0)
        {
            throw new DateiAbgelehntException("Die Datei ist leer.");
        }

        return puffer.ToArray();
    }

    /// <summary>Nur der Dateiname (keine Pfadanteile), Steuerzeichen entfernt, Endung passend zum erkannten Typ.</summary>
    internal static string DateinameBereinigen(string? dateiname, string endung)
    {
        var name = Path.GetFileName((dateiname ?? "").Replace('\\', '/'));
        name = new string(name.Where(z => !char.IsControl(z) && !Path.GetInvalidFileNameChars().Contains(z)).ToArray()).Trim();
        var basis = Path.GetFileNameWithoutExtension(name);
        if (string.IsNullOrWhiteSpace(basis))
        {
            basis = "datei";
        }

        // HEIC wird unverändert, JPG/PNG neu kodiert: die Endung folgt immer dem tatsächlichen Inhalt.
        return basis + endung;
    }
}
