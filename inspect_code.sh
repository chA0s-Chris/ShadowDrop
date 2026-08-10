#!/bin/bash
#
#
set -euo pipefail

CACHES_HOME="tmp/inspectcode-caches"
REPORT_FILE="tmp/inspectcode-report.txt"

# `inspectcode` reports semantic issues, not formatting ones, and its rule set only covers C#
# in this solution. Formatting stays the job of cleanup_code.sh.
ARGUMENTS=(--caches-home="${CACHES_HOME}"
           --format=Text
           --absolute-paths
           --output="${REPORT_FILE}"
           --verbosity=WARN)

INSPECT_ALL=0

# Accept --all in any position: inspectcode ignores unknown options without a word, so a forwarded
# --all would silently inspect the changed files instead of the solution.
for argument in "$@"; do
    if [ "${argument}" = "--all" ]; then
        INSPECT_ALL=1
    else
        ARGUMENTS+=("${argument}")
    fi
done

if [ "${INSPECT_ALL}" -eq 0 ]; then
    # Untracked files are included for the same reason cleanup_code.sh includes them: a new file is
    # not staged yet when this script is normally run, and it is the most likely to have findings.
    PATTERNS=$({ git diff --name-only --diff-filter=ACM; git diff --name-only --cached --diff-filter=ACM; git ls-files --others --exclude-standard; } | { grep '\.cs$' | sort -u | sed 's|^|**/|' | paste -sd ';' || true; })

    # Without --include, inspectcode analyzes the whole solution, so an empty file set must not
    # simply be passed through.
    if [ -z "${PATTERNS}" ]; then
        echo "No matching files to process."
        exit 0
    fi

    ARGUMENTS+=(--include="${PATTERNS}")
fi

mkdir -p "$(dirname "${REPORT_FILE}")"

dotnet jb inspectcode "${ARGUMENTS[@]}" ShadowDrop.slnx

# A report without findings still contains the solution header line, so look for actual
# `<file>:<line> <description>` entries instead of testing the file for emptiness.
if grep -qE ':[0-9]+ ' "${REPORT_FILE}"; then
    cat "${REPORT_FILE}"
else
    echo "No issues found."
fi
