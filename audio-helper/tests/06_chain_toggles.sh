#!/usr/bin/env bash
# Phase 06: stdin commands flip flag bits in the next audio frame.
set -uo pipefail
. "$(dirname "$0")/lib.sh"

require pw-link || exit 1
require python3 || exit 1

spawn_helper || exit 1
wait_for_node ghelper-audio 3 || { stop_helper; exit 1; }
link_default_mic || { stop_helper; exit 1; }

# Default state: rnnoise on (bit 0), EQ/delay/reverb off.
# Sequence of (cmd, expected_flags) tuples.
declare -a CMDS=(
    "RNN 1:1"
    "EQ 1:3"
    "DLY 1:7"
    "RVB 1:15"
    "RNN 0:14"
    "EQ 0:12"
    "DLY 0:8"
    "RVB 0:0"
)

# Snapshot output up to here, then issue commands sequentially with small
# gaps so each one ends up reflected in subsequent frames. After all commands
# are sent, stop and verify the trajectory of flags through the file.

for entry in "${CMDS[@]}"; do
    cmd="${entry%:*}"
    send_cmd "$cmd"
    sleep 0.25
done

stop_helper

# Read frames, find the LAST occurrence of each expected flags value in the
# trajectory. If we ever observed the expected pattern after issuing the
# matching command, we pass that step.
mapfile -t SEEN < <(python3 "$FRAMES_PY" --skip-waveform --skip-spectrum <"$HELPER_OUT" 2>/dev/null \
    | python3 -c "
import json, sys
out = []
for line in sys.stdin:
    try:
        out.append(json.loads(line))
    except Exception:
        pass
flags_trail = [d['flags'] for d in out]
print('\n'.join(str(f) for f in flags_trail))
")

if [[ ${#SEEN[@]} -lt 5 ]]; then
    echo "too few frames: ${#SEEN[@]}"
    exit 1
fi

# Each expected pattern must appear *somewhere* in the trajectory.
missing=()
for entry in "${CMDS[@]}"; do
    exp="${entry#*:}"
    found=0
    for f in "${SEEN[@]}"; do
        if [[ "$f" == "$exp" ]]; then found=1; break; fi
    done
    if [[ $found -eq 0 ]]; then missing+=("$entry"); fi
done

if [[ ${#missing[@]} -gt 0 ]]; then
    echo "never observed flags for: ${missing[*]}"
    echo "trajectory: ${SEEN[*]}"
    exit 1
fi
exit 0
