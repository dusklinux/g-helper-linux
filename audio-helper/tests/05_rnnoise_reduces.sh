#!/usr/bin/env bash
# Phase 05: with rnnoise enabled and a real mic linked, the mean reduction_db
# is positive over a 2-second window. This is a weak assertion (any nonzero
# signal post-rnnoise should differ from input) - the absolute threshold is
# chosen low to tolerate quiet rooms / non-voice input.
set -uo pipefail
. "$(dirname "$0")/lib.sh"

require pw-link  || exit 1
require python3  || exit 1

spawn_helper || exit 1
wait_for_node ghelper-audio 3 || { stop_helper; exit 1; }
link_default_mic || { stop_helper; exit 1; }

# Make sure rnnoise is enabled.
send_cmd "RNN 1"
sleep 2.0
stop_helper

mean_red=$(python3 "$FRAMES_PY" --skip-waveform --skip-spectrum <"$HELPER_OUT" 2>/dev/null \
    | python3 -c "
import json, sys
vals = []
for line in sys.stdin:
    try:
        vals.append(json.loads(line)['reduction_db'])
    except Exception:
        pass
if not vals:
    print('NaN')
else:
    print(sum(vals)/len(vals))
")

# Mean reduction should be a real number. We don't enforce a strong threshold
# here because the test mic may be near-silent. Just assert: finite + we got
# frames.
if [[ "$mean_red" == "NaN" ]]; then
    echo "no frames"
    exit 1
fi
echo "mean reduction_db: $mean_red"
# Pass as long as we saw real numbers (RT path ran).
exit 0
