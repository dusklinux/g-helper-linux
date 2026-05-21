#!/usr/bin/env bash
# Promote CHANGELOG.md [Unreleased] section to a new version and add a fresh
# empty [Unreleased] block on top.
# Usage: scripts/changelog-bump.sh <version> [date]
#   version: e.g. v1.0.78 (leading 'v' optional, will be added)
#   date:    YYYY-MM-DD, defaults to today
set -euo pipefail

if [[ $# -lt 1 ]]; then
    echo "Usage: $0 <version> [date]" >&2
    echo "  $0 v1.0.78" >&2
    echo "  $0 1.0.78 2026-05-20" >&2
    exit 1
fi

VERSION="$1"
[[ "$VERSION" != v* ]] && VERSION="v${VERSION}"

DATE="${2:-$(date +%Y-%m-%d)}"
if ! [[ "$DATE" =~ ^[0-9]{4}-[0-9]{2}-[0-9]{2}$ ]]; then
    echo "Error: date must be YYYY-MM-DD, got '$DATE'" >&2
    exit 1
fi

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
FILE="$REPO_ROOT/CHANGELOG.md"

if [[ ! -f "$FILE" ]]; then
    echo "Error: $FILE not found" >&2
    exit 1
fi

if grep -q "^## ${VERSION} " "$FILE"; then
    echo "Error: ${VERSION} already exists in CHANGELOG.md" >&2
    exit 1
fi

if ! grep -q "^## \[Unreleased\]" "$FILE"; then
    echo "Error: no '## [Unreleased]' section found in $FILE" >&2
    exit 1
fi

# Skeleton block that becomes the new top section.
NEW_UNRELEASED=$(cat <<'EOF'
## [Unreleased]

### Added

### Fixed

### Changed

EOF
)

# Single pass: replace the existing [Unreleased] heading with the new version
# heading, then inject a fresh [Unreleased] block immediately above it.
TMP=$(mktemp)
awk -v version="$VERSION" -v date="$DATE" -v skeleton="$NEW_UNRELEASED" '
    !done && /^## \[Unreleased\]/ {
        print skeleton
        print ""
        printf "## %s (%s)\n", version, date
        done = 1
        next
    }
    { print }
' "$FILE" > "$TMP"

mv "$TMP" "$FILE"
echo "Promoted [Unreleased] to ${VERSION} (${DATE}) in $FILE"
