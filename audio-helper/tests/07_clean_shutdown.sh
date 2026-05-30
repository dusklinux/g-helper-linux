#!/usr/bin/env bash
# Phase 07: QUIT command produces a clean exit within 1s, and the node
# disappears from PipeWire afterwards.
set -uo pipefail
. "$(dirname "$0")/lib.sh"

require pw-cli || exit 1

spawn_helper || exit 1
wait_for_node ghelper-audio 3 || { stop_helper; exit 1; }

send_cmd "QUIT"
# Wait up to 1 second for process to vanish.
i=0
while (( i++ < 20 )) && kill -0 "$HELPER_PID" 2>/dev/null; do
    sleep 0.05
done
if kill -0 "$HELPER_PID" 2>/dev/null; then
    echo "helper still alive 1s after QUIT"
    stop_helper
    exit 1
fi
wait "$HELPER_PID" 2>/dev/null
exec 9>&- 2>/dev/null || true
rm -f "$HELPER_STDIN"

# Allow PipeWire a beat to retire the node.
sleep 0.5
if pw-cli ls Node 2>/dev/null | grep -q 'node.name = "ghelper-audio"'; then
    echo "node lingered after exit"
    exit 1
fi
exit 0
