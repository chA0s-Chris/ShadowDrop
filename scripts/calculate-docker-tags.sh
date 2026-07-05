#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 1 || $# -gt 2 ]]; then
  echo "Usage: $0 <semver> [docker-image]" >&2
  exit 2
fi

version="$1"
image="${2:-chaos/shadowdrop}"

# Whether a stable release also moves the floating latest/MAJOR/MAJOR.MINOR tags. Set to false when
# releasing an older version out of order so those tags are not dragged backward. Pre-releases never
# receive floating tags regardless of this flag.
include_floating="${INCLUDE_FLOATING_TAGS:-true}"

if [[ "$include_floating" != "true" && "$include_floating" != "false" ]]; then
  echo "INCLUDE_FLOATING_TAGS must be 'true' or 'false' (got '$include_floating')." >&2
  exit 2
fi

if [[ ! "$version" =~ ^([0-9]+)\.([0-9]+)\.([0-9]+)(-[0-9A-Za-z.-]+)?$ ]]; then
  echo "'$version' is not a valid semantic version (expected MAJOR.MINOR.PATCH with an optional -prerelease suffix)." >&2
  exit 1
fi

major="${BASH_REMATCH[1]}"
minor="${BASH_REMATCH[2]}"
prerelease_suffix="${BASH_REMATCH[4]}"

is_prerelease=false
docker_tags=("${image}:${version}")

if [[ -n "$prerelease_suffix" ]]; then
  is_prerelease=true
elif [[ "$include_floating" == "true" ]]; then
  docker_tags+=("${image}:latest")
  docker_tags+=("${image}:${major}.${minor}")
  docker_tags+=("${image}:${major}")
fi

# Local image produced by the NUKE pipeline (DockerImageRepository = "shadowdrop"); the release
# workflow retags this to the Docker Hub repository before pushing.
source_image="shadowdrop:${version}"

printf 'is_prerelease=%s\n' "$is_prerelease"
printf 'git_tag=v%s\n' "$version"
printf 'release_name=v%s\n' "$version"
printf 'source_image=%s\n' "$source_image"
printf 'docker_repository=%s\n' "$image"
printf 'docker_image_tag=%s:%s\n' "$image" "$version"
printf 'docker_hub_url=https://hub.docker.com/r/%s\n' "$image"
printf 'docker_tags<<SHADOWDROP_DOCKER_TAGS\n'
printf '%s\n' "${docker_tags[@]}"
printf 'SHADOWDROP_DOCKER_TAGS\n'
