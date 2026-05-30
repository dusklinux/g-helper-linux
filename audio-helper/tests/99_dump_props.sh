#!/usr/bin/env bash
# Phase 99 (diagnostic, not a real test): dump every property our node has
# alongside the first three other Audio/Source nodes for comparison. Useful
# when phase 03 (pulse visibility) is failing.
set -uo pipefail
. "$(dirname "$0")/lib.sh"

require pw-cli || exit 1

spawn_helper || exit 1
wait_for_node ghelper-audio 3 || { stop_helper; exit 1; }
sleep 0.5

id=$(get_node_id ghelper-audio)
echo "=== ghelper-audio (id=$id) ==="
pw-cli i "$id" 2>&1 | sed -n '/properties:/,/state:/p'

echo
echo "=== Comparison: other Audio/Source nodes (first 3) ==="
pw-cli ls Node 2>&1 | awk '
    /^\tid [0-9]+,/ { id=$2; sub(/,$/, "", id); next }
    /media\.class = "Audio\/Source"/ && !/ghelper-audio/ { print id; }
' | head -3 | while read other; do
    name=$(pw-cli i "$other" 2>&1 | grep node.name | head -1 | sed 's/.*"\([^"]*\)".*/\1/')
    echo "--- $name (id=$other) ---"
    pw-cli i "$other" 2>&1 | sed -n '/properties:/,/state:/p' | head -40
done

stop_helper
exit 0
