#!/usr/bin/env bash
# Phase 11: SRC <name> retargets the capture stream to a specific source.
# Asserts: after SRC, the capture stream has a link from the requested
# source, and helper continues emitting audio frames.
set -uo pipefail
. "$(dirname "$0")/lib.sh"

require pw-cli  || exit 1
require pw-link || exit 1
require python3 || exit 1

# Enumerate available Audio/Source nodes (excluding ours).
mapfile -t SOURCES < <(pw-cli ls Node 2>/dev/null | python3 -c "
import sys, re
text = sys.stdin.read()
# Walk node blocks
blocks = re.split(r'^\tid \d+, type PipeWire:Interface:Node/\d+', text, flags=re.M)
for b in blocks:
    if 'media.class = \"Audio/Source\"' in b and 'ghelper-audio' not in b:
        m = re.search(r'node\.name = \"([^\"]+)\"', b)
        if m: print(m.group(1))
")

if [[ ${#SOURCES[@]} -lt 1 ]]; then
    echo "no Audio/Source nodes available for retarget test (skipping)"
    exit 0
fi

spawn_helper || exit 1
wait_for_node ghelper-audio 3 || { stop_helper; exit 1; }
wait_for_capture_link 3 || true

TARGET="${SOURCES[0]}"
send_cmd "SRC $TARGET"

# Wait for the new link to appear.
ok=0
for _ in 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 21 22 23 24 25; do
    sleep 0.1
    if pw-link -l 2>/dev/null | awk -v t="$TARGET" '
        /^[^ \t]/ { src=$0 }
        /^[ \t]*\|->.*ghelper-audio-capture/ { if (src ~ t) { found=1 } }
        END { exit !found }
    '; then
        ok=1
        break
    fi
done
if [[ $ok -eq 0 ]]; then
    echo "after SRC $TARGET, no link from $TARGET to ghelper-audio-capture"
    pw-link -l 2>&1 | head -40
    stop_helper
    exit 1
fi

# Verify the helper is still emitting frames after the retarget.
sleep 1
n=$(python3 "$FRAMES_PY" --skip-waveform --skip-spectrum <"$HELPER_OUT" 2>/dev/null | wc -l)
if (( n < 10 )); then
    echo "only $n frames emitted after retarget"
    stop_helper
    exit 1
fi

stop_helper
exit 0
