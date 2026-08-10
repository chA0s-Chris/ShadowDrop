#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
subject="${script_dir}/../inspect_code.sh"

work_root="$(mktemp -d)"
trap 'rm -rf "$work_root"' EXIT

# The fake `dotnet` records the invocation instead of running ReSharper, so the tests can assert the
# selected include masks and the forwarded arguments. It also creates the report file the subject
# inspects afterwards.
fake_bin="${work_root}/bin"
mkdir -p "$fake_bin"
cat >"${fake_bin}/dotnet" <<'FAKE_DOTNET'
#!/usr/bin/env bash
set -euo pipefail

printf '%s\n' "$@" >"$FAKE_DOTNET_ARGS"

for argument in "$@"; do
  case "$argument" in
    --output=*) : >"${argument#--output=}" ;;
  esac
done
FAKE_DOTNET
chmod +x "${fake_bin}/dotnet"

args_file="${work_root}/dotnet-args.txt"
repo_count=0
last_status=0
last_output=""
last_args=""
last_invoked=0
current_case=""

fail() {
  echo "FAILED: ${current_case}" >&2
  echo "$1" >&2
  echo "--- subject output ---" >&2
  echo "$last_output" >&2
  echo "--- dotnet arguments ---" >&2
  echo "$last_args" >&2
  exit 1
}

new_repo() {
  repo_count=$((repo_count + 1))
  repo="${work_root}/repo${repo_count}"
  mkdir -p "$repo"
  git -C "$repo" init -b main --quiet
  git -C "$repo" config user.email "tests@shadowdrop.invalid"
  git -C "$repo" config user.name "ShadowDrop Tests"
  git -C "$repo" config commit.gpgsign false
}

write_file() {
  local path="${repo}/$1"
  mkdir -p "$(dirname "$path")"
  printf '%s\n' "$2" >"$path"
}

commit_all() {
  git -C "$repo" add -A
  git -C "$repo" commit --quiet -m "$1"
}

run_subject() {
  rm -f "$args_file"
  set +e
  last_output="$(cd "$repo" && PATH="${fake_bin}:${PATH}" FAKE_DOTNET_ARGS="$args_file" bash "$subject" "$@" 2>&1)"
  last_status=$?
  set -e

  if [[ -f "$args_file" ]]; then
    last_invoked=1
    last_args="$(cat "$args_file")"
  else
    last_invoked=0
    last_args=""
  fi
}

include_mask() {
  printf '%s\n' "$last_args" | sed -n 's/^--include=//p'
}

assert_status() {
  [[ "$last_status" == "$1" ]] || fail "Expected exit status ${1}, got ${last_status}."
}

assert_include() {
  local actual
  actual="$(include_mask)"
  [[ "$actual" == "$1" ]] || fail "Expected include mask '${1}', got '${actual}'."
}

assert_no_include() {
  [[ -z "$(include_mask)" ]] || fail "Expected no include mask, got '$(include_mask)'."
}

assert_argument() {
  printf '%s\n' "$last_args" | grep -qxF -- "$1" || fail "Expected argument '${1}' to be forwarded."
}

assert_no_argument() {
  printf '%s\n' "$last_args" | grep -qxF -- "$1" && fail "Expected argument '${1}' not to be forwarded."
  return 0
}

assert_output_contains() {
  case "$last_output" in
    *"$1"*) ;;
    *) fail "Expected output to contain '${1}'." ;;
  esac
}

assert_not_invoked() {
  [[ "$last_invoked" == "0" ]] || fail "Expected ReSharper not to be invoked."
}

