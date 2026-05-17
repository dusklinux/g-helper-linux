#!/usr/bin/env bash
# ╔══════════════════════════════════════════════════════════════════════╗
# ║  G-HELPER GPU MODE BOOT APPLICATION                                  ║
# ║  Applied BEFORE display-manager.service by ghelper-gpu-boot.service. ║
# ║  Reads pending GPU mode from trigger file, applies sysfs writes,     ║
# ║  cleans up block artifacts.                                          ║
# ║                                                                      ║
# ║  Runs as root via systemd - no sudo/pkexec needed.                   ║
# ║  CRITICAL: This script MUST NOT block boot. Always exit 0.           ║
# ║                                                                      ║
# ║  Testability:                                                        ║
# ║    Set GHELPER_TEST_ROOT=/tmp/scenario-N to redirect all /sys, /etc  ║
# ║    and side-effecting commands (rmmod, udevadm) into a sandbox.      ║
# ║    See install/tests/test-ghelper-gpu-boot.sh for the harness.       ║
# ╚══════════════════════════════════════════════════════════════════════╝

# NO set -e - we must never abort on error. Individual errors are handled.
set -uo pipefail

LOG_TAG="ghelper-gpu-boot"

# ── Sandbox root for testing ───────────────────────────────────────────────────
# Empty in production (paths resolve under real /sys, /etc). In tests this is
# set to a temporary directory containing fake sysfs and etc trees.
ROOT="${GHELPER_TEST_ROOT:-}"

# ── Paths (all prefixed with $ROOT for testability) ────────────────────────────
# Legacy asus-nb-wmi sysfs bases (tried in order, matching SysfsHelper.ResolveAttrPath)
LEGACY_BASES=(
    "${ROOT}/sys/bus/platform/devices/asus-nb-wmi"
    "${ROOT}/sys/devices/platform/asus-nb-wmi"
)
# Firmware-attributes (asus-armoury, kernel 6.8+)
FW_ATTR_BASE="${ROOT}/sys/class/firmware-attributes/asus-armoury/attributes"

TRIGGER="${ROOT}/etc/ghelper/pending-gpu-mode"
RETRY_COUNTER="${ROOT}/etc/ghelper/eco-retry-count"
FAILURE_MARKER="${ROOT}/etc/ghelper/last-eco-failed"
# Backend selector. "asus-wmi" (default) writes dgpu_disable=1 via firmware.
# "pci" relies purely on the modprobe block + udev hot-remove rule; the boot
# script never touches dgpu_disable, the block artifacts themselves ARE the
# persistent Eco state.
BACKEND_FILE="${ROOT}/etc/ghelper/backend"
# Vendor-aware block file (blocks both nvidia + amdgpu)
MODPROBE_BLOCK="${ROOT}/etc/modprobe.d/ghelper-gpu-block.conf"
UDEV_BLOCK="${ROOT}/etc/udev/rules.d/50-ghelper-remove-dgpu.rules"

PCI_DEVICES="${ROOT}/sys/bus/pci/devices"
PCI_RESCAN="${ROOT}/sys/bus/pci/rescan"
DEBUGFS_BASE="${ROOT}/sys/kernel/debug/asus-nb-wmi"

# Cap on consecutive failed Eco apply attempts before giving up (Bug 4 fix)
MAX_ECO_RETRIES=3

# In test mode we still emit logs via logger (no-op without journal) AND stdout
# so the harness can capture them.
log() { logger -t "$LOG_TAG" "$*" 2>/dev/null || true; echo "$LOG_TAG: $*"; }

# ── Side-effect wrappers (overridable in test mode) ───────────────────────────
# In production these call the real tools. In test mode they manipulate the
# sandbox fake-sysfs and emit log lines the harness can assert on.
rmmod_modules() {
    if [[ -n "$ROOT" ]]; then
        # Test sandbox: read instructed behavior from a flag file the harness
        # set up beforehand. Default: succeed by deleting the fake module dirs.
        local flag="${ROOT}/test-rmmod-fail"
        if [[ -f "$flag" ]]; then
            local n
            n=$(cat "$flag" 2>/dev/null || echo "1")
            log "test: simulated rmmod failure ($n more times remaining)"
            n=$((n - 1))
            if (( n <= 0 )); then
                rm -f "$flag"
            else
                echo "$n" > "$flag"
            fi
            return 1
        fi
        for mod in "$@"; do
            rm -rf "${ROOT}/sys/module/$mod" 2>/dev/null
        done
        return 0
    fi
    rmmod "$@" 2>/dev/null
}

