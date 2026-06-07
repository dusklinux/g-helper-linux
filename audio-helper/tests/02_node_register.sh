#!/usr/bin/env bash
# Phase 02: helper registers as a node in PipeWire.
set -uo pipefail
. "$(dirname "$0")/lib.sh"

require pw-cli || exit 1

spawn_helper || exit 1
if wait_for_node ghelper-audio 3; then
    stop_helper
    exit 0
fi
echo "node never appeared"
pw-cli ls Node 2>&1 | tail -30
stop_helper
exit 1
