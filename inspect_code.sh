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
BASE_COUNT=0
BASE_REVISION=""
AWAITING_BASE_REVISION=0

# --all and --base are script-owned modes and are accepted in any position: inspectcode ignores an
# unknown option, so a forwarded --all would silently inspect the changed files instead of the
# solution, and a forwarded --base=<revision> would silently inspect the working tree.
for argument in "$@"; do
    if [ "${AWAITING_BASE_REVISION}" -eq 1 ]; then
        BASE_REVISION="${argument}"
        AWAITING_BASE_REVISION=0
        continue
    fi

    case "${argument}" in
        --all)
            INSPECT_ALL=1
            ;;
        --base)
            BASE_COUNT=$((BASE_COUNT + 1))
            AWAITING_BASE_REVISION=1
            ;;
        --base=*)
            BASE_COUNT=$((BASE_COUNT + 1))
            BASE_REVISION="${argument#--base=}"
            ;;
        *)
            ARGUMENTS+=("${argument}")
            ;;
    esac
done

if [ "${BASE_COUNT}" -gt 1 ]; then
    echo "Only one --base <revision> is allowed." >&2
    exit 2
fi

if [ "${AWAITING_BASE_REVISION}" -eq 1 ] || { [ "${BASE_COUNT}" -eq 1 ] && [ -z "${BASE_REVISION}" ]; }; then
    echo "--base needs a revision, for example: ./inspect_code.sh --base main" >&2
    exit 2
fi

if [ "${BASE_COUNT}" -eq 1 ] && [ "${INSPECT_ALL}" -eq 1 ]; then
    echo "--base and --all are mutually exclusive: --base inspects a diff, --all the whole solution." >&2
    exit 2
fi

BASE_COMMIT=""

if [ "${BASE_COUNT}" -eq 1 ]; then
    # Resolving the revision here turns an unusable base into a diagnostic instead of an empty diff
    # that looks like a clean inspection.
    if ! BASE_COMMIT=$(git rev-parse --verify --quiet "${BASE_REVISION}^{commit}"); then
        echo "Cannot resolve --base revision '${BASE_REVISION}' to a commit." >&2
        exit 2
    fi
fi

if [ "${INSPECT_ALL}" -eq 0 ]; then
    # Untracked files are included for the same reason cleanup_code.sh includes them: a new file is
    # not staged yet when this script is normally run, and it is the most likely to have findings.
    # With --base, the committed changes of a branch or stack layer join that selection. A deletion
    # leaves nothing to inspect, and a copy arrives as an addition because Git does not detect copies
    # unless it is asked to. A committed rename is inspected under its new path; a merely staged one
    # is not, because the working-tree selection keeps the narrower filter it has always used.
    COMMITTED_PATHS=""

    if [ -n "${BASE_COMMIT}" ]; then
        # Kept out of the group below, which reports the status of its last command only: a failing
        # diff would be masked there and silently narrow the inspection to the working tree while
        # still looking like a clean scoped run.
        if ! COMMITTED_PATHS=$(git diff --name-only --diff-filter=ACMR "${BASE_COMMIT}...HEAD"); then
            echo "Cannot diff '${BASE_REVISION}...HEAD'. A shallow clone has no merge base; fetch more history." >&2
            exit 2
        fi
    fi

    PATTERNS=$({ if [ -n "${COMMITTED_PATHS}" ]; then printf '%s\n' "${COMMITTED_PATHS}"; fi
                 git diff --name-only --diff-filter=ACM
                 git diff --name-only --cached --diff-filter=ACM
                 git ls-files --others --exclude-standard; } | { grep '\.cs$' | sort -u | sed 's|^|**/|' | paste -sd ';' || true; })

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