udevadm_settle() {
    if [[ -n "$ROOT" ]]; then
        log "test: would udevadm settle"
        return 0
    fi
    if command -v udevadm &>/dev/null; then
        udevadm settle --timeout=10 2>/dev/null || true
    fi
}

udevadm_reload() {
    if [[ -n "$ROOT" ]]; then
        log "test: would udevadm control --reload-rules"
        return 0
    fi
    if command -v udevadm &>/dev/null; then
        udevadm control --reload-rules 2>/dev/null || true
    fi
}

pci_rescan() {
    if [[ ! -f "$PCI_RESCAN" ]]; then
        return 0
    fi
    echo 1 > "$PCI_RESCAN" 2>/dev/null || true
}

# Read dgpu_disable. In production this is just `cat`. In test mode, a counter
# file lets the harness simulate "write succeeded but readback shows wrong
# value" firmware behavior so the Bug 2 double-write+rescan retry can be
# exercised. The counter decrements on each call; while > 0 we lie and return
# "0" regardless of the actual file content.
read_dgpu_disable() {
    local path="$1"
    if [[ -n "$ROOT" ]]; then
        local fail_file="${ROOT}/test-dgpu-readback-fail-count"
        if [[ -f "$fail_file" ]]; then
            local n
            n=$(cat "$fail_file" 2>/dev/null || echo "0")
            if (( n > 0 )); then
                echo "$((n - 1))" > "$fail_file"
                echo "0"
                return 0
            fi
        fi
    fi
    cat "$path" 2>/dev/null || echo "0"
}

# ── Resolve sysfs path (mirrors SysfsHelper.ResolveAttrPath) ─────────────────
# Usage: resolve_sysfs_path <attr_name>
# Tries legacy asus-nb-wmi paths first, then firmware-attributes (asus-armoury).
# Prints the first path that exists, or empty string if not found.
resolve_sysfs_path() {
    local attr="$1"
    for base in "${LEGACY_BASES[@]}"; do
        local path="$base/$attr"
        if [[ -f "$path" ]]; then
            echo "$path"
            return
        fi
    done
    local fw_path="$FW_ATTR_BASE/$attr/current_value"
    if [[ -f "$fw_path" ]]; then
        echo "$fw_path"
        return
    fi
    echo ""
}

