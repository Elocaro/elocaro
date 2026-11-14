#!/usr/bin/env bash
# Erzeugt die Release-Notes einer Version aus den Commit-Betreffs seit dem vorigen Tag (Markdown-Liste).
# Aufruf: release-notes.sh vX.Y.Z   (im Repository, mit allen Tags und der vollen Historie)
set -euo pipefail

tag="${1:?Tag fehlt (z. B. v0.7.0)}"
previous="$(git tag --list 'v[0-9]*.[0-9]*.[0-9]*' --sort=-v:refname | grep -vx -- "$tag" \
  | while read -r t; do git merge-base --is-ancestor "$t" "$tag" && { echo "$t"; break; }; done || true)"
range="${previous:+$previous..}$tag"

# Merge-Commits und Commits, die nur Nummer/Dokumentation der Version pflegen, gehören nicht in die Notes.
git log "$range" --no-merges --format='%s' \
  | grep -Eiv '^(Merge |Release |Version |Changelog:)' \
  | sed -E 's/ \((Refs|Closes|Fixes) (#[0-9]+)(, [^)]*)?\)$/ (\2)/' \
  | sed -E 's/^/- /'
