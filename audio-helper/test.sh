#!/usr/bin/env bash
#
# G-Helper audio helper test harness. Runs a sequence of integration tests
# against ghelper-audio on the local PipeWire session. Each phase prints
# PASS/FAIL with diagnostic details on failure.
#
# Usage:
#   ./test.sh                  # run all phases
#   ./test.sh 03 05            # run only phases 03 and 05
#   ./test.sh -v               # verbose mode (show test stderr too)
#
# Layout:
#   test.sh           # this file (orchestrator)
#   tests/NN_*.sh     # individual test phases
#   test_frames.py    # binary frame parser
#
# Each test phase must:
#   - exit 0 on PASS, non-zero on FAIL
#   - print human-readable diagnostics to stderr
#   - clean up its own child processes (sourced lib.sh helps)

set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TESTS_DIR="$SCRIPT_DIR/tests"

VERBOSE=0
SELECTED=()
for arg in "$@"; do
    case "$arg" in
        -v|--verbose) VERBOSE=1 ;;
        [0-9]*)       SELECTED+=("$arg") ;;
        *)            echo "unknown arg: $arg" >&2; exit 2 ;;
    esac
done

# Ensure helper is built and reasonably fresh.
if [[ -f "$SCRIPT_DIR/main.c" ]]; then
    (cd "$SCRIPT_DIR" && make -s) || {
        echo "FAIL build: see make output above" >&2
        exit 1
    }
fi

if [[ ! -x "$SCRIPT_DIR/ghelper-audio" ]]; then
    echo "FAIL: $SCRIPT_DIR/ghelper-audio missing or not executable" >&2
    exit 1
fi

# Discover phases.
mapfile -t ALL_PHASES < <(ls "$TESTS_DIR"/[0-9][0-9]_*.sh 2>/dev/null | sort)
if [[ ${#ALL_PHASES[@]} -eq 0 ]]; then
    echo "no test phases found in $TESTS_DIR" >&2
    exit 2
fi

PASS=0
FAIL=0
FAILED_NAMES=()

export GHELPER_AUDIO_BIN="$SCRIPT_DIR/ghelper-audio"
export FRAMES_PY="$SCRIPT_DIR/test_frames.py"
export TMP="$(mktemp -d -t ghelper-audio-test.XXXXXX)"
trap 'rm -rf "$TMP"; pkill -P $$ 2>/dev/null || true' EXIT

# Make sure no zombie helper from a prior aborted run.
pkill -9 -x ghelper-audio 2>/dev/null
sleep 0.3

for phase_path in "${ALL_PHASES[@]}"; do
    phase_file=$(basename "$phase_path")
    phase_num="${phase_file%%_*}"

    if [[ ${#SELECTED[@]} -gt 0 ]]; then
        skip=1
        for s in "${SELECTED[@]}"; do
            [[ "$phase_num" == "$s" ]] && { skip=0; break; }
        done
        [[ $skip -eq 1 ]] && continue
    fi

    printf "%-50s" "$phase_file"
    if [[ $VERBOSE -eq 1 ]]; then
        echo
        "$phase_path"
        rc=$?
    else
        out=$("$phase_path" 2>&1)
        rc=$?
    fi
    if [[ $rc -eq 0 ]]; then
        echo "PASS"
        PASS=$((PASS + 1))
    else
        echo "FAIL"
        FAIL=$((FAIL + 1))
        FAILED_NAMES+=("$phase_file")
        if [[ $VERBOSE -eq 0 ]]; then
            echo "----- $phase_file output -----"
            echo "$out"
            echo "----- end -----"
        fi
    fi

    # Ensure no zombies between phases.
    pkill -9 -x ghelper-audio 2>/dev/null
    pkill -9 -f "tail -f /dev/null  *# *ghelper-audio-stdin" 2>/dev/null
    sleep 0.2
done

echo
echo "===== $PASS pass, $FAIL fail ====="
if [[ $FAIL -gt 0 ]]; then
    echo "Failed phases: ${FAILED_NAMES[*]}"
    exit 1
fi
