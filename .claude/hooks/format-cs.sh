#!/usr/bin/env bash
# PostToolUse-Hook: formatiert eine von Claude geänderte .cs-Datei (nur Leerraum, schnell).
# Die vollständige Prüfung bleibt `dotnet format --verify-no-changes` vor dem Commit.
input="$(cat)"
file="$(printf '%s' "$input" | sed -n 's/.*"file_path"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' | head -n 1 | sed 's#\\#/#g')"
case "$file" in
  *.cs) ;;
  *) exit 0 ;;
esac
cd "${CLAUDE_PROJECT_DIR:-.}" || exit 0
root="$(pwd -W 2>/dev/null || pwd)"
file="${file#"$root"/}"
dotnet format whitespace Elocaro.slnx --no-restore --include "$file" >/dev/null 2>&1 || true
exit 0
