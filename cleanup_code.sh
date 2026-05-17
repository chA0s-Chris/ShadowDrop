#!/bin/bash
#
#
set -euo pipefail

PATTERNS=$({ git diff --name-only --diff-filter=ACM; git diff --name-only --cached --diff-filter=ACM; } | { grep '\.\(cs\|csproj\|json\|sh\|slnx\|config\)$' | sort -u | sed 's|^|**/|' | paste -sd ';' || true; })

if [ -n "${PATTERNS}" ]; then
    dotnet jb cleanupcode --include="${PATTERNS}" ShadowDrop.slnx
else
    echo "No matching changed files to process."
fi
