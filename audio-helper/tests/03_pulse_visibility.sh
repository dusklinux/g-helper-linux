#!/usr/bin/env bash
# Phase 03: helper appears as a recordable Source in PulseAudio compat layer.
#
# This is what pavucontrol's "Recording" tab and Discord/OBS/Zoom consult.
# Equivalent CLI: pactl list sources short  | grep ghelper
#
# If pw-cli sees the node but pactl does not, the issue is media.category /
# media.class properties not surfacing the node to the Pulse compatibility
# layer. Diagnose with 99_dump_props.sh.
set -uo pipefail
. "$(dirname "$0")/lib.sh"

require pactl || exit 1

spawn_helper || exit 1
wait_for_node ghelper-audio 3 || { echo "node never appeared in pw-cli either"; stop_helper; exit 1; }

# Give wireplumber/pulse compat a moment to propagate.
sleep 0.5

found=$(pactl list sources short 2>/dev/null | grep -i ghelper || true)
if [[ -n "$found" ]]; then
    echo "pactl: $found"
    stop_helper
    exit 0
fi

echo "ghelper-audio NOT visible to pactl"
echo "---all pactl sources---"
pactl list sources short 2>&1
echo "---pw-cli still sees us though---"
pw-cli ls Node 2>&1 | grep -B 1 -A 4 ghelper-audio | head -20
stop_helper
exit 1
