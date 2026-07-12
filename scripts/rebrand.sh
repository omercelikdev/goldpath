#!/usr/bin/env bash
# The living rename tool: rebrand.sh <OldName> <NewName> <OldRuleCode> <NewRuleCode>
#   e.g. rebrand.sh Goldpath Newname GP NN
# Renames the product across the WHOLE repo: file contents (Pascal/lower/UPPER case
# variants + analyzer rule ids like GP1701) and file/directory names. Idempotent by
# construction (a second run finds nothing to change). The gate set is the proof:
# run build + tests + format + spec corpus afterwards — the goldens make a half-done
# rename impossible to miss. Until the first NuGet publish a rename costs minutes;
# after it, published package ids stay behind (deprecation, not deletion) — decide
# the name before you publish.
set -euo pipefail

OLD=${1:?usage: rebrand.sh <OldName> <NewName> <OldRuleCode> <NewRuleCode>}
NEW=${2:?usage: rebrand.sh <OldName> <NewName> <OldRuleCode> <NewRuleCode>}
OLD_CODE=${3:?usage: rebrand.sh <OldName> <NewName> <OldRuleCode> <NewRuleCode>}
NEW_CODE=${4:?usage: rebrand.sh <OldName> <NewName> <OldRuleCode> <NewRuleCode>}
ROOT=$(cd "$(dirname "$0")/.." && pwd)
cd "$ROOT"

python3 - "$OLD" "$NEW" "$OLD_CODE" "$NEW_CODE" <<'PY'
import os, re, sys

old, new, old_code, new_code = sys.argv[1:5]
pairs = [
    (re.compile(rf'\b{re.escape(old_code)}(\d{{4}})\b'), rf'{new_code}\1'),   # rule ids first
    (re.compile(re.escape(old)), new),                                        # PascalCase
    (re.compile(re.escape(old.lower())), new.lower()),                        # lowercase
    (re.compile(re.escape(old.upper())), new.upper()),                        # UPPERCASE leftovers
]

SKIP_DIRS = {'.git', 'bin', 'obj', 'StrykerOutput', 'node_modules'}
changed_files = 0

for root, dirs, files in os.walk('.'):
    dirs[:] = [d for d in dirs if d not in SKIP_DIRS]
    for fn in files:
        path = os.path.join(root, fn)
        try:
            text = open(path, encoding='utf-8').read()
        except (UnicodeDecodeError, OSError):
            continue
        out = text
        for pattern, repl in pairs:
            out = pattern.sub(repl, out)
        if out != text:
            open(path, 'w', encoding='utf-8').write(out)
            changed_files += 1

# File/directory names: deepest first so parents rename after children.
renames = 0
entries = []
for root, dirs, files in os.walk('.'):
    dirs[:] = [d for d in dirs if d not in SKIP_DIRS]
    for name in files + dirs:
        entries.append(os.path.join(root, name))
for path in sorted(entries, key=lambda p: -p.count(os.sep)):
    base = os.path.basename(path)
    target = base
    for pattern, repl in pairs:
        target = pattern.sub(repl, target)
    if target != base and os.path.exists(path):
        os.rename(path, os.path.join(os.path.dirname(path), target))
        renames += 1

print(f'rebrand: {changed_files} files rewritten, {renames} paths renamed — now run the gates')
PY
