#!/usr/bin/env bash
# Prüft einen Commit-Betreff. Die Betreffe landen als Liste in den Release-Notes (release-notes.sh), deshalb müssen sie für
# Anwender lesbar sein. Aufruf: check-commit-subject.sh "<Betreff>"   (Exit 1 mit Begründung bei einem Verstoß)
set -euo pipefail

subject="${1-}"

# Von Git erzeugte Betreffe sind ausgenommen.
case "$subject" in
  "Merge "*|"Revert \""*) exit 0 ;;
esac

fail() {
  echo "Ungültiger Commit-Betreff: \"$subject\"" >&2
  echo "  -> $1" >&2
  echo "  Muster: <Bereich>: <was sich für Anwender oder Entwickler ändert> (Refs #<Issue>)   – siehe CONTRIBUTING.md" >&2
  exit 1
}

case "$subject" in
  fixup!*|squash!*|amend!*) fail "fixup!/squash!/amend! vor dem Zusammenführen auflösen." ;;
esac

# Verweis auf das Issue am Ende: (Refs #12), (Closes #12), (Refs #12, #13) oder (Refs #12, Stufe 1)
re_ref=' \((Refs|Closes) #[0-9]+(, [^)]+)?\)$'
[[ "$subject" =~ $re_ref ]] || fail "Am Ende fehlt der Issue-Verweis \"(Refs #<Nr>)\" oder \"(Closes #<Nr>)\"."

text="$(sed -E "s/$re_ref//" <<< "$subject")"

(( ${#text} >= 15 )) || fail "Zu kurz (mindestens 15 Zeichen vor dem Issue-Verweis): Was hat sich geändert?"
(( ${#text} <= 130 )) || fail "Zu lang (höchstens 130 Zeichen vor dem Issue-Verweis, aktuell ${#text}): Einzelheiten gehören in den Text darunter."
[[ "$text" =~ ^[[:upper:]] ]] || fail "Muss mit einem Großbuchstaben beginnen."
[[ "$text" != *. ]] || fail "Kein Punkt am Ende."
[[ ! "$text" =~ ^(WIP|wip|Fix|fix|Update|update|Änderungen|Anpassungen|Diverses|Misc)[[:space:]:.]*$ ]] || fail "Zu allgemein: Bereich und Änderung nennen."
[[ ! "$text" =~ ^(Changelog:|Release |Version ) ]] || fail "Diese Präfixe filtert die Release-Notes-Erzeugung heraus; anders formulieren."
