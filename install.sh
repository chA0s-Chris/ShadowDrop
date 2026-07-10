#!/bin/sh

set -eu

fail() {
    printf '%s\n' "ShadowDrop installer: $*" >&2
    exit 1
}

cleanup() {
    if [ -n "${install_stage:-}" ]; then
        rm -f "$install_stage"
    fi
    if [ -n "${staging_dir:-}" ]; then
        rm -rf "$staging_dir"
    fi
}

trap cleanup 0 HUP INT TERM

install_dir=${HOME:+$HOME/.local/bin}

while [ "$#" -gt 0 ]; do
    case "$1" in
        --install-dir)
            [ "$#" -ge 2 ] || fail "--install-dir requires a directory"
            [ -n "$2" ] || fail "--install-dir requires a non-empty directory"
            install_dir=$2
            shift 2
            ;;
        --install-dir=*)
            install_dir=${1#*=}
            [ -n "$install_dir" ] || fail "--install-dir requires a non-empty directory"
            shift
            ;;
        --)
            shift
            [ "$#" -eq 0 ] || fail "unexpected argument: $1"
            ;;
        *)
            fail "unknown argument: $1"
            ;;
    esac
done

[ -n "$install_dir" ] || fail "HOME is not set; use --install-dir"
command -v curl >/dev/null 2>&1 || fail "curl is required"

if command -v sha256sum >/dev/null 2>&1; then
    checksum_tool=sha256sum
elif command -v shasum >/dev/null 2>&1; then
    checksum_tool=shasum
else
    fail "sha256sum or shasum is required"
fi

os=${SHADOWDROP_INSTALLER_OS:-$(uname -s)}
arch=${SHADOWDROP_INSTALLER_ARCH:-$(uname -m)}

case "$os" in
    Linux)
        platform=linux
        ;;
    Darwin)
        platform=osx
        ;;
    *)
        fail "unsupported operating system: $os"
        ;;
esac

case "$arch" in
    x86_64|amd64)
        architecture=x64
        ;;
    aarch64|arm64)
        architecture=arm64
        ;;
    *)
        fail "unsupported architecture: $arch"
        ;;
esac

rid="$platform-$architecture"
download_url=${SHADOWDROP_INSTALLER_DOWNLOAD_URL:-https://github.com/chA0s-Chris/ShadowDrop/releases/latest/download}
download_url=${download_url%/}

mkdir -p "$install_dir" || fail "could not create install directory: $install_dir"
staging_dir=$(mktemp -d "${TMPDIR:-/tmp}/shadowdrop-install.XXXXXX") || fail "could not create staging directory"

checksum_file="$staging_dir/CHECKSUMS.sha256"
if ! curl -fsSL -o "$checksum_file" "$download_url/CHECKSUMS.sha256"; then
    fail "could not download CHECKSUMS.sha256 from the latest release"
fi

carriage_return=$(printf '\r')
binary_name=
expected_hash=
while IFS= read -r manifest_line || [ -n "$manifest_line" ]; do
    manifest_line=${manifest_line%"$carriage_return"}
    entry_hash=${manifest_line%% *}
    entry_name=${manifest_line#"$entry_hash"}
    while [ "${entry_name# }" != "$entry_name" ]; do
        entry_name=${entry_name# }
    done
    entry_name=${entry_name#\*}
    case "$entry_name" in
        *"-$rid") ;;
        *) continue ;;
    esac
    [ "${#entry_hash}" -eq 64 ] || continue
    case "$entry_hash" in
        *[!0-9A-Fa-f]*) continue ;;
    esac
    [ -z "$binary_name" ] || fail "CHECKSUMS.sha256 has multiple entries ending in -$rid"
    binary_name=$entry_name
    expected_hash=$(printf '%s' "$entry_hash" | tr '[:upper:]' '[:lower:]')
done < "$checksum_file"
[ -n "$binary_name" ] || fail "CHECKSUMS.sha256 has no entry ending in -$rid"

binary_file="$staging_dir/binary"
if ! curl -fsSL -o "$binary_file" "$download_url/$binary_name"; then
    fail "could not download release asset: $binary_name"
fi

if [ "$checksum_tool" = sha256sum ]; then
    actual_hash=$(sha256sum "$binary_file") || fail "could not compute the SHA-256 of $binary_name"
else
    actual_hash=$(shasum -a 256 "$binary_file") || fail "could not compute the SHA-256 of $binary_name"
fi
actual_hash=${actual_hash%% *}
[ "$actual_hash" = "$expected_hash" ] || fail "checksum verification failed for $binary_name"

install_stage=$(mktemp "$install_dir/.shadowdrop.XXXXXX") || fail "could not create staged executable in $install_dir"
cp "$binary_file" "$install_stage" || fail "could not stage ShadowDrop executable"
chmod 755 "$install_stage" || fail "could not make staged ShadowDrop executable"
target="$install_dir/shadowdrop"
mv -f "$install_stage" "$target" || fail "could not replace $target"
install_stage=""

path_contains_install_dir=$(
    IFS=:
    set -f
    found=0
    normalized_target=$install_dir
    while [ "$normalized_target" != / ] && [ "${normalized_target%/}" != "$normalized_target" ]; do
        normalized_target=${normalized_target%/}
    done
    for path_entry in ${PATH:-}; do
        while [ "$path_entry" != / ] && [ "${path_entry%/}" != "$path_entry" ]; do
            path_entry=${path_entry%/}
        done
        if [ "$path_entry" = "$normalized_target" ]; then
            found=1
            break
        fi
    done
    printf '%s\n' "$found"
)
if [ "$path_contains_install_dir" -ne 1 ]; then
    printf '%s\n' "Warning: $install_dir is not in PATH" >&2
fi

printf '%s\n' "Installed ShadowDrop at $target"
"$target" --version
