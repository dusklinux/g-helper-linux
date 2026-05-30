#!/usr/bin/env bash
# Phase 01: helper binary exists, is an ELF, and exits cleanly when stdin closes.
set -uo pipefail
. "$(dirname "$0")/lib.sh"

[[ -x "$GHELPER_AUDIO_BIN" ]] || { echo "binary missing"; exit 1; }
file "$GHELPER_AUDIO_BIN" | grep -q "ELF 64-bit" || { echo "not ELF"; exit 1; }

# Run with /dev/null on stdin - helper should self-terminate on EOF (HUP).
timeout 3 "$GHELPER_AUDIO_BIN" </dev/null >/dev/null 2>"$TMP/01.err"
rc=$?
# 0 on graceful exit, 143 if signal-killed by SIGTERM from internal kill().
if [[ $rc -ne 0 && $rc -ne 143 ]]; then
    echo "exit code: $rc"
    cat "$TMP/01.err"
    exit 1
fi
grep -q "ready" "$TMP/01.err" || { echo "no ready line"; cat "$TMP/01.err"; exit 1; }
exit 0