# ── amdgpu binding probe (Bug 1 fix) ──────────────────────────────────────────
# Returns 0 (success) if amdgpu is bound to ANY dGPU (vendor 0x1002, boot_vga!=1).
# Returns 1 if amdgpu is bound only to the iGPU (boot_vga=1) or not bound at all.
#
# This differentiates AMD-iGPU + NVIDIA-dGPU hybrids (where amdgpu drives only the
# iGPU and is safe to leave loaded during Eco apply) from pure-AMD-dGPU systems
# (where amdgpu would need to be unloaded but can't be because the iGPU also uses
# it). Critical for FA608*, GA402*, GA403*, G14, G16 etc.
amdgpu_drives_dgpu() {
    [[ -d "$PCI_DEVICES" ]] || return 1
    local dev vendor boot_vga drv_link drv_name
    for dev in "$PCI_DEVICES"/*; do
        [[ -e "$dev" ]] || continue
        drv_link="$dev/driver"
        [[ -L "$drv_link" ]] || continue
        drv_name=$(basename "$(readlink -f "$drv_link")")
        [[ "$drv_name" == "amdgpu" ]] || continue
        vendor=$(cat "$dev/vendor" 2>/dev/null || echo "")
        [[ "$vendor" == "0x1002" ]] || continue
        boot_vga=$(cat "$dev/boot_vga" 2>/dev/null || echo "0")
        if [[ "$boot_vga" != "1" ]]; then
            return 0
        fi
    done
    return 1
}

# ── Foreign-driver probe (#29 - vfio-pci passthrough et al.) ──────────────────
# Returns 0 and echoes the driver name if a dGPU (vendor 0x10de or 0x1002 with
# boot_vga != 1; PCI class 0x0300xx VGA or 0x0302xx 3D-controller) is bound to
# any driver OTHER than the ones the earlier branches already handled
# (nvidia / nouveau via rmmod, amdgpu via amdgpu_drives_dgpu).
#
# Typical culprit: vfio-pci for GPU passthrough into a VM. Disabling the dGPU
# while passthrough is active would yank the device from under the guest. Defer.
dgpu_foreign_driver() {
    [[ -d "$PCI_DEVICES" ]] || return 1
    local dev vendor boot_vga class drv_link drv_name
    for dev in "$PCI_DEVICES"/*; do
        [[ -e "$dev" ]] || continue
        vendor=$(cat "$dev/vendor" 2>/dev/null || echo "")
        case "$vendor" in
            0x10de) ;;
            0x1002)
                boot_vga=$(cat "$dev/boot_vga" 2>/dev/null || echo "0")
                [[ "$boot_vga" != "1" ]] || continue
                ;;
            *) continue ;;
        esac
        class=$(cat "$dev/class" 2>/dev/null || echo "")
        case "$class" in
            0x0300*|0x0302*) ;;
            *) continue ;;
        esac
        drv_link="$dev/driver"
        [[ -L "$drv_link" ]] || continue
        drv_name=$(basename "$(readlink -f "$drv_link")")
        case "$drv_name" in
            nvidia|nouveau|amdgpu) continue ;;
        esac
        echo "$drv_name"
        return 0
    done
    return 1
}

# ── Resolve hardware paths ────────────────────────────────────────────────────
dgpu_path=$(resolve_sysfs_path "dgpu_disable")
mux_path=$(resolve_sysfs_path "gpu_mux_mode")

# ── Resolve backend selector ──────────────────────────────────────────────────
# Read /etc/ghelper/backend and normalize. Unknown/garbage/missing falls back to
# "asus-wmi" so existing ASUS users keep the WMI flow without writing any file.
BACKEND="asus-wmi"
if [[ -f "$BACKEND_FILE" ]]; then
    raw_backend=$(cat "$BACKEND_FILE" 2>/dev/null | tr -d '[:space:]')
    case "$raw_backend" in
        pci)       BACKEND="pci" ;;
        asus-wmi)  BACKEND="asus-wmi" ;;
        *)         log "backend: unknown value '$raw_backend' - falling back to asus-wmi" ;;
    esac
fi
log "backend: $BACKEND"

if [[ -n "$dgpu_path" ]]; then
    log "dgpu_disable resolved: $dgpu_path"
else
    log "dgpu_disable: not found (no asus-nb-wmi or asus-armoury)"
fi
if [[ -n "$mux_path" ]]; then
    log "gpu_mux_mode resolved: $mux_path"
else
    log "gpu_mux_mode: not found (no asus-nb-wmi or asus-armoury)"
fi

# ══════════════════════════════════════════════════════════════════════════════
#  STEP 1: Boot safety check (ALWAYS runs, even without trigger)
#
#  TWO impossible states when MUX=0 (dGPU is sole display):
#
#  1) MUX=0 + dgpu_disable=1 → dGPU powered off but is sole display → black screen
#     Fix: force dgpu_disable=0
#
#  2) MUX=0 + modprobe GPU block present → dGPU driver can't load, dGPU has no driver
#     → black screen even though dGPU is powered on
#     Fix: remove block artifacts so dGPU driver loads normally
#
#  Runs in BOTH backends. The MUX=0 + driver-unavailable pair is a black
#  screen regardless of how the dGPU got blocked (firmware dgpu_disable or
#  modprobe blacklist + udev hot-remove). Non-ASUS systems naturally skip
#  because the mux_path lookup is empty there. The dgpu_disable=0 write is
#  conditional on $dgpu_path existing so it is a no-op on pure non-ASUS
#  PCI configurations.
# ══════════════════════════════════════════════════════════════════════════════
if [[ -n "$mux_path" ]]; then
    mux_val=$(cat "$mux_path" 2>/dev/null || echo "-1")
    if [[ "$mux_val" == "0" ]]; then
        recovery_needed=0

        # Fix impossible state 1: MUX=0 + dgpu_disable=1
        if [[ -n "$dgpu_path" ]]; then
            dgpu_val=$(cat "$dgpu_path" 2>/dev/null || echo "-1")
            if [[ "$dgpu_val" == "1" ]]; then
                log "SAFETY: MUX=0 + dgpu_disable=1 - IMPOSSIBLE STATE, forcing dgpu_disable=0"
                echo 0 > "$dgpu_path" 2>/dev/null || log "SAFETY: failed to write dgpu_disable=0"
                recovery_needed=1
            fi
        fi

        # Fix impossible state 2: MUX=0 + GPU block artifacts present
        if [[ -f "$MODPROBE_BLOCK" || -f "$UDEV_BLOCK" ]]; then
            log "SAFETY: MUX=0 + GPU block artifacts present - removing (dGPU driver must load for display)"
            rm -f "$MODPROBE_BLOCK" "$UDEV_BLOCK" 2>/dev/null
            udevadm_reload
            recovery_needed=1
        fi

        # If trigger says "eco", that's impossible with MUX=0 - discard it
        if [[ -f "$TRIGGER" ]]; then
            trig_mode=$(cat "$TRIGGER" 2>/dev/null | tr -d '[:space:]')
            if [[ "$trig_mode" == "eco" || "$trig_mode" == "1" ]]; then
                log "SAFETY: MUX=0 + trigger='$trig_mode' - discarding impossible Eco trigger"
                rm -f "$TRIGGER" 2>/dev/null
                recovery_needed=1
            fi
        fi

        if [[ "$recovery_needed" == "1" ]]; then
            log "SAFETY: backend=$BACKEND recovery - forcing MUX=1 (Standard), preventing black screen"
            echo 1 > "$mux_path" 2>/dev/null || log "SAFETY: failed to write MUX=1"

            mkdir -p "${ROOT}/etc/ghelper"
            echo "$(date -Iseconds) backend=$BACKEND MUX=0 + eco artifacts detected, recovered to Standard" > "${ROOT}/etc/ghelper/last-recovery"
            chmod 666 "${ROOT}/etc/ghelper/last-recovery" 2>/dev/null || true
            log "SAFETY: recovery marker written to ${ROOT}/etc/ghelper/last-recovery"
        fi

        log "SAFETY: MUX=0 safety check complete"
        if [[ ! -f "$TRIGGER" ]]; then
            exit 0
        fi
    fi
fi

# ══════════════════════════════════════════════════════════════════════════════
#  STEP 2: Check for pending mode
# ══════════════════════════════════════════════════════════════════════════════
if [[ ! -f "$TRIGGER" ]]; then
    log "No pending mode - nothing to do"
    exit 0
fi

MODE=$(cat "$TRIGGER" 2>/dev/null || echo "")
MODE=$(echo "$MODE" | tr -d '[:space:]')
log "Pending GPU mode: '$MODE'"

if [[ "$MODE" == "1" ]]; then
    MODE="eco"
fi

# ══════════════════════════════════════════════════════════════════════════════
#  STEP 3: Validate mode makes sense
# ══════════════════════════════════════════════════════════════════════════════
if [[ -z "$MODE" ]]; then
    log "Empty trigger file - cleaning up"
    rm -f "$TRIGGER" 2>/dev/null
    exit 0
fi

# ── Bug 4 fix: bounded retry on rmmod failure ─────────────────────────────────
# Track consecutive failed Eco apply attempts so we don't loop forever on
# kernels that ship nvidia in the initramfs and refuse rmmod every boot.
record_eco_failure() {
    local reason="$1"
    mkdir -p "${ROOT}/etc/ghelper"
    local count=0
    if [[ -f "$RETRY_COUNTER" ]]; then
        count=$(cat "$RETRY_COUNTER" 2>/dev/null || echo "0")
    fi
    # #38 - sanitize non-numeric / negative content (corrupted counter file).
    # bash arithmetic on a non-integer aborts the script under `set -u`/`pipefail`.
    if [[ ! "$count" =~ ^[0-9]+$ ]]; then
        count=0
    fi
    count=$((count + 1))
    echo "$count" > "$RETRY_COUNTER"
    log "eco: failure #$count: $reason"

    if (( count >= MAX_ECO_RETRIES )); then
        log "eco: reached $MAX_ECO_RETRIES failed attempts - giving up, removing trigger"
        echo "$(date -Iseconds) Eco apply failed $count times: $reason" > "$FAILURE_MARKER"
        chmod 666 "$FAILURE_MARKER" 2>/dev/null || true
        rm -f "$TRIGGER" "$RETRY_COUNTER" 2>/dev/null
    fi
}

reset_eco_retry_counter() {
    rm -f "$RETRY_COUNTER" "$FAILURE_MARKER" 2>/dev/null
}

# ── Bug 3 fix: drain pending udev events before touching hardware ─────────────
# The udev rule (50-ghelper-remove-dgpu.rules) hot-removes dGPU PCI devices
# asynchronously on ACTION=="add". Without a settle here, the dgpu_disable
# write can race with the PCI removal and the kernel ACPI _PS3 method may
# block waiting for an in-flight PCI event.
udevadm_settle

# ══════════════════════════════════════════════════════════════════════════════
#  STEP 4: Apply mode
# ══════════════════════════════════════════════════════════════════════════════
case "$MODE" in
    eco)
        # ── Bug 1 fix: vendor-aware driver gating ──────────────────────────────
        # Three possible driver states at this point:
        #   a) nvidia_drm / nouveau loaded → must rmmod (display-manager not up yet)
        #   b) amdgpu driving the dGPU (vendor=0x1002 boot_vga!=1) → can't proceed
        #      safely (would need to unload amdgpu but iGPU may share it on pure-AMD
        #      hybrids)
        #   c) amdgpu only on iGPU OR nothing → safe to proceed
        if [[ -d "${ROOT}/sys/module/nvidia_drm" ]] || [[ -d "${ROOT}/sys/module/nouveau" ]]; then
            log "eco: nvidia/nouveau loaded, attempting rmmod"
            rmmod_modules nvidia_drm nvidia_modeset nvidia_uvm nvidia
            rmmod_modules nouveau
            sleep 0.2
            if [[ -d "${ROOT}/sys/module/nvidia_drm" ]] || [[ -d "${ROOT}/sys/module/nouveau" ]]; then
                record_eco_failure "rmmod nvidia/nouveau failed"
                exit 0
            fi
            log "eco: rmmod succeeded"
        elif [[ -d "${ROOT}/sys/module/amdgpu" ]] && amdgpu_drives_dgpu; then
            # Pure-AMD hybrid where amdgpu drives the dGPU. Unloading would
            # likely take down the iGPU display too. Defer.
            record_eco_failure "amdgpu drives dGPU (pure-AMD hybrid), cannot unload safely"
            exit 0
        else
            # Either no dGPU driver bound, or amdgpu drives only the iGPU
            # (boot_vga=1, common on AMD-iGPU + NVIDIA-dGPU laptops). Safe.
            log "eco: no blocking dGPU driver bound, proceeding"
        fi

        # #29 - foreign driver (vfio-pci, etc.) bound to dGPU. Common in GPU
        # passthrough setups. Disabling the dGPU now would yank the device from
        # under a running VM. Defer and let the retry counter eventually
        # surrender so we don't loop forever.
        if foreign_drv=$(dgpu_foreign_driver); then
            record_eco_failure "dGPU bound to foreign driver '$foreign_drv' (passthrough?), refusing to disable"
            exit 0
        fi

        # ── PCI backend ────────────────────────────────────────────────────────
        # Driver is already unloaded by the rmmod step above. The modprobe
        # block prevents re-load and the udev rule hot-removes the dGPU PCI
        # device on every boot. Those two files ARE the persistent Eco state
        # in PCI mode - we must NOT delete them in STEP 5. Clear the trigger
        # and the retry counter, then exit early so the WMI sysfs/debugfs
        # paths below and the STEP 5 cleanup do not run.
        if [[ "$BACKEND" == "pci" ]]; then
            log "eco: PCI backend - blocks remain as persistent state, skipping dgpu_disable write"
            rm -f "$TRIGGER" 2>/dev/null
            reset_eco_retry_counter
            log "eco: PCI backend apply complete"
            exit 0
        fi

        if [[ -z "$dgpu_path" ]]; then
            # No sysfs - try debugfs raw WMI if available
            if [[ -d "$DEBUGFS_BASE" ]]; then
                DEVID="0x00090020"
                echo "$DEVID" > "$DEBUGFS_BASE/dev_id" 2>/dev/null
                probe=$(cat "$DEBUGFS_BASE/dsts" 2>&1)
                if echo "$probe" | grep -q "No such device"; then
                    DEVID="0x00090120"
                    log "eco: ROG endpoint not supported, trying Vivobook ($DEVID)"
                fi
                log "eco: no sysfs, using debugfs raw WMI (DEVS $DEVID, 1)"
                # Double-write pattern (kernel comment in dgpu_disable_store):
                # "store the value twice, typical store first, then rescan PCI
                #  bus to activate power, then store a second time to save
                #  correctly."
                echo "$DEVID" > "$DEBUGFS_BASE/dev_id" 2>/dev/null
                echo 1 > "$DEBUGFS_BASE/ctrl_param" 2>/dev/null
                result=$(cat "$DEBUGFS_BASE/devs" 2>&1)
                log "eco: raw WMI first write: $result"
                sleep 0.1
                pci_rescan
                sleep 0.1
                echo "$DEVID" > "$DEBUGFS_BASE/dev_id" 2>/dev/null
                echo 1 > "$DEBUGFS_BASE/ctrl_param" 2>/dev/null
                result=$(cat "$DEBUGFS_BASE/devs" 2>&1)
                log "eco: raw WMI second write: $result"
                reset_eco_retry_counter
            else
                record_eco_failure "no sysfs or debugfs node available"
            fi
        else
            # Check MUX - cannot set Eco when MUX=0
            if [[ -n "$mux_path" ]]; then
                mux_val=$(cat "$mux_path" 2>/dev/null || echo "1")
                if [[ "$mux_val" == "0" ]]; then
                    log "eco: MUX=0 (Ultimate) - cannot apply Eco, cleaning up"
                    rm -f "$MODPROBE_BLOCK" "$UDEV_BLOCK" "$TRIGGER" 2>/dev/null
                    reset_eco_retry_counter
                    exit 0
                fi
            fi

            current=$(read_dgpu_disable "$dgpu_path")
            if [[ "$current" == "1" ]]; then
                log "eco: dgpu_disable already 1 - already in Eco"
                reset_eco_retry_counter
            else
                # ── Bug 2 fix: double-write+rescan retry on EIO ────────────────
                # Some firmware (e.g. GA402XV.318) returns -EIO on the first
                # store because the dGPU bus isn't powered. The kernel comment
                # at dgpu_disable_store documents the workaround: rescan PCI,
                # then store again. Mirrors the existing debugfs path above.
                log "eco: writing dgpu_disable=1 (attempt 1)"
                echo 1 > "$dgpu_path" 2>/dev/null
                actual=$(read_dgpu_disable "$dgpu_path")
                if [[ "$actual" != "1" ]]; then
                    log "eco: first write readback=$actual (expected 1), retrying after PCI rescan"
                    pci_rescan
                    sleep 0.1
                    log "eco: writing dgpu_disable=1 (attempt 2)"
                    echo 1 > "$dgpu_path" 2>/dev/null
                    sleep 0.1
                    actual=$(read_dgpu_disable "$dgpu_path")
                fi

                if [[ "$actual" == "1" ]]; then
                    log "eco: dgpu_disable=1 confirmed"
                    reset_eco_retry_counter
                else
                    record_eco_failure "dgpu_disable write readback=$actual after retry"
                    exit 0
                fi
            fi
        fi
        ;;

    standard|optimized)
        # ── PCI backend ────────────────────────────────────────────────────────
        # No firmware write; just remove the block artifacts, reload udev so
        # the hot-remove rule disappears, then rescan PCI to re-enumerate the
        # dGPU. The nvidia driver will bind on its own once udev sees the new
        # PCI ADD without the remove rule active.
        if [[ "$BACKEND" == "pci" ]]; then
            log "$MODE: PCI backend - removing block artifacts, reloading udev, rescanning PCI"
            rm -f "$MODPROBE_BLOCK" "$UDEV_BLOCK" 2>/dev/null
            udevadm_reload
            sleep 0.05
            pci_rescan
            reset_eco_retry_counter
            rm -f "$TRIGGER" 2>/dev/null
            log "$MODE: PCI bus rescan triggered, dGPU should reappear"
            exit 0
        fi

        # Ensure dGPU is enabled
        wrote_enable=0
        if [[ -n "$dgpu_path" ]]; then
            current=$(cat "$dgpu_path" 2>/dev/null || echo "0")
            if [[ "$current" == "1" ]]; then
                log "$MODE: writing dgpu_disable=0 (enabling dGPU)"
                echo 0 > "$dgpu_path" 2>/dev/null
                wrote_enable=1
            else
                log "$MODE: dgpu already enabled (dgpu_disable=0)"
            fi
        elif [[ -d "$DEBUGFS_BASE" ]]; then
            DEVID="0x00090020"
            echo "$DEVID" > "$DEBUGFS_BASE/dev_id" 2>/dev/null
            probe=$(cat "$DEBUGFS_BASE/dsts" 2>&1)
            if echo "$probe" | grep -q "No such device"; then
                DEVID="0x00090120"
                log "$MODE: ROG endpoint not supported, trying Vivobook ($DEVID)"
            fi
            log "$MODE: no sysfs, using debugfs raw WMI (DEVS $DEVID, 0)"
            echo "$DEVID" > "$DEBUGFS_BASE/dev_id" 2>/dev/null
            echo 0 > "$DEBUGFS_BASE/ctrl_param" 2>/dev/null
            result=$(cat "$DEBUGFS_BASE/devs" 2>&1)
            log "$MODE: raw WMI result: $result"
            wrote_enable=1
        fi
        # ── Bug 5 fix: always rescan PCI when transitioning to a non-Eco mode.
        # If a previous Eco attempt removed the dGPU via the udev rule but
        # failed to actually disable it, the dgpu_disable readback can show 0
        # while the PCI device is gone. Rescanning makes the dGPU reappear.
        sleep 0.05
        pci_rescan
        if (( wrote_enable == 1 )); then
            log "$MODE: PCI bus rescan triggered (write completed)"
        else
            log "$MODE: PCI bus rescan triggered (recovery for prior failed Eco)"
        fi
        reset_eco_retry_counter
        ;;

    ultimate)
        # Ultimate (MUX=0) is meaningless on PCI backend - there's no MUX
        # hardware to latch. Treat as a no-op so we don't crash the cleanup
        # and so the user sees a clear log line if they accidentally trigger
        # it (e.g. by manual /etc/ghelper write).
        if [[ "$BACKEND" == "pci" ]]; then
            log "ultimate: not applicable in PCI backend (no MUX hardware), clearing trigger"
            rm -f "$TRIGGER" 2>/dev/null
            reset_eco_retry_counter
            exit 0
        fi

        # dGPU must be enabled in Ultimate (MUX=0)
        if [[ -n "$dgpu_path" ]]; then
            current=$(cat "$dgpu_path" 2>/dev/null || echo "0")
            if [[ "$current" == "1" ]]; then
                log "ultimate: writing dgpu_disable=0 (enabling dGPU)"
                echo 0 > "$dgpu_path" 2>/dev/null
            else
                log "ultimate: dgpu already enabled"
            fi
        fi
        # Bug 5 fix: always rescan (see standard/optimized branch).
        sleep 0.05
        pci_rescan
        log "ultimate: PCI bus rescan triggered"
        if [[ -n "$mux_path" ]]; then
            mux_val=$(cat "$mux_path" 2>/dev/null || echo "-1")
            log "ultimate: MUX=$mux_val (expected 0 if latch took effect)"
        fi
        reset_eco_retry_counter
        ;;

    *)
        log "Unknown mode '$MODE' - cleaning up without hardware action"
        ;;
esac

# ══════════════════════════════════════════════════════════════════════════════
#  STEP 5: Clean up block artifacts
# ══════════════════════════════════════════════════════════════════════════════
rm -f "$MODPROBE_BLOCK" "$UDEV_BLOCK" "$TRIGGER" 2>/dev/null
udevadm_reload

log "Boot GPU mode application complete - block artifacts cleaned"
exit 0
