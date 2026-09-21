#!/usr/bin/env bash
#
# Start the hub against the wiki named in .env, for trying an ingest by hand.

set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
env_file="$root/.env"

readonly KNOWN_KEYS="GRIMOIRE_WIKI GRIMOIRE_PURPOSE GRIMOIRE_MODEL GRIMOIRE_URLS GRIMOIRE_INSTRUCTION"
readonly REQUIRED_KEYS="GRIMOIRE_WIKI GRIMOIRE_PURPOSE GRIMOIRE_MODEL"

usage() {
  cat <<'USAGE'
Start the hub against the wiki named in .env, for trying an ingest by hand.

  run-hub.sh                start the hub against the wiki as it stands
  run-hub.sh --fresh        throw that wiki away first and start from an empty one
  run-hub.sh --fresh --yes  the same, without being asked to confirm
  run-hub.sh --help         this text

.env at the repository root, one KEY=value per line:

  GRIMOIRE_WIKI         the wiki this Grimoire writes into      (required)
  GRIMOIRE_PURPOSE      the description of what it is for       (required)
  GRIMOIRE_MODEL        a pinned model id, never an alias       (required)
  GRIMOIRE_URLS         where the hub listens; loopback only    (optional)
  GRIMOIRE_INSTRUCTION  Grimoire's own instruction              (optional)

A relative path is taken from the repository root. .env and local/ are ignored by
git, so what you try here stays yours; .env-example is a copy to start from.
USAGE
}

# Two ways to stop. `die` is for a situation the usage does not explain — a missing
# file, a tool that is not installed. `refuse` is for one it does.
die() { printf '%s\n' "$@" >&2; exit 1; }
refuse() { printf '%s\n\n' "$@" >&2; usage >&2; exit 1; }

fresh=false
confirmed=false

for argument in "$@"; do
  case "$argument" in
    --fresh) fresh=true ;;
    --yes|-y) confirmed=true ;;
    --help|-h) usage; exit 0 ;;
    *) refuse "run-hub.sh does not understand $argument." ;;
  esac
done

if [[ "$confirmed" == true && "$fresh" != true ]]; then
  refuse "--yes answers the question --fresh asks, and nothing else asks one."
fi

command -v dotnet >/dev/null || die "No dotnet on PATH. The hub is a .NET 10 project and cannot be built without it."

[[ -f "$env_file" ]] || refuse "No .env at $env_file. Copy .env-example to .env and edit it."

