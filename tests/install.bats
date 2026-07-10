#!/usr/bin/env bats

setup() {
    PROJECT_ROOT=$(cd "$BATS_TEST_DIRNAME/.." && pwd)
    TEST_ROOT=$(mktemp -d "${BATS_TMPDIR:-/tmp}/shadowdrop-installer-test.XXXXXX")
    FIXTURES="$TEST_ROOT/fixtures"
    FAKE_BIN="$TEST_ROOT/bin"
    REQUEST_LOG="$TEST_ROOT/requests.log"
    INSTALLER_SOURCE="$PROJECT_ROOT/install.sh"
    DOWNLOAD_BASE=https://fixture.test/download
    INSTALLER_URL=https://fixture.test/install.sh
    ORIGINAL_PATH=$PATH
    mkdir -p "$FIXTURES/assets" "$FAKE_BIN" "$TEST_ROOT/home"
    : > "$REQUEST_LOG"
    HASH_TOOL=$(command -v sha256sum || command -v shasum)
    case "$HASH_TOOL" in
        *sha256sum)
            HASH_TOOL_TYPE=sha256sum
            ;;
        *)
            HASH_TOOL_TYPE=shasum
            ;;
    esac
    export PROJECT_ROOT TEST_ROOT FIXTURES FAKE_BIN REQUEST_LOG INSTALLER_SOURCE DOWNLOAD_BASE INSTALLER_URL ORIGINAL_PATH HASH_TOOL HASH_TOOL_TYPE
    export HOME="$TEST_ROOT/home"
    export SHADOWDROP_INSTALLER_DOWNLOAD_URL="$DOWNLOAD_BASE"
    export SHADOWDROP_INSTALLER_OS=Linux
    export SHADOWDROP_INSTALLER_ARCH=x86_64
    PATH="$FAKE_BIN:$ORIGINAL_PATH"
    export PATH
    write_fake_curl
    write_release_fixtures
}

teardown() {
    rm -rf "$TEST_ROOT"
}

hash_file() {
    if [ "$HASH_TOOL_TYPE" = sha256sum ]; then
        "$HASH_TOOL" "$1" | awk '{ print $1 }'
    else
        "$HASH_TOOL" -a 256 "$1" | awk '{ print $1 }'
    fi
}

write_binary() {
    path=$1
    version=$2
    cat > "$path" <<EOF
#!/bin/sh
[ "\${1:-}" = "--version" ] || exit 64
printf '%s\\n' '$version'
EOF
    chmod 755 "$path"
}

write_fake_curl() {
    cat > "$FAKE_BIN/curl" <<'EOF'
#!/bin/sh
output=
url=
while [ "$#" -gt 0 ]; do
    case "$1" in
        -o)
            output=$2
            shift 2
            ;;
        -* )
            shift
            ;;
        *)
            url=$1
            shift
            ;;
    esac
