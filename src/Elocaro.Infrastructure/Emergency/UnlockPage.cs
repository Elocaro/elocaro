using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Elocaro.Infrastructure.Emergency;

/// <summary>
/// The page around the encrypted data: a small unlock form and the script that decrypts with WebCrypto. No external
/// scripts, fonts or images. A Content-Security-Policy in the file allows exactly this one script (by hash), so injected
/// markup could not run code even if it got in. The decrypted folder is shown in a sandboxed frame without scripts.
/// </summary>
internal static class UnlockPage
{
    /// <summary>Whitespace matters: the Content-Security-Policy hash covers this exact text.</summary>
    internal const string Script = """

        (function () {
          "use strict";
          var $ = function (id) { return document.getElementById(id); };
          var form = $("sperre-formular");
          var message = $("meldung");

          function fail(text) {
            message.textContent = text;
            message.hidden = false;
          }

          function fromBase64(text) {
            var binary = atob(text);
            var bytes = new Uint8Array(binary.length);
            for (var i = 0; i < binary.length; i++) { bytes[i] = binary.charCodeAt(i); }
            return bytes;
          }

          async function decrypt(password) {
            var parameters = JSON.parse($("mappe-daten").textContent);
            var encoder = new TextEncoder();
            var material = await crypto.subtle.importKey("raw", encoder.encode(password.normalize("NFC")), "PBKDF2", false, ["deriveKey"]);
            var key = await crypto.subtle.deriveKey(
              { name: "PBKDF2", hash: "SHA-256", salt: fromBase64(parameters.salt), iterations: parameters.iterations },
              material, { name: "AES-GCM", length: 256 }, false, ["decrypt"]);
            var plain = await crypto.subtle.decrypt(
              { name: "AES-GCM", iv: fromBase64(parameters.nonce), additionalData: encoder.encode(parameters.aad) },
              key, fromBase64(parameters.ciphertext));
            return JSON.parse(new TextDecoder().decode(plain));
          }

          function resize(frame) {
            try { frame.style.height = frame.contentDocument.documentElement.scrollHeight + "px"; }
            catch (e) { frame.style.height = "80vh"; }
          }

          function show(content) {
            var frame = $("mappe-rahmen");
            frame.addEventListener("load", function () { resize(frame); });
            window.addEventListener("resize", function () { resize(frame); });
            frame.srcdoc = content.html;

            var list = $("anlagen-liste");
            (content.files || []).forEach(function (file) {
              var link = document.createElement("a");
              link.href = URL.createObjectURL(new Blob([fromBase64(file.data)], { type: file.type || "application/octet-stream" }));
              link.download = file.name;
              link.textContent = file.name;
              var item = document.createElement("li");
              item.appendChild(link);
              list.appendChild(item);
            });
            $("anlagen").hidden = list.children.length === 0;
            $("sperre").hidden = true;
            $("inhalt").hidden = false;
          }

          if (!(window.crypto && window.crypto.subtle)) {
            fail("Dieser Browser kann die Datei nicht entschlüsseln (WebCrypto fehlt). Bitte einen aktuellen Browser wie Firefox, Chrome, Edge oder Safari verwenden.");
            form.hidden = true;
            return;
          }

          form.addEventListener("submit", async function (event) {
            event.preventDefault();
            var field = $("passwort");
            var button = $("oeffnen");
            message.hidden = true;
            button.disabled = true;
            button.textContent = "Wird entschlüsselt …";
            try {
              var content = await decrypt(field.value);
              field.value = "";
              show(content);
            } catch (e) {
              fail("Das Passwort passt nicht, oder die Datei ist beschädigt. Bitte erneut versuchen.");
            } finally {
              button.disabled = false;
              button.textContent = "Mappe öffnen";
            }
          });

          $("drucken").addEventListener("click", function () { window.print(); });
          $("schliessen").addEventListener("click", function () { window.location.reload(); });
        })();

        """;

