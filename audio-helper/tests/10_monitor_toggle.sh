#!/usr/bin/env bash
# Phase 10: MON 1 attaches a playback stream, MON 0 detaches it.
# We don't assert anything about actual audible output - just that the
# helper node graph changes shape correctly.
set -uo pipefail
. "$(dirname "$0")/lib.sh"

require pw-cli || exit 1

spawn_helper || exit 1
wait_for_node ghelper-audio 3 || { stop_helper; exit 1; }
wait_for_capture_link 3 || true

# Initially: monitor node may exist but should not be linked anywhere.
sleep 0.3
links_before=$(pw-link -l 2>/dev/null | grep -c "ghelper-audio-monitor" || true)

send_cmd "MON 1"
# Wait up to 2s for the monitor connection to come up.
ok=0
for _ in 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20; do
    sleep 0.1
    if pw-link -l 2>/dev/null | grep -q "ghelper-audio-monitor"; then
        ok=1
        break
    fi
done
if [[ $ok -eq 0 ]]; then
    echo "monitor never linked after MON 1"
    echo "---all links---"
    pw-link -l 2>&1 | head -30
    stop_helper
    exit 1
fi

send_cmd "MON 0"
# Wait for it to drop.
for _ in 1 2 3 4 5 6 7 8 9 10; do
    sleep 0.1
    if ! pw-link -l 2>/dev/null | grep -q "ghelper-audio-monitor"; then
        ok=2
        break
    fi
done
if [[ $ok -ne 2 ]]; then
    echo "monitor still linked after MON 0"
    pw-link -l 2>&1 | grep -i monitor
    stop_helper
    exit 1
fi

stop_helper
exit 0
