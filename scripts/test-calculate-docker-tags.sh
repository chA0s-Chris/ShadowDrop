#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
subject="${script_dir}/calculate-docker-tags.sh"

assert_output() {
  local version="$1"
  local expected="$2"
  local include_floating="${3:-}"
  local actual

  if [[ -n "$include_floating" ]]; then
    actual="$(INCLUDE_FLOATING_TAGS="$include_floating" bash "$subject" "$version")"
  else
    actual="$(bash "$subject" "$version")"
  fi

  if [[ "$actual" == "$expected" ]]; then
    return
  fi

  echo "Unexpected output for version '${version}' (INCLUDE_FLOATING_TAGS='${include_floating:-<unset>}')." >&2
  diff -u <(printf '%s\n' "$expected") <(printf '%s\n' "$actual") >&2
  exit 1
}

assert_invalid() {
  local version="$1"

  if bash "$subject" "$version" >/dev/null 2>&1; then
    echo "Expected version '${version}' to be rejected." >&2
    exit 1
  fi
}

assert_invalid_floating() {
  local include_floating="$1"

  if INCLUDE_FLOATING_TAGS="$include_floating" bash "$subject" "1.2.3" >/dev/null 2>&1; then
    echo "Expected INCLUDE_FLOATING_TAGS='${include_floating}' to be rejected." >&2
    exit 1
  fi
}

assert_output "1.2.3" "$(cat <<'EXPECTED'
is_prerelease=false
git_tag=v1.2.3
release_name=v1.2.3
source_image=shadowdrop:1.2.3
docker_repository=chaos/shadowdrop
docker_image_tag=chaos/shadowdrop:1.2.3
docker_hub_url=https://hub.docker.com/r/chaos/shadowdrop
docker_tags<<SHADOWDROP_DOCKER_TAGS
chaos/shadowdrop:1.2.3
chaos/shadowdrop:latest
chaos/shadowdrop:1.2
chaos/shadowdrop:1
SHADOWDROP_DOCKER_TAGS
EXPECTED
)"

assert_output "1.2.3-rc.1" "$(cat <<'EXPECTED'
is_prerelease=true
git_tag=v1.2.3-rc.1
release_name=v1.2.3-rc.1
source_image=shadowdrop:1.2.3-rc.1
docker_repository=chaos/shadowdrop
docker_image_tag=chaos/shadowdrop:1.2.3-rc.1
docker_hub_url=https://hub.docker.com/r/chaos/shadowdrop
docker_tags<<SHADOWDROP_DOCKER_TAGS
chaos/shadowdrop:1.2.3-rc.1
SHADOWDROP_DOCKER_TAGS
EXPECTED
)"

assert_output "10.20.30" "$(cat <<'EXPECTED'
is_prerelease=false
git_tag=v10.20.30
release_name=v10.20.30
source_image=shadowdrop:10.20.30
docker_repository=chaos/shadowdrop
docker_image_tag=chaos/shadowdrop:10.20.30
docker_hub_url=https://hub.docker.com/r/chaos/shadowdrop
docker_tags<<SHADOWDROP_DOCKER_TAGS
chaos/shadowdrop:10.20.30
chaos/shadowdrop:latest
chaos/shadowdrop:10.20
chaos/shadowdrop:10
SHADOWDROP_DOCKER_TAGS
EXPECTED
)"

# A stable release can suppress the floating latest/MAJOR/MAJOR.MINOR tags when publishing an older
# version out of order.
assert_output "1.2.3" "$(cat <<'EXPECTED'
is_prerelease=false
git_tag=v1.2.3
release_name=v1.2.3
source_image=shadowdrop:1.2.3
docker_repository=chaos/shadowdrop
docker_image_tag=chaos/shadowdrop:1.2.3
docker_hub_url=https://hub.docker.com/r/chaos/shadowdrop
docker_tags<<SHADOWDROP_DOCKER_TAGS
chaos/shadowdrop:1.2.3
SHADOWDROP_DOCKER_TAGS
EXPECTED
)" "false"

# Explicitly enabling floating tags matches the default behavior.
assert_output "1.2.3" "$(cat <<'EXPECTED'
is_prerelease=false
git_tag=v1.2.3
release_name=v1.2.3
source_image=shadowdrop:1.2.3
docker_repository=chaos/shadowdrop
docker_image_tag=chaos/shadowdrop:1.2.3
docker_hub_url=https://hub.docker.com/r/chaos/shadowdrop
docker_tags<<SHADOWDROP_DOCKER_TAGS
chaos/shadowdrop:1.2.3
chaos/shadowdrop:latest
chaos/shadowdrop:1.2
chaos/shadowdrop:1
SHADOWDROP_DOCKER_TAGS
EXPECTED
)" "true"

# Pre-releases never receive floating tags, so the toggle does not change their output.
assert_output "1.2.3-rc.1" "$(cat <<'EXPECTED'
is_prerelease=true
git_tag=v1.2.3-rc.1
release_name=v1.2.3-rc.1
source_image=shadowdrop:1.2.3-rc.1
docker_repository=chaos/shadowdrop
docker_image_tag=chaos/shadowdrop:1.2.3-rc.1
docker_hub_url=https://hub.docker.com/r/chaos/shadowdrop
docker_tags<<SHADOWDROP_DOCKER_TAGS
chaos/shadowdrop:1.2.3-rc.1
SHADOWDROP_DOCKER_TAGS
EXPECTED
)" "false"

assert_invalid "1.2"
assert_invalid "1.2.3+build.1"
assert_invalid_floating "yes"

echo "calculate-docker-tags tests passed."
