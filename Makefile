# Grimoire — build, test and local run.
#
# This file is a thin wrapper around the commands already documented in CLAUDE.md,
# specs/001-source-ingest-agent-run/quickstart.md and .github/workflows/ci.yml. It exists for two
# reasons that are not obvious from any of those:
#
#  1. The build order is load-bearing. The hub spawns src/agentrun/dist/main.js and the C# fixtures
#     spawn tests/scripted-model/dist/server.js — not the TypeScript sources. A stale dist/ means
#     the suite tests the previous runner and says nothing about the change under test. Every test
#     target here therefore depends on the builds it needs.
#
#  2. `dotnet test` reports "Zero tests ran" on this SDK. .NET 10 dropped VSTest for
#     Microsoft.Testing.Platform; the opt-in is wired (test.runner in global.json plus
#     UseMicrosoftTestingPlatformRunner in tests/Directory.Build.props) and the binaries are proper
#     MTP test applications, but the orchestrator gets nothing back. `make test-cs` runs the built
#     test applications directly instead.
#
# There is deliberately no `deploy` target: src/egress/ and the container (Phases 4-6 of
# specs/001-source-ingest-agent-run/tasks.md) are not built yet, and a target that deploys nothing
# would be a claim this repository cannot honour.

CONFIG ?= Debug

# Extra arguments passed to a single suite, e.g.
#   make test-tasks ARGS='--filter-class "*SqliteStoreTests*"'
#   make test-runner ARGS='tests/agentrun/loop.test.ts'
ARGS ?=

# The hub's environment (contracts/deployment.md). Read from .env if present, so no token ever
# reaches shell history; `make dev-setup` writes a starter one. A line here loses to one passed on
# the command line (make run GRIMOIRE_MODEL_TOKEN=...) and wins over one already in the shell.
-include .env

ASPNETCORE_URLS ?= http://127.0.0.1:5099
ASPNETCORE_WEBROOT ?= $(CURDIR)/frontend/build
GRIMOIRE_INSTRUCTION ?= $(CURDIR)/src/instructions/ingest.md