    /// <summary>The page with the placeholder <c>{{DATA}}</c> where the encrypted data block goes.</summary>
    public static string Template(DateOnly createdOn)
    {
        // Browsers normalise line breaks before hashing; a checkout with CRLF must not break the hash.
        var script = Script.ReplaceLineEndings("\n");
        var hash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(script)));
        return Html
            .Replace("{{HASH}}", hash, StringComparison.Ordinal)
            .Replace("{{DATE}}", createdOn.ToString("dd.MM.yyyy", CultureInfo.GetCultureInfo("de-DE")), StringComparison.Ordinal)
            .Replace("{{DATA_ID}}", EmergencyFolderEncryption.DataElementId, StringComparison.Ordinal)
            .Replace("{{SCRIPT}}", script, StringComparison.Ordinal);
    }

    private const string Html = """
        <!DOCTYPE html>
        <html lang="de">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <meta http-equiv="Content-Security-Policy" content="default-src 'none'; script-src 'sha256-{{HASH}}'; style-src 'unsafe-inline'; frame-src about:; base-uri 'none'; form-action 'none'">
        <title>Elocaro Notfallmappe (verschlüsselt)</title>
        <style>
        :root { color-scheme: light dark; --bg: #fdfcf8; --text: #1c1b1f; --muted: #5d5b62; --line: #cac4d0; --accent: #3b5b3a; --on-accent: #fff; --error: #b3261e; }
        @media (prefers-color-scheme: dark) { :root { --bg: #1c1b1f; --text: #e6e1e5; --muted: #b0acb4; --line: #49454f; --accent: #a8d5a2; --on-accent: #10200f; --error: #f2b8b5; } }
        * { box-sizing: border-box; }
        body { margin: 0; padding: 1rem; background: var(--bg); color: var(--text); font: 16px/1.5 system-ui, -apple-system, "Segoe UI", Roboto, sans-serif; }
        main { max-width: 56rem; margin: 0 auto; }
        h1 { font-size: 1.5rem; margin: 0 0 0.5rem; }
        p { margin: 0 0 0.75rem; }
        .muted { color: var(--muted); }
        label { display: block; font-weight: 600; margin-bottom: 0.25rem; }
        input[type=password] { width: 100%; min-height: 48px; padding: 0 0.75rem; font: inherit; color: var(--text); background: var(--bg); border: 1px solid var(--line); border-radius: 8px; }
        button { min-height: 48px; padding: 0 1.5rem; font: inherit; font-weight: 600; color: var(--on-accent); background: var(--accent); border: 0; border-radius: 24px; cursor: pointer; }
        button.secondary { color: var(--text); background: transparent; border: 1px solid var(--line); }
        button:disabled { opacity: 0.6; cursor: wait; }
        form > * + * { margin-top: 0.75rem; }
        .error { color: var(--error); font-weight: 600; }
        .toolbar { display: flex; flex-wrap: wrap; gap: 0.5rem; margin-bottom: 1rem; }
        #anlagen { margin-bottom: 1rem; }
        #anlagen a { display: inline-block; min-height: 44px; padding: 0.6rem 0; color: var(--accent); }
        iframe { display: block; width: 100%; height: 70vh; border: 0; }
        @media print { .no-print { display: none !important; } body { padding: 0; } }
        </style>
        </head>
        <body>
        <main>
        <div id="sperre">
        <h1>Elocaro Notfallmappe</h1>
        <p>Diese Datei enthält verschlüsselte Unterlagen zur Vermietung, erstellt am {{DATE}}. Zum Öffnen brauchen Sie das Passwort, das beim Erstellen gewählt wurde.</p>
        <p class="muted">Die Entschlüsselung geschieht nur in diesem Browserfenster. Es wird nichts gesendet, und die Datei braucht weder Internet noch Elocaro.</p>
        <noscript><p class="error">Zum Öffnen wird JavaScript benötigt. Bitte aktivieren Sie es für diese Datei.</p></noscript>
        <form id="sperre-formular">
        <div>
        <label for="passwort">Passwort</label>
        <input id="passwort" type="password" autocomplete="off" required>
        </div>
        <button id="oeffnen" type="submit">Mappe öffnen</button>
        </form>
        <p id="meldung" class="error" role="alert" hidden></p>
        </div>
        <div id="inhalt" hidden>
        <div class="toolbar no-print">
        <button id="drucken" type="button">Drucken</button>
        <button id="schliessen" type="button" class="secondary">Mappe schließen</button>
        </div>
        <div id="anlagen" class="no-print" hidden>
        <h2>Beigelegte Dokumente</h2>
        <ul id="anlagen-liste"></ul>
        </div>
        <iframe id="mappe-rahmen" title="Notfallmappe" sandbox="allow-same-origin"></iframe>
        </div>
        </main>
        <script type="application/json" id="{{DATA_ID}}">{{DATA}}</script>
        <script>{{SCRIPT}}</script>
        </body>
        </html>
        """;
}
