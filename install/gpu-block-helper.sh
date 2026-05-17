#!/bin/bash
# ╔══════════════════════════════════════════════════════════════════════╗
# ║  G-HELPER GPU BLOCK HELPER                                          ║
# ║  Manages GPU block artifacts (vendor-aware: nvidia + amdgpu)         ║
# ║  for Eco mode boot transitions.                                      ║
# ║  Called by ghelper via sudo (NOPASSWD via /etc/sudoers.d/ghelper).   ║
# ║                                                                      ║
# ║  Usage:                                                              ║
# ║    sudo /usr/local/lib/ghelper/gpu-block-helper.sh write <mode> [backend] ║
# ║    sudo /usr/local/lib/ghelper/gpu-block-helper.sh clean             ║
# ║                                                                      ║
# ║  Backend selector:                                                   ║
# ║    asus-wmi  (default)  boot script writes dgpu_disable=1 via        ║
# ║                         firmware. Blocks are temporary.              ║
# ║    pci                  modprobe block + udev hot-remove rule.       ║
# ║                         Blocks are the persistent Eco state; boot    ║
# ║                         script never touches firmware sysfs.         ║
# ╚══════════════════════════════════════════════════════════════════════╝
set -euo pipefail

# Defensive: pin PATH for the script body even though sudo's secure_path
# already gives us a sane one. Useful when the script is run directly for
# testing without sudo.
PATH=/usr/sbin:/usr/bin:/sbin:/bin
export PATH

MODPROBE_DEST="/etc/modprobe.d/ghelper-gpu-block.conf"
UDEV_DEST="/etc/udev/rules.d/50-ghelper-remove-dgpu.rules"
TRIGGER_DIR="/etc/ghelper"
TRIGGER_DEST="$TRIGGER_DIR/pending-gpu-mode"
BACKEND_DEST="$TRIGGER_DIR/backend"

case "${1:-}" in
    write)
        MODE="${2:-eco}"
        BACKEND="${3:-asus-wmi}"
        # Validate mode - only known values accepted
        case "$MODE" in
            eco|standard|optimized|ultimate) ;;
            *)
                echo "Error: invalid mode '$MODE' (expected: eco|standard|optimized|ultimate)" >&2
                exit 1
                ;;
        esac
        case "$BACKEND" in
            asus-wmi|pci) ;;
            *)
                echo "Error: invalid backend '$BACKEND' (expected: asus-wmi|pci)" >&2
                exit 1
                ;;
        esac

        mkdir -p "$TRIGGER_DIR"
        # Backend marker - read by ghelper-gpu-boot.sh on every boot. The
        # marker is a persistent user preference and survives the `clean`
        # subcommand below.
        echo "$BACKEND" > "$BACKEND_DEST"
        chmod 644 "$BACKEND_DEST"

        if [[ "$MODE" == "eco" ]]; then
            # Modprobe block - prevent dGPU driver loading
            cat > "$MODPROBE_DEST" << 'GHELPER_EOF'
# ghelper: block dGPU driver modules so dGPU can be safely disabled on next boot
# Auto-generated - will be removed after Eco mode is applied (asus-wmi backend)
# Auto-generated - kept across reboots as persistent Eco state (pci backend)
# Uses 'install /bin/false' (strongest block - prevents loading by ANY means)
# NVIDIA modules
install nvidia /bin/false
install nvidia_drm /bin/false
install nvidia_modeset /bin/false
install nvidia_uvm /bin/false
install nvidia_wmi_ec_backlight /bin/false
# Open-source NVIDIA driver
install nouveau /bin/false
# AMD dGPU driver
install amdgpu /bin/false
GHELPER_EOF
            chmod 644 "$MODPROBE_DEST"

            # Udev rule - remove dGPU PCI devices from bus on add
            cat > "$UDEV_DEST" << 'GHELPER_EOF'