# tests/ mirrors the slices, so a C# suite is a tests/<slice>/ holding both a project and tests,
# and its test application is named after that project. Derived rather than listed: a new slice
# needs no edit here; tests/support/ (shared helpers, no project) stays out; and the two slices
# whose tests are not written yet — deployment, egress — stay out until they have one, instead of
# failing the run with MTP's "zero tests" exit.
CS_PROJECTS := $(wildcard tests/*/*.csproj)
CS_SUITES := $(sort $(foreach p,$(CS_PROJECTS),\
  $(if $(wildcard $(dir $(p))*.cs),$(patsubst tests/%/,%,$(dir $(p))))))
suite_bin = tests/$(1)/bin/$(CONFIG)/net10.0/$(basename $(notdir $(wildcard tests/$(1)/*.csproj)))
CS_BINS := $(foreach s,$(CS_SUITES),$(call suite_bin,$(s)))

NPM_WORKSPACES := src/agentrun tests/scripted-model frontend

.DEFAULT_GOAL := help

# ---------------------------------------------------------------- help

.PHONY: help
help:
	@echo 'Grimoire'
	@echo
	@echo '  make build           dotnet, runner, scripted model, frontend'
	@echo '  make test            everything below, in CI order'
	@echo '  make test-cs         the C# suites ($(CS_SUITES))'
	@echo '  make test-<slice>    one C# suite, e.g. make test-hub ARGS='"'"'--filter-method "*Readyz*"'"'"''
	@echo '  make test-runner     the runner: real SDK loop against the scripted model'
	@echo '  make test-e2e        Playwright over the three surfaces (needs Node 20.19+)'
	@echo '  make lint            eslint + dependency-cruiser in each npm workspace'
	@echo '  make run             the hub, on $(ASPNETCORE_URLS)'
	@echo '  make dev-setup       a scratch wiki repository and a starter .env, for make run'
	@echo '  make clean           build output; leaves node_modules and .local/'
	@echo
	@echo 'CONFIG=$(CONFIG) (set CONFIG=Release to match CI).'
	@echo 'No deploy target: the egress proxy and the container are not built yet.'

# ---------------------------------------------------------------- build

.PHONY: build build-hub build-runner build-model build-frontend deps
build: build-hub build-runner build-model build-frontend

build-hub:
	dotnet build --configuration $(CONFIG)

build-runner: src/agentrun/dist/main.js
build-model: tests/scripted-model/dist/server.js
build-frontend: frontend/build/index.html

deps: $(addsuffix /node_modules,$(NPM_WORKSPACES))

# npm ci wipes node_modules, so it runs only when the lockfile is newer than the install.
%/node_modules: %/package-lock.json
	npm --prefix $* ci
	@touch $@

src/agentrun/dist/main.js: $(shell find src/agentrun/src -name '*.ts' 2>/dev/null) src/agentrun/tsconfig.json | src/agentrun/node_modules
	npm --prefix src/agentrun run build

tests/scripted-model/dist/server.js: $(shell find tests/scripted-model/src -name '*.ts' 2>/dev/null) tests/scripted-model/tsconfig.json | tests/scripted-model/node_modules
	npm --prefix tests/scripted-model run build

frontend/build/index.html: $(shell find frontend/src -type f 2>/dev/null) frontend/package.json contracts/hub-api.openapi.yaml | frontend/node_modules
	@$(MAKE) --no-print-directory require-node-20
	npm --prefix frontend run build

# vite and dependency-cruiser need Node 20.19+; the runner suite and the scripted model happen to
# work on 18. Fail with the reason rather than with vite's stack trace.
.PHONY: require-node-20
require-node-20:
	@node -e 'const [a,b]=process.versions.node.split(".").map(Number); \
	  if (a < 20 || (a === 20 && b < 19)) { \
	    console.error("This target needs Node 20.19+ (the project targets 22); this is " + process.versions.node + "."); \
	    process.exit(1); }'

# ---------------------------------------------------------------- test

.PHONY: test test-cs test-runner test-e2e
test: test-cs test-runner test-e2e

# The C# suites run serially by constitutional rule: configuration is process environment and a run
# holds the wiki working tree, so two hubs cannot coexist. A failing suite does not stop the rest —
# one pass, one report.
test-cs: build-hub build-runner build-model
	@failed=""; \
	for bin in $(CS_BINS); do \
	  echo "==> $$bin"; \
	  "$$bin" || failed="$$failed $$(basename $$bin)"; \
	done; \
	if [ -n "$$failed" ]; then echo; echo "FAILED:$$failed" >&2; exit 1; fi

test-%: build-hub build-runner build-model
	@bin='$(call suite_bin,$*)'; \
	if [ ! -x "$$bin" ]; then \
	  echo "No C# suite '$*'. Have: $(CS_SUITES)" >&2; exit 2; \
	fi; \
	echo "==> $$bin $(ARGS)"; \
	"$$bin" $(ARGS)

# Covers tests/agentrun/ plus the TypeScript half of the architecture gates (quality gate 1).
# Every test spawns a real runner process against the scripted model; there is no Anthropic key.
test-runner: build-runner
	npx --prefix src/agentrun vitest run $(ARGS) --root .

test-e2e: build-hub build-runner build-model build-frontend
	npm --prefix frontend run test:e2e

.PHONY: lint
lint: deps require-node-20
	@for ws in $(NPM_WORKSPACES); do \
	  echo "==> $$ws"; \
	  npm --prefix $$ws run lint || exit 1; \
	  npm --prefix $$ws run depcruise --if-present || exit 1; \
	done

# ---------------------------------------------------------------- run

# The hub's environment is exported to this target alone. Every suite builds its own environment
# from scratch — the fixtures set what a run needs and nothing inherits — so a .env aimed at a
# laptop must not reach a test hub through the recipe environment and quietly change what the suite
# proves.
run: export GRIMOIRE_WIKI_REPO := $(GRIMOIRE_WIKI_REPO)
run: export GRIMOIRE_STATE_DB := $(GRIMOIRE_STATE_DB)
run: export GRIMOIRE_INSTRUCTION := $(GRIMOIRE_INSTRUCTION)
run: export GRIMOIRE_RUN_MAX_TOOL_CALLS := $(GRIMOIRE_RUN_MAX_TOOL_CALLS)
run: export GRIMOIRE_RUN_MAX_ELAPSED_MS := $(GRIMOIRE_RUN_MAX_ELAPSED_MS)
run: export GRIMOIRE_MODEL_BASE_URL := $(GRIMOIRE_MODEL_BASE_URL)
run: export GRIMOIRE_MODEL_TOKEN := $(GRIMOIRE_MODEL_TOKEN)
run: export GRIMOIRE_FETCH_PROXY := $(GRIMOIRE_FETCH_PROXY)
run: export ASPNETCORE_URLS := $(ASPNETCORE_URLS)
run: export ASPNETCORE_WEBROOT := $(ASPNETCORE_WEBROOT)

# The hub serves /api and the built frontend on one origin. Its whole configuration is environment
# variables, read once at the composition root, and a missing required one fails startup loudly —
# so check the four required ones here and name them, rather than let the hub exit on the first.
.PHONY: run
run: build-hub build-runner
	@missing=""; \
	for v in GRIMOIRE_WIKI_REPO GRIMOIRE_STATE_DB GRIMOIRE_MODEL_BASE_URL GRIMOIRE_MODEL_TOKEN; do \
	  eval "val=\$$$$v"; [ -n "$$val" ] || missing="$$missing $$v"; \
	done; \
	if [ -n "$$missing" ]; then \
	  echo "Not set:$$missing" >&2; \
	  echo "Put them in .env (see contracts/deployment.md), or run: make dev-setup" >&2; \
	  exit 1; \
	fi; \
	if ! git -C "$(GRIMOIRE_WIKI_REPO)" rev-parse HEAD >/dev/null 2>&1; then \
	  echo "GRIMOIRE_WIKI_REPO=$(GRIMOIRE_WIKI_REPO) is not a git repository with a commit." >&2; \
	  echo "The wiki must have at least one commit before the first ingest." >&2; \
	  exit 1; \
	fi; \
	[ -f "$(ASPNETCORE_WEBROOT)/index.html" ] || \
	  echo "note: no built frontend at $(ASPNETCORE_WEBROOT) — /api answers, the surfaces 404. 'make build-frontend' needs Node 20.19+." >&2
	dotnet run --project src/hub --configuration $(CONFIG) --no-build

# A wiki repository and a state database under .local/, plus a starter .env. Idempotent, and it
# never overwrites an existing .env.
.PHONY: dev-setup
dev-setup:
	@mkdir -p .local
	@if ! git -C .local/wiki rev-parse HEAD >/dev/null 2>&1; then \
	  git init -q -b main .local/wiki; \
	  git -C .local/wiki commit -q --allow-empty -m "Initialise the wiki"; \
	  echo "created .local/wiki"; \
	else echo ".local/wiki already exists"; fi
	@if [ -f .env ]; then echo ".env already exists, left alone"; else \
	  printf '%s\n' \
	    '# The hub reads these once at the composition root.' \
	    '# Full contract: specs/001-source-ingest-agent-run/contracts/deployment.md' \
	    '#' \
	    '# Parsed by make, so: no quotes around values, and no "#" inside one. To override a line' \
	    '# for a single command, pass it on the command line: make run GRIMOIRE_MODEL_TOKEN=...' \
	    'GRIMOIRE_WIKI_REPO=$(CURDIR)/.local/wiki' \
	    'GRIMOIRE_STATE_DB=$(CURDIR)/.local/grimoire.db' \
	    '' \
	    '# In deployment this is the egress proxy, which holds the upstream credential, and the' \
	    '# token below is an opaque internal one. Against the Anthropic API directly — fine on a' \
	    '# laptop, not how the container runs — it is a real key.' \
	    'GRIMOIRE_MODEL_BASE_URL=https://api.anthropic.com' \
	    'GRIMOIRE_MODEL_TOKEN=' \
	    > .env; \
	  echo "wrote .env — fill in GRIMOIRE_MODEL_TOKEN before make run"; fi

# ---------------------------------------------------------------- clean

.PHONY: clean
clean:
	dotnet clean --configuration $(CONFIG) >/dev/null || true
	rm -rf src/agentrun/dist tests/scripted-model/dist
	rm -rf frontend/build frontend/.svelte-kit
	rm -rf test-results playwright-report