done
printf '%s\n' "$url" >> "$REQUEST_LOG"
source=
case "$url" in
    "$INSTALLER_URL")
        source=$INSTALLER_SOURCE
        ;;
    "$DOWNLOAD_BASE"/*)
        source=$FIXTURES/assets/${url#"$DOWNLOAD_BASE"/}
        ;;
esac
[ -n "$source" ] && [ -f "$source" ] || exit 22
if [ -n "$output" ]; then
    cp "$source" "$output"
else
    cat "$source"
fi
EOF
    chmod 755 "$FAKE_BIN/curl"
}

write_release_fixtures() {
    write_binary "$FIXTURES/assets/shadowdrop-v9.8.7-linux-x64" "ShadowDrop stable linux-x64"
    write_binary "$FIXTURES/assets/shadowdrop-v9.8.7-linux-arm64" "ShadowDrop stable linux-arm64"
    write_binary "$FIXTURES/assets/shadowdrop-v9.8.7-osx-x64" "ShadowDrop stable osx-x64"
    write_binary "$FIXTURES/assets/shadowdrop-v9.8.7-osx-arm64" "ShadowDrop stable osx-arm64"
    {
        printf '%s  %s\n' "$(hash_file "$FIXTURES/assets/shadowdrop-v9.8.7-linux-x64")" shadowdrop-v9.8.7-linux-x64
        printf '%s  %s\n' "$(hash_file "$FIXTURES/assets/shadowdrop-v9.8.7-linux-arm64")" shadowdrop-v9.8.7-linux-arm64
        printf '%s  %s\n' "$(hash_file "$FIXTURES/assets/shadowdrop-v9.8.7-osx-x64")" shadowdrop-v9.8.7-osx-x64
        printf '%s  %s\n' "$(hash_file "$FIXTURES/assets/shadowdrop-v9.8.7-osx-arm64")" shadowdrop-v9.8.7-osx-arm64
        printf '%064d  %s\n' 0 shadowdrop-v9.8.7-win-x64.exe
        printf '%064d  %s\n' 0 shadowdrop-v9.8.7-win-arm64.exe
    } > "$FIXTURES/assets/CHECKSUMS.sha256"
}

make_fallback_path() {
    fallback=$1
    mkdir -p "$fallback"
    for tool in cat mkdir mktemp rm cp chmod mv tr uname; do
        ln -s "$(command -v "$tool")" "$fallback/$tool"
    done
    cp "$FAKE_BIN/curl" "$fallback/curl"
    cat > "$fallback/shasum" <<'EOF'
#!/bin/sh
if [ "$HASH_TOOL_TYPE" = sha256sum ]; then
    [ "${1:-}" = -a ] && shift 2
    exec "$HASH_TOOL" "$@"
fi
exec "$HASH_TOOL" "$@"
EOF
    chmod 755 "$fallback/curl" "$fallback/shasum"
}

@test "documented curl pipe installs the latest stable release to the user default" {
    PATH="$HOME/.local/bin:$PATH"
    export PATH
    run sh -c 'curl "$INSTALLER_URL" | sh'
    [ "$status" -eq 0 ]
    [[ "$output" == *"Installed ShadowDrop at $HOME/.local/bin/shadowdrop"* ]]
    [[ "$output" == *"ShadowDrop stable linux-x64"* ]]
    [ -x "$HOME/.local/bin/shadowdrop" ]
    grep -Fx "$DOWNLOAD_BASE/CHECKSUMS.sha256" "$REQUEST_LOG"
    grep -Fx "$DOWNLOAD_BASE/shadowdrop-v9.8.7-linux-x64" "$REQUEST_LOG"
}

@test "documented curl pipe with options installs to the override directory" {
    CUSTOM_INSTALL="$TEST_ROOT/custom/bin"
    export CUSTOM_INSTALL
    run sh -c 'curl "$INSTALLER_URL" | sh -s -- --install-dir "$CUSTOM_INSTALL"'
    [ "$status" -eq 0 ]
    [[ "$output" == *"Installed ShadowDrop at $CUSTOM_INSTALL/shadowdrop"* ]]
    [[ "$output" == *"ShadowDrop stable linux-x64"* ]]
    [ -x "$CUSTOM_INSTALL/shadowdrop" ]
}

@test "supported operating systems and architectures map to the required runtime identifiers" {
    for mapping in "Linux x86_64 linux-x64" "Linux amd64 linux-x64" "Linux aarch64 linux-arm64" "Linux arm64 linux-arm64" "Darwin x86_64 osx-x64" "Darwin amd64 osx-x64" "Darwin aarch64 osx-arm64" "Darwin arm64 osx-arm64"; do
        set -- $mapping
        export SHADOWDROP_INSTALLER_OS=$1
        export SHADOWDROP_INSTALLER_ARCH=$2
        destination="$TEST_ROOT/$1-$2"
        run /bin/sh "$INSTALLER_SOURCE" --install-dir "$destination"
        [ "$status" -eq 0 ]
        [[ "$output" == *"ShadowDrop stable $3"* ]]
        [ -x "$destination/shadowdrop" ]
    done
}

@test "checksum mismatch fails without replacing an existing installation and cleans staging" {
    destination="$TEST_ROOT/existing"
    mkdir -p "$destination"
    write_binary "$destination/shadowdrop" "ShadowDrop existing"
    printf '%064d  %s\n' 0 shadowdrop-v9.8.7-linux-x64 > "$FIXTURES/assets/CHECKSUMS.sha256"
    run /bin/sh "$INSTALLER_SOURCE" --install-dir "$destination"
    [ "$status" -ne 0 ]
    [[ "$output" == *"checksum verification failed"* ]]
    run "$destination/shadowdrop" --version
    [ "$status" -eq 0 ]
    [ "$output" = "ShadowDrop existing" ]
    [ -z "$(find "$destination" -name '.shadowdrop.*' -print)" ]
}

@test "a successful update replaces the executable through a staged file and prints its version" {
    destination="$TEST_ROOT/update"
    mkdir -p "$destination"
    write_binary "$destination/shadowdrop" "ShadowDrop old"
    PATH="$destination:$PATH"
    export PATH
    run /bin/sh "$INSTALLER_SOURCE" --install-dir "$destination"
    [ "$status" -eq 0 ]
    [[ "$output" == *"ShadowDrop stable linux-x64"* ]]
    [ -x "$destination/shadowdrop" ]
    [ -z "$(find "$destination" -name '.shadowdrop.*' -print)" ]
}

@test "an install directory outside PATH emits a warning and still prints the version" {
    destination="$TEST_ROOT/not-on-path"
    run /bin/sh "$INSTALLER_SOURCE" --install-dir "$destination"
    [ "$status" -eq 0 ]
    [[ "$output" == *"Warning: $destination is not in PATH"* ]]
    [[ "$output" == *"ShadowDrop stable linux-x64"* ]]
}

@test "PATH comparison ignores trailing installation-directory separators" {
    destination="$TEST_ROOT/on-path"
    PATH="$destination:$PATH"
    export PATH
    run /bin/sh "$INSTALLER_SOURCE" --install-dir "$destination/"
    [ "$status" -eq 0 ]
    [[ "$output" != *"is not in PATH"* ]]
    [[ "$output" == *"ShadowDrop stable linux-x64"* ]]
}

@test "a missing release checksum manifest produces an actionable failure" {
    rm "$FIXTURES/assets/CHECKSUMS.sha256"
    run /bin/sh "$INSTALLER_SOURCE" --install-dir "$TEST_ROOT/missing-release"
    [ "$status" -ne 0 ]
    [[ "$output" == *"could not download CHECKSUMS.sha256 from the latest release"* ]]
}

@test "manifests without exactly one runtime-identifier entry produce actionable failures" {
    printf '%064d  %s\n' 0 shadowdrop-v9.8.7-linux-x64-extra > "$FIXTURES/assets/CHECKSUMS.sha256"
    run /bin/sh "$INSTALLER_SOURCE" --install-dir "$TEST_ROOT/no-entry"
    [ "$status" -ne 0 ]
    [[ "$output" == *"CHECKSUMS.sha256 has no entry ending in -linux-x64"* ]]
    {
        printf '%064d  %s\n' 0 shadowdrop-v9.8.7-linux-x64
        printf '%064d  %s\n' 1 shadowdrop-v9.9.0-linux-x64
    } > "$FIXTURES/assets/CHECKSUMS.sha256"
    run /bin/sh "$INSTALLER_SOURCE" --install-dir "$TEST_ROOT/duplicate-entries"
    [ "$status" -ne 0 ]
    [[ "$output" == *"CHECKSUMS.sha256 has multiple entries ending in -linux-x64"* ]]
}

@test "a manifest entry without a downloadable asset produces an actionable failure" {
    rm "$FIXTURES/assets/shadowdrop-v9.8.7-linux-x64"
    run /bin/sh "$INSTALLER_SOURCE" --install-dir "$TEST_ROOT/missing-asset"
    [ "$status" -ne 0 ]
    [[ "$output" == *"could not download release asset: shadowdrop-v9.8.7-linux-x64"* ]]
}

@test "a CRLF checksum manifest verifies the selected asset" {
    awk '{ printf "%s\r\n", $0 }' "$FIXTURES/assets/CHECKSUMS.sha256" > "$FIXTURES/assets/CHECKSUMS.sha256.crlf"
    mv "$FIXTURES/assets/CHECKSUMS.sha256.crlf" "$FIXTURES/assets/CHECKSUMS.sha256"
    destination="$TEST_ROOT/crlf"
    run /bin/sh "$INSTALLER_SOURCE" --install-dir "$destination"
    [ "$status" -eq 0 ]
    [[ "$output" == *"ShadowDrop stable linux-x64"* ]]
    [ -x "$destination/shadowdrop" ]
}

@test "missing required download and checksum tools are reported" {
    empty_path="$TEST_ROOT/empty-path"
    mkdir "$empty_path"
    run env PATH="$empty_path" SHADOWDROP_INSTALLER_OS=Linux SHADOWDROP_INSTALLER_ARCH=x86_64 /bin/sh "$INSTALLER_SOURCE" --install-dir "$TEST_ROOT/no-curl"
    [ "$status" -ne 0 ]
    [[ "$output" == *"curl is required"* ]]
    curl_only="$TEST_ROOT/curl-only"
    mkdir "$curl_only"
    cp "$FAKE_BIN/curl" "$curl_only/curl"
    chmod 755 "$curl_only/curl"
    run env PATH="$curl_only" SHADOWDROP_INSTALLER_OS=Linux SHADOWDROP_INSTALLER_ARCH=x86_64 /bin/sh "$INSTALLER_SOURCE" --install-dir "$TEST_ROOT/no-hash"
    [ "$status" -ne 0 ]
    [[ "$output" == *"sha256sum or shasum is required"* ]]
}

@test "shasum is used when sha256sum is unavailable" {
    fallback="$TEST_ROOT/fallback"
    make_fallback_path "$fallback"
    run env PATH="$fallback" HOME="$HOME" SHADOWDROP_INSTALLER_DOWNLOAD_URL="$DOWNLOAD_BASE" SHADOWDROP_INSTALLER_OS=Linux SHADOWDROP_INSTALLER_ARCH=x86_64 REQUEST_LOG="$REQUEST_LOG" FIXTURES="$FIXTURES" INSTALLER_SOURCE="$INSTALLER_SOURCE" DOWNLOAD_BASE="$DOWNLOAD_BASE" INSTALLER_URL="$INSTALLER_URL" HASH_TOOL="$HASH_TOOL" HASH_TOOL_TYPE="$HASH_TOOL_TYPE" /bin/sh "$INSTALLER_SOURCE" --install-dir "$TEST_ROOT/shasum"
    [ "$status" -eq 0 ]
    [[ "$output" == *"ShadowDrop stable linux-x64"* ]]
}

@test "--install-dir accepts the equals form" {
    destination="$TEST_ROOT/equals-form"
    run /bin/sh "$INSTALLER_SOURCE" --install-dir="$destination"
    [ "$status" -eq 0 ]
    [[ "$output" == *"ShadowDrop stable linux-x64"* ]]
    [ -x "$destination/shadowdrop" ]
}

@test "an unset HOME without --install-dir produces an actionable failure" {
    run env -u HOME /bin/sh "$INSTALLER_SOURCE"
    [ "$status" -ne 0 ]
    [[ "$output" == *"HOME is not set; use --install-dir"* ]]
}

@test "invalid arguments and unsupported hosts fail before download" {
    run /bin/sh "$INSTALLER_SOURCE" --unknown
    [ "$status" -ne 0 ]
    [[ "$output" == *"unknown argument: --unknown"* ]]
    run /bin/sh "$INSTALLER_SOURCE" --pre-release
    [ "$status" -ne 0 ]
    [[ "$output" == *"unknown argument: --pre-release"* ]]
    run /bin/sh "$INSTALLER_SOURCE" --install-dir
    [ "$status" -ne 0 ]
    [[ "$output" == *"--install-dir requires a directory"* ]]
    export SHADOWDROP_INSTALLER_OS=FreeBSD
    run /bin/sh "$INSTALLER_SOURCE" --install-dir "$TEST_ROOT/freebsd"
    [ "$status" -ne 0 ]
    [[ "$output" == *"unsupported operating system: FreeBSD"* ]]
    export SHADOWDROP_INSTALLER_OS=Linux
    export SHADOWDROP_INSTALLER_ARCH=s390x
    run /bin/sh "$INSTALLER_SOURCE" --install-dir "$TEST_ROOT/s390x"
    [ "$status" -ne 0 ]
    [[ "$output" == *"unsupported architecture: s390x"* ]]
}
