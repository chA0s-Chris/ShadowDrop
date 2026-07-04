#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 1 || $# -gt 2 ]]; then
  echo "Usage: $0 <semver> [docker-image]" >&2
  exit 2
fi

version="$1"
image="${2:-chaos/shadowdrop}"

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
else
  docker_tags+=("${image}:latest")
  docker_tags+=("${image}:${major}.${minor}")
  docker_tags+=("${image}:${major}")
fi

printf 'is_prerelease=%s\n' "$is_prerelease"
printf 'git_tag=v%s\n' "$version"
printf 'release_name=v%s\n' "$version"
printf 'docker_repository=%s\n' "$image"
printf 'docker_image_tag=%s:%s\n' "$image" "$version"
printf 'docker_hub_url=https://hub.docker.com/r/%s\n' "$image"
printf 'docker_tags<<SHADOWDROP_DOCKER_TAGS\n'
printf '%s\n' "${docker_tags[@]}"
printf 'SHADOWDROP_DOCKER_TAGS\n'