# A branch whose changes are fully committed: additions, modifications, and renames are inspected,
# while deletions, non-C# files, and files the branch never touched are not.
current_case="committed branch changes"
new_repo
write_file "src/Existing.cs" "class Existing;"
write_file "src/Old.cs" "class Old;"
write_file "src/Gone.cs" "class Gone;"
write_file "src/Unrelated.cs" "class Unrelated;"
write_file "docs/notes.md" "notes"
commit_all "initial"
git -C "$repo" switch --quiet -c feature
write_file "src/Added.cs" "class Added;"
write_file "src/Existing.cs" "class Existing { }"
write_file "docs/added.md" "added"
git -C "$repo" mv src/Old.cs src/New.cs
git -C "$repo" rm --quiet src/Gone.cs
commit_all "feature"
run_subject --base main
assert_status 0
assert_include "**/src/Added.cs;**/src/Existing.cs;**/src/New.cs"

# Work in progress on top of those commits is inspected together with the committed diff.
current_case="committed changes combined with the working tree"
write_file "src/Existing.cs" "class Existing { public int Value; }"
write_file "src/Staged.cs" "class Staged;"
git -C "$repo" add src/Staged.cs
write_file "src/Untracked.cs" "class Untracked;"
run_subject --base main
assert_status 0
assert_include "**/src/Added.cs;**/src/Existing.cs;**/src/New.cs;**/src/Staged.cs;**/src/Untracked.cs"

# The equals form has to be recognized as well: inspectcode would ignore a forwarded --base=<rev>
# and silently inspect the working tree instead.
current_case="--base=<revision>"
run_subject --base=main
assert_status 0
assert_include "**/src/Added.cs;**/src/Existing.cs;**/src/New.cs;**/src/Staged.cs;**/src/Untracked.cs"

# Without --base the committed files stay out of the selection.
current_case="default working-tree mode"
run_subject
assert_status 0
assert_include "**/src/Existing.cs;**/src/Staged.cs;**/src/Untracked.cs"

current_case="argument forwarding in base mode"
run_subject --base main -e=WARNING
assert_status 0
assert_argument "-e=WARNING"
assert_no_argument "--base"
assert_no_argument "main"

current_case="argument forwarding in --all mode"
run_subject --all -e=WARNING
assert_status 0
assert_no_include
assert_argument "-e=WARNING"
assert_no_argument "--all"

current_case="--base with no C# changes"
new_repo
write_file "docs/notes.md" "notes"
commit_all "initial"
git -C "$repo" switch --quiet -c docs-only
write_file "docs/notes.md" "more notes"
commit_all "docs"
run_subject --base main
assert_status 0
assert_not_invoked
assert_output_contains "No matching files to process."

current_case="--base on an unchanged tree"
run_subject --base HEAD
assert_status 0
assert_not_invoked
assert_output_contains "No matching files to process."

current_case="--base without a revision"
run_subject --base
assert_status 2
assert_not_invoked
assert_output_contains "--base needs a revision"

current_case="empty --base= revision"
run_subject --base=
assert_status 2
assert_not_invoked
assert_output_contains "--base needs a revision"

current_case="duplicate --base"
run_subject --base main --base HEAD
assert_status 2
assert_not_invoked
assert_output_contains "Only one --base"

current_case="--base combined with --all"
run_subject --base main --all
assert_status 2
assert_not_invoked
assert_output_contains "mutually exclusive"

current_case="unresolvable --base revision"
run_subject --base no-such-revision
assert_status 2
assert_not_invoked
assert_output_contains "Cannot resolve --base revision 'no-such-revision'"

# A resolvable base that shares no history with HEAD has no merge base, so the committed diff cannot
# be computed. Reporting that matters: falling back to the working tree alone would look exactly like
# a clean scoped run, which is also how a shallow clone fails.
current_case="--base without a merge base"
new_repo
write_file "src/Present.cs" "class Present;"
commit_all "initial"
git -C "$repo" switch --quiet --orphan unrelated
write_file "src/Unrelated.cs" "class Unrelated;"
commit_all "unrelated"
git -C "$repo" switch --quiet main
run_subject --base unrelated
assert_status 2
assert_not_invoked
assert_output_contains "Cannot diff 'unrelated...HEAD'"

echo "inspect-code tests passed."
