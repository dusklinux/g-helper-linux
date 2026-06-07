#!/usr/bin/env bash
# Phase 04: with default mic linked, we receive valid binary frames on stdout
# at approximately the expected rate.
set -uo pipefail
. "$(dirname "$0")/lib.sh"

require pw-link || exit 1
require python3 || exit 1

spawn_helper || exit 1
wait_for_node ghelper-audio 3 || { stop_helper; exit 1; }
link_default_mic || { stop_helper; exit 1; }

sleep 2.5
stop_helper

if [[ ! -s "$HELPER_OUT" ]]; then
    echo "no bytes on stdout"
    cat "$HELPER_ERR"
    exit 1
fi

# Count valid frames via the python parser.
count=$(python3 "$FRAMES_PY" --skip-waveform --skip-spectrum <"$HELPER_OUT" 2>/dev/null | wc -l)
if (( count < 40 )); then
    echo "only $count frames in 2.5s (expected >= 40)"
    head -c 200 "$HELPER_OUT" | xxd | head -5
    exit 1
fi
echo "received $count frames"
exit 0