# ghelper: remove dGPU PCI devices so no driver can bind
# Auto-generated - will be removed after Eco mode is applied (asus-wmi backend)
# Auto-generated - kept across reboots as persistent Eco state (pci backend)
# Remove NVIDIA VGA controller
ACTION=="add", SUBSYSTEM=="pci", ATTR{vendor}=="0x10de", ATTR{class}=="0x030000", ATTR{power/control}="auto", ATTR{remove}="1"
# Remove NVIDIA 3D controller
ACTION=="add", SUBSYSTEM=="pci", ATTR{vendor}=="0x10de", ATTR{class}=="0x030200", ATTR{power/control}="auto", ATTR{remove}="1"
# Remove NVIDIA Audio devices (HDMI audio on dGPU)
ACTION=="add", SUBSYSTEM=="pci", ATTR{vendor}=="0x10de", ATTR{class}=="0x040300", ATTR{power/control}="auto", ATTR{remove}="1"
# Remove NVIDIA USB xHCI Host Controller
ACTION=="add", SUBSYSTEM=="pci", ATTR{vendor}=="0x10de", ATTR{class}=="0x0c0330", ATTR{power/control}="auto", ATTR{remove}="1"
# Remove NVIDIA USB Type-C UCSI devices
ACTION=="add", SUBSYSTEM=="pci", ATTR{vendor}=="0x10de", ATTR{class}=="0x0c8000", ATTR{power/control}="auto", ATTR{remove}="1"
# Remove AMD dGPU VGA controller (boot_vga!=1 protects the iGPU)
ACTION=="add", SUBSYSTEM=="pci", ATTR{vendor}=="0x1002", ATTR{class}=="0x030000", ATTR{boot_vga}!="1", ATTR{power/control}="auto", ATTR{remove}="1"
# Remove AMD dGPU 3D controller
ACTION=="add", SUBSYSTEM=="pci", ATTR{vendor}=="0x1002", ATTR{class}=="0x030200", ATTR{boot_vga}!="1", ATTR{power/control}="auto", ATTR{remove}="1"
# Remove AMD dGPU Audio devices
ACTION=="add", SUBSYSTEM=="pci", ATTR{vendor}=="0x1002", ATTR{class}=="0x040300", ATTR{power/control}="auto", ATTR{remove}="1"
GHELPER_EOF
            chmod 644 "$UDEV_DEST"
        else
            # Non-eco mode change while in pci backend: the user is switching
            # OUT of eco, so the persistent block files must go. The boot
            # script will reload udev + rescan PCI on the next reboot to bring
            # the dGPU back online. In asus-wmi backend this branch is dead
            # code (WriteDriverBlock early-returns for non-eco) but we still
            # remove any leftover files defensively.
            rm -f "$MODPROBE_DEST" "$UDEV_DEST"
        fi

        # Trigger file - tells ghelper on startup which mode to apply
        echo "$MODE" > "$TRIGGER_DEST"
        ;;
    clean)
        # Remove only the ephemeral artifacts. The backend marker is a user
        # preference and persists; it is only rewritten by `write` and only
        # removed during full uninstall (`uninstall` subcommand).
        rm -f "$MODPROBE_DEST" "$UDEV_DEST" "$TRIGGER_DEST"
        ;;
    uninstall)
        # Full reset for package uninstall - removes the backend marker too
        # so a future install starts fresh with auto-detect.
        rm -f "$MODPROBE_DEST" "$UDEV_DEST" "$TRIGGER_DEST" "$BACKEND_DEST"
        ;;
    set-backend)
        # Update only the backend marker. Called from the UI when the user
        # toggles "Use PCI dGPU disable" so the boot service sees the new
        # backend on the very next boot, even before any mode change is
        # scheduled. Idempotent.
        BACKEND="${2:-asus-wmi}"
        case "$BACKEND" in
            asus-wmi|pci) ;;
            *)
                echo "Error: invalid backend '$BACKEND' (expected: asus-wmi|pci)" >&2
                exit 1
                ;;
        esac
        mkdir -p "$TRIGGER_DIR"
        echo "$BACKEND" > "$BACKEND_DEST"
        chmod 644 "$BACKEND_DEST"
        ;;
    live-standard)
        # Atomic live recovery from PCI Eco to Standard. Drops the
        # persistent modprobe block + udev hot-remove rule, reloads udev
        # so the rule is forgotten, rescans the PCI bus to re-enumerate
        # the dGPU, and kicks modprobe explicitly so slow distros do not
        # need a coldplug round-trip to bring the driver back. No reboot
        # required. Mirrors the SetGpuEco(false) live path from the
        # asus-wmi backend so both flows feel the same in the UI.
        #
        # All operations target hardcoded paths / module names. No user
        # input flows in, so there is no injection point even though we
        # run as root via NOPASSWD sudo.
        rm -f "$MODPROBE_DEST" "$UDEV_DEST" "$TRIGGER_DEST"
        udevadm control --reload-rules 2>/dev/null || true
        echo 1 > /sys/bus/pci/rescan 2>/dev/null || true
        # Best-effort kick. udev coldplug normally retries modprobe once
        # the block file disappears, but a direct call avoids races on
        # slow distros. Failure is harmless when the module is missing.
        modprobe nvidia 2>/dev/null || true
        modprobe amdgpu 2>/dev/null || true
        ;;
    *)
        echo "Usage: $0 {write <mode> [backend]|set-backend <backend>|live-standard|clean|uninstall}" >&2
        exit 1
        ;;
esac
