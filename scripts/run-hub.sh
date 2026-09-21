#!/usr/bin/env bash
#
# Start the hub against the wiki named in .env, for trying an ingest by hand.
#
# `.env` at the repository root, `KEY=value` per line:
#
#   GRIMOIRE_WIKI         the wiki this Grimoire writes into      (required)
#   GRIMOIRE_PURPOSE      the description of what it is for       (required)
#   GRIMOIRE_MODEL        a pinned model id, never an alias       (required)
#   GRIMOIRE_URLS         where the hub listens; loopback only    (optional)
#   GRIMOIRE_INSTRUCTION  Grimoire's own instruction              (optional)
#
# A relative path is taken from the repository root. `.env` and `.local/` are
# ignored by git, so what you try here stays yours.

set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
env_file="$root/.env"

if [[ ! -f "$env_file" ]]; then
  echo "No .env at $env_file. It needs GRIMOIRE_WIKI, GRIMOIRE_PURPOSE and GRIMOIRE_MODEL;" >&2
  echo "the comment at the top of this script says what each one is." >&2
  exit 1
fi

# Read rather than source: this file says what the hub is started with, and nothing it says
# should be able to run.
while IFS='=' read -r key value; do
  [[ "$key" =~ ^[[:space:]]*# || -z "${key// }" ]] && continue
  key="${key//[[:space:]]/}"
  value="${value%\"}"; value="${value#\"}"
  value="${value%\'}"; value="${value#\'}"
  [[ "$key" == GRIMOIRE_* ]] && printf -v "$key" '%s' "$value"
done < "$env_file"

absolute() { [[ "$1" = /* ]] && echo "$1" || echo "$root/$1"; }

for required in GRIMOIRE_WIKI GRIMOIRE_PURPOSE GRIMOIRE_MODEL; do
  if [[ -z "${!required:-}" ]]; then
    echo "$required is not set in $env_file." >&2
    exit 1
  fi
done

wiki="$(absolute "$GRIMOIRE_WIKI")"
purpose="$(absolute "$GRIMOIRE_PURPOSE")"

if [[ ! -f "$purpose" ]]; then
  echo "No purpose description at $purpose. Every run is given it, and without it every" >&2
  echo "submission is refused (INGEST-003)." >&2
  exit 1
fi

# The wiki is a repository you control, because Grimoire never commits and never takes a run
# back: its history is your undo (docs/product.md §4). A first run needs nothing in it.
if [[ ! -d "$wiki" ]]; then
  mkdir -p "$wiki"
  git -C "$wiki" init --quiet
  echo "Made a wiki at $wiki and put it under git — commit what you want to keep."
fi

if [[ ! -d "$wiki/.git" ]]; then
  echo "Warning: $wiki is not a git repository, so you have no way to undo what a run writes." >&2
fi

arguments=(--wiki "$wiki" --purpose "$purpose" --model "$GRIMOIRE_MODEL")
[[ -n "${GRIMOIRE_URLS:-}" ]] && arguments+=(--urls "$GRIMOIRE_URLS")
[[ -n "${GRIMOIRE_INSTRUCTION:-}" ]] && arguments+=(--instruction "$(absolute "$GRIMOIRE_INSTRUCTION")")

echo "wiki    $wiki"
echo "purpose $purpose"
echo "model   $GRIMOIRE_MODEL"
echo "open    ${GRIMOIRE_URLS:-http://127.0.0.1:5057}"
echo

exec dotnet run --project "$root/src/Grimoire.Hub" --configuration Release -- "${arguments[@]}"