# Read rather than source: this file says what the hub is started with, and nothing it
# says should be able to run. The `|| [[ -n "$key" ]]` keeps a last line that has no
# newline after it, which an editor may well leave behind.
unknown=()
while IFS='=' read -r key value || [[ -n "$key" ]]; do
  key="${key%%[[:space:]]*}"; key="${key##[[:space:]]}"
  [[ -z "$key" || "$key" == \#* ]] && continue

  value="${value%$'\r'}"                                  # a file written on Windows
  value="${value#"${value%%[![:space:]]*}"}"              # leading space
  value="${value%"${value##*[![:space:]]}"}"              # trailing space
  value="${value%\"}"; value="${value#\"}"
  value="${value%\'}"; value="${value#\'}"

  if [[ " $KNOWN_KEYS " == *" $key "* ]]; then
    printf -v "$key" '%s' "$value"
  elif [[ "$key" == GRIMOIRE_* ]]; then
    unknown+=("$key")
  fi
done < "$env_file"

# A key nobody reads is almost always a key that was meant to be read.
if (( ${#unknown[@]} )); then
  printf 'Warning: %s in .env is not a setting run-hub.sh knows — mistyped?\n' "${unknown[@]}" >&2
fi

missing=()
for required in $REQUIRED_KEYS; do
  [[ -n "${!required:-}" ]] || missing+=("$required")
done

if (( ${#missing[@]} )); then
  refuse "$env_file does not set: ${missing[*]}."
fi

# An alias follows whatever it is pointed at, so a run made with one cannot be repeated
# and its cost cannot be attributed to a model (DEC-010).
case "$GRIMOIRE_MODEL" in
  opus|sonnet|haiku|fable|default|"")
    refuse "GRIMOIRE_MODEL is the alias '$GRIMOIRE_MODEL'. Name a model in full, e.g. claude-sonnet-5." ;;
esac

absolute() { [[ "$1" = /* ]] && printf '%s' "$1" || printf '%s' "$root/$1"; }

wiki="$(absolute "$GRIMOIRE_WIKI")"
purpose="$(absolute "$GRIMOIRE_PURPOSE")"

[[ -e "$purpose" ]] || die \
  "No purpose description at $purpose." \
  "Every run is given it, and without it every submission is refused (INGEST-003)."
[[ -f "$purpose" ]] || die "$purpose is not a file."
[[ -s "$purpose" ]] || die "$purpose is empty. It is half of what every run is told (Constitution V.1)."

if [[ -n "${GRIMOIRE_INSTRUCTION:-}" ]]; then
  instruction="$(absolute "$GRIMOIRE_INSTRUCTION")"
  [[ -f "$instruction" ]] || die "No instruction at $instruction."
fi

[[ -e "$wiki" && ! -d "$wiki" ]] && die "$wiki is not a directory, so it cannot be a wiki."

# Throwing the wiki away is how a changed instruction gets a clean reading: a run cannot
# delete or move what an earlier one wrote (GUARD-002), so an abandoned section stays in
# the tree and in the root index until someone removes it by hand.
if [[ "$fresh" == true && -d "$wiki" ]]; then
  # Refuse the paths where a mistyped GRIMOIRE_WIKI does real damage. A wiki is a
  # directory of its own, and none of these is that.
  case "$wiki" in
    / | "$HOME" | "$HOME"/ | "$root" | "$root"/) die "Refusing to delete $wiki." ;;
  esac

  files="$(find "$wiki" -type f -not -path '*/.git/*' | wc -l | tr -d ' ')"
  commits="$(git -C "$wiki" rev-list --count HEAD 2>/dev/null || echo 0)"

  echo "About to delete $wiki — $files file(s), $commits commit(s) of history."
  [[ "$commits" != "0" ]] && echo "Its history goes with it, and that history is the only undo there is."

  if [[ "$confirmed" != true ]]; then
    # No terminal to ask at means no answer, and no answer means no.
    read -r -p "Type the word yes to delete it: " answer || answer=""
    [[ "$answer" == "yes" ]] || die "Left alone."
  fi

  rm -rf "$wiki"
  echo "Deleted $wiki."
fi

# The wiki is a repository you control, because Grimoire never commits and never takes a
# run back: its history is your undo (docs/product.md §4). A first run needs nothing in it.
if [[ ! -d "$wiki" ]]; then
  mkdir -p "$wiki"
  git -C "$wiki" init --quiet
  echo "Made a wiki at $wiki and put it under git — commit what you want to keep."
elif [[ ! -d "$wiki/.git" ]]; then
  echo "Warning: $wiki is not a git repository, so you have no way to undo what a run writes." >&2
fi

# The hub starts without it; only a dispatched run needs it, and that failure arrives much
# later, when a submission has already been accepted.
command -v claude >/dev/null || echo "Warning: no claude on PATH — a submission will be accepted and its run will fail." >&2

arguments=(--wiki "$wiki" --purpose "$purpose" --model "$GRIMOIRE_MODEL")
[[ -n "${GRIMOIRE_URLS:-}" ]] && arguments+=(--urls "$GRIMOIRE_URLS")
[[ -n "${GRIMOIRE_INSTRUCTION:-}" ]] && arguments+=(--instruction "$instruction")

echo "wiki    $wiki"
echo "purpose $purpose"
echo "model   $GRIMOIRE_MODEL"
echo "open    ${GRIMOIRE_URLS:-http://127.0.0.1:5057}"
echo

exec dotnet run --project "$root/src/Grimoire.Hub" --configuration Release -- "${arguments[@]}"
