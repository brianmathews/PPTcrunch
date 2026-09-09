#!/usr/bin/env bash
set -euo pipefail

# This installer ships one executable; only macOS-provided dylibs may remain
# external. Reject Homebrew, MacPorts, and unresolved loader-relative paths.
binary="${1:?Usage: verify-macos-dependencies.sh /path/to/pptcrunch}"
dependencies="$(otool -L "$binary")"
unexpected="$(printf '%s\n' "$dependencies" | sed '1d' | \
    sed -E 's/^[[:space:]]+//; s/ \(compatibility version.*$//' | \
    awk '!/^\/System\/Library\// && !/^\/usr\/lib\// && NF')"

if [ -n "$unexpected" ]; then
    printf 'ERROR: %s depends on libraries not supplied by macOS:\n%s\n' "$binary" "$unexpected" >&2
    printf 'Rebuild using the official Microsoft .NET SDK/runtime packs, then retry.\n' >&2
    exit 1
fi

printf 'Native dependency check passed: %s\n' "$binary"
