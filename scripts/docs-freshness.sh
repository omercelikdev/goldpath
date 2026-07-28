#!/usr/bin/env bash
# The freshness gate (ADR-0009, curated class): a relative markdown link to a file that
# does not exist fails CI. This is the structural answer to the module-plan-v1.md class
# of rot — a doc may not reference what is not there.
set -uo pipefail
ROOT=$(cd "$(dirname "$0")/.." && pwd)
python3 - "$ROOT" <<'PY'
import os, re, sys
root = sys.argv[1]
link = re.compile(r"\]\(([^)#?\s]+)(?:#[^)]*)?\)")
scopes = ["README.md", "CLAUDE.md", "docs", "samples", "schemas", "evals",
          "packages", "analyzers", "rulesets", "skills", "tools", "ui"]
broken = []
for scope in scopes:
    full = os.path.join(root, scope)
    files = []
    if os.path.isfile(full):
        files = [full]
    else:
        skip = {"node_modules", "bin", "obj", "dist", ".pnpm"}
        for base, dirs, names in os.walk(full):
            dirs[:] = [d for d in dirs if d not in skip]
            files += [os.path.join(base, n) for n in names if n.endswith(".md")]
    for f in files:
        text = open(f, encoding="utf-8", errors="replace").read()
        for m in link.finditer(text):
            target = m.group(1)
            if target.startswith(("http://", "https://", "mailto:", "/")):
                continue
            resolved = os.path.normpath(os.path.join(os.path.dirname(f), target))
            if not os.path.exists(resolved):
                broken.append(f"{os.path.relpath(f, root)}: -> {target}")
if broken:
    print("── docs freshness: BROKEN relative links:")
    for b in broken:
        print(f"  {b}")
    sys.exit(1)
print("── docs freshness: all relative links resolve")

# RETIRED NAMES. A tool we no longer use may not quietly reappear in the docs, the
# schema or the templates: the strategy documents carried "WireMock" for four months
# after Mockifyr had overtaken it on every axis we care about, and every reader of
# foundation.md was told to reach for it. Removing a name once is a cleanup; keeping it
# gone is a gate. Add a name here the day a decision retires it.
retired = {
    "wiremock": "Mockifyr is the mock system (foundation.md 5.1, decided 2026-07-28)",
}
gate = os.path.abspath(__file__) if "__file__" in dir() else ""
exts = {".md", ".json", ".yaml", ".yml", ".cs", ".ts", ".tsx", ".sh", ".props", ".csproj"}
skip = {"node_modules", "bin", "obj", "dist", ".pnpm", ".git", ".next"}
offences = []
for base, dirs, names in os.walk(root):
    dirs[:] = [d for d in dirs if d not in skip]
    for name in names:
        path = os.path.join(base, name)
        if os.path.splitext(name)[1] not in exts or os.path.samefile(path, os.path.join(root, "scripts/docs-freshness.sh")):
            continue
        text = open(path, encoding="utf-8", errors="replace").read().lower()
        for word, why in retired.items():
            if word in text:
                offences.append(f"{os.path.relpath(path, root)}: '{word}' is retired — {why}")
if offences:
    print("── retired names: a name we stopped using is back:")
    for o in offences:
        print(f"  {o}")
    sys.exit(1)
print("── retired names: none of the retired tools are mentioned")
PY
