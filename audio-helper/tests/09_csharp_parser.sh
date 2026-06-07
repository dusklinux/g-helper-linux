#!/usr/bin/env bash
# Phase 09: C# AudioFrame parser against live helper.
# Compiles tests/cs/ on first run, runs it; uses GHELPER_AUDIO_BIN env.
set -uo pipefail
. "$(dirname "$0")/lib.sh"

require dotnet || { echo "dotnet SDK not installed - skipping"; exit 0; }

CS_DIR="$(dirname "$0")/cs"
BIN="$CS_DIR/bin/Debug/net10.0/TestAudioPipeline"
if [[ ! -f "$BIN" ]]; then
    (cd "$CS_DIR" && dotnet build -c Debug --nologo -v q) >"$TMP/dotnet.log" 2>&1
    if [[ $? -ne 0 ]]; then
        echo "dotnet build failed:"
        tail -30 "$TMP/dotnet.log"
        exit 1
    fi
fi

# Pre-emptively reap any stragglers; the C# program spawns its own helper.
pkill -9 -x ghelper-audio 2>/dev/null
sleep 0.2

GHELPER_AUDIO_BIN="$GHELPER_AUDIO_BIN" "$BIN"
