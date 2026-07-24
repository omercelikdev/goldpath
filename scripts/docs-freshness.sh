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
PY
