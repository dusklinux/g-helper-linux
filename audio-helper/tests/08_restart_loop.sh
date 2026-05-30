#!/usr/bin/env bash
# Phase 08: start/stop the helper 5 times and assert exactly one node lives
# at a time, with no leftover after the final stop.
set -uo pipefail
. "$(dirname "$0")/lib.sh"

require pw-cli || exit 1

for cycle in 1 2 3 4 5; do
    spawn_helper || { echo "spawn $cycle failed"; exit 1; }
    wait_for_node ghelper-audio 3 || { echo "no node on cycle $cycle"; stop_helper; exit 1; }
    n=$(pw-cli ls Node 2>/dev/null | grep -c 'node.name = "ghelper-audio"')
    if (( n != 1 )); then
        echo "cycle $cycle: expected 1 node, got $n"
        stop_helper
        exit 1
    fi
    stop_helper
    sleep 0.3
done

# Final state: no node remaining.
sleep 0.3
if pw-cli ls Node 2>/dev/null | grep -q 'node.name = "ghelper-audio"'; then
    echo "node left behind after final cycle"
    exit 1
fi
exit 0
