#!/usr/bin/env bash
# The compile-link half of platform RFC §2b risk R-2 (source leakage).
#
# GP2001 catches the loud symptom — a product assembly DECLARING into the Goldpath
# namespace. This catches the quiet one: a .csproj that reaches out of its own repo and
# compiles someone else's source in place. That file never appears as a package reference,
# never shows up in an SBOM, and cannot be updated by taking a new train; the product has
# forked without a single line of its own code changing.
#
# It is a script rather than a Roslyn rule on purpose: the question is "does this path leave
# the repo?", which MSBuild answers exactly and an analyzer can only approximate — a Roslyn
# rule would need the repo root injected as a build property, i.e. more plumbing for a
# weaker answer.
#
#   scripts/product-guard.sh [path-to-repo-root]
set -euo pipefail

ROOT=$(cd "${1:-.}" && pwd)
FOUND=0

# Only Compile items can pull foreign SOURCE in. ProjectReference across repos is a
# different (and louder) mistake that the build itself refuses.
while IFS= read -r project; do
  PROJECT_DIR=$(cd "$(dirname "$project")" && pwd)
  while IFS= read -r include; do
    # The well-known project-anchored properties resolve exactly; anything else is reported
    # rather than skipped. Review #162 caught the first version doing a silent `continue`
    # here, which meant `$(SomeRoot)/../../core/Foo.cs` — a leak spelled with a variable
    # instead of a literal `..` — came back as CLEAN. That is the same false-green shape as
    # the train-freshness skip bug: an unverifiable case must never be indistinguishable
    # from a verified one.
    RAW="${include//\\//}"
    RAW="${RAW//\$(MSBuildThisFileDirectory)/$PROJECT_DIR/}"
    RAW="${RAW//\$(MSBuildProjectDirectory)/$PROJECT_DIR}"
    RAW="${RAW//\$(ProjectDir)/$PROJECT_DIR/}"
    case "$RAW" in
      *'$('*)
        echo "product-guard: ${project#$ROOT/} has a Compile Include this gate cannot resolve:" >&2
        echo "                 $include" >&2
        echo "               An unresolvable path cannot be certified as inside the repo — spell it" >&2
        echo "               relative to the project, or the guard is only pretending to check." >&2
        FOUND=1
        continue ;;
      /*) ABS="$RAW" ;;
      *)  ABS="$PROJECT_DIR/$RAW" ;;
    esac
    # Resolve .. without requiring the file to exist (realpath -m is not on macOS).
    RESOLVED=$(python3 -c "import os,sys; print(os.path.normpath(sys.argv[1]))" "$ABS")
    case "$RESOLVED" in
      "$ROOT"/*|"$ROOT") ;;   # inside the repo — a shared file, not a leak
      *)
        echo "product-guard: ${project#$ROOT/} compiles source from OUTSIDE the repo:" >&2
        echo "                 $RESOLVED" >&2
        FOUND=1 ;;
    esac
  done < <(grep -o '<Compile[^>]*Include="[^"]*"' "$project" 2>/dev/null | sed 's/.*Include="//; s/"$//')
done < <(find "$ROOT" -name '*.csproj' -not -path '*/bin/*' -not -path '*/obj/*')

if [ "$FOUND" -ne 0 ]; then
  echo "product-guard: a product binds to PUBLISHED packages (ADR-0012). Compile-linking core" >&2
  echo "  source is a fork that no train can update — take the dependency, not the file." >&2
  exit 1
fi

echo "product-guard: no .csproj compiles source from outside $ROOT — no source leakage."
