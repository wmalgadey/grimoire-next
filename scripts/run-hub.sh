#!/usr/bin/env bash
#
# Start the hub against the wiki named in .env, for trying an ingest by hand.
#
#   run-hub.sh                start the hub against the wiki as it stands
#   run-hub.sh --fresh        throw that wiki away first and start from an empty one
#   run-hub.sh --fresh --yes  the same without being asked to confirm
#
# `.env` at the repository root, `KEY=value` per line:
#
#   GRIMOIRE_WIKI         the wiki this Grimoire writes into      (required)
#   GRIMOIRE_PURPOSE      the description of what it is for       (required)
#   GRIMOIRE_MODEL        a pinned model id, never an alias       (required)
#   GRIMOIRE_URLS         where the hub listens; loopback only    (optional)
#   GRIMOIRE_INSTRUCTION  Grimoire's own instruction              (optional)
#
# A relative path is taken from the repository root. `.env` and `local/` are
# ignored by git, so what you try here stays yours.

set -euo pipefail

fresh=false
confirmed=false

for argument in "$@"; do
  case "$argument" in
    --fresh) fresh=true ;;
    --yes|-y) confirmed=true ;;
    *) echo "run-hub.sh [--fresh] [--yes]" >&2; exit 1 ;;
  esac
done

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

# Throwing the wiki away is how a changed instruction gets a clean reading: a run cannot delete
# or move what an earlier one wrote (GUARD-002), so an abandoned section stays in the tree and in
# the root index until someone removes it by hand.
if [[ "$fresh" == true && -d "$wiki" ]]; then
  # Refuse the paths where a mistyped GRIMOIRE_WIKI does real damage. The wiki is meant to be a
  # directory of its own, and none of these is that.
  case "$wiki" in
    / | "$HOME" | "$HOME"/ | "$root" | "$root"/) echo "Refusing to delete $wiki." >&2; exit 1 ;;
  esac

  files="$(find "$wiki" -type f -not -path '*/.git/*' | wc -l | tr -d ' ')"
  commits="$(git -C "$wiki" rev-list --count HEAD 2>/dev/null || echo 0)"

  echo "About to delete $wiki — $files file(s), $commits commit(s) of history."
  [[ "$commits" != "0" ]] && echo "Its history goes with it, and that history is the only undo there is."

  if [[ "$confirmed" != true ]]; then
    # No terminal to ask at means no answer, and no answer means no.
    read -r -p "Type the word yes to delete it: " answer || answer=""
    [[ "$answer" == "yes" ]] || { echo "Left alone." >&2; exit 1; }
  fi

  rm -rf "$wiki"
  echo "Deleted $wiki."
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
