# Changelog

## [Unreleased]

### Added

### Fixed

### Changed

## v1.0.79 (2026-05-30)

### Action required: re-run the install script

All root GPU operations now go through a single helper binary (`/opt/ghelper/gpu-helper`) behind one sudoers entry.
The boot service unit was hardened. Updating the binary alone is not enough -
re-run the install script so the new helper and sudoers file are deployed:

```bash
curl -sL https://raw.githubusercontent.com/utajum/g-helper-linux/master/install/install.sh | sudo bash
```

### Added

- Live Eco / Standard switching without a reboot, with automatic rollback if the
  driver cannot be released
- "Processes using dGPU" window listing every process holding the dGPU
- Service-aware process termination on Eco "Switch Now": holders are stopped
- systemd-service indicator in the dGPU processes window: holders backed by a
  unit show a small badge with the unit name (e.g. `rustdesk.service`)
- GPU tuning: nvidia-smi power limit / clock lock and NVML core+mem clock offsets
- Experimental Intel CPU undervolting, alongside the existing AMD Ryzen Curve Optimizer
- Recovery dialog when the dGPU is enabled in firmware but does not re-appear on
  the PCI bus after repeated rescans (slow ASUS firmware) that advises a reboot
  instead of leaving a silent broken state
- Diagnostics dump now includes the `gpu-helper` journal so a copied report shows
  exactly what ran
- Add keep keyboard backlight always on option to Extra window

### Fixed

- `ghelper` itself holding `/dev/nvidia*` FDs under PRIME render offload.
  `Program.cs:Main` sets `__NV_PRIME_RENDER_OFFLOAD=0`,
  `__GLX_VENDOR_LIBRARY_NAME=mesa`, and `DRI_PRIME=0` when unset so ghelper does
  not appear as a dGPU holder
- Eco to Standard sometimes leaving the dGPU off: the re-enable path now polls
  for the device and re-issues `/sys/bus/pci/rescan` (up to 10s) and only starts
  the NVIDIA daemons once the device is actually present, instead of waiting on
  the kernel module (which can linger from a respawn loop with no GPU)
- Opening the Extra window triggered unwanted side effects
- Keyboard backlight turning off when monitor turns off (option to keep on in Extra window)

### Changed

- All privileged GPU operations were consolidated into one helper
  binary, `gpu-helper` (embedded in the AOT binary, extracted to
  `/opt/ghelper/gpu-helper`, mode 755). It exposes validated subcommands - list
  / kill dGPU holders, NVIDIA daemon stop/start/reset-failed, module rmmod, PCI
  bind/unbind, nvidia-smi power/clock, whitelisted modprobe, and NVML clock
  offsets - each guarded by an internal whitelist.
- Dependency updates: Avalonia to 12.0.4 (bundled SkiaSharp 3.119.3-preview to 3.119.4 stable, Svg.Controls.Skia.Avalonia to 12.0.0.11 (Svg.Skia 4 to 5), and LiveCharts to dev-570

<img width="1916" height="1035" alt="image" src="https://github.com/user-attachments/assets/40f0d340-1394-4927-b811-d227a66fc447" />

## v1.0.78 (2026-05-23)

### Added
- A16 FA608UM - Optimal Display Brightness control (Off / On Always / On Battery only) in Extra Settings

### Changed
- Diagnostics dump expanded with NVIDIA, AMD GPU, Power Source, CPU, App Config, Displays, ghelper systemd units, and Suspend / Resume sections
- Removed unused "Open Log File" button and related i18n keys

### What's Changed
* Add changelog by @utajum in https://github.com/utajum/g-helper-linux/pull/104
* Add OptimalBrightness controls for A16 FA608UM by @utajum in https://github.com/utajum/g-helper-linux/pull/106
* Extend dgpu diagnostics info by @utajum in https://github.com/utajum/g-helper-linux/pull/107
* Remove unused button by @utajum in https://github.com/utajum/g-helper-linux/pull/108

## v1.0.77 (2026-05-19)

### What's Changed
* Fix sudo uses wrong path by @utajum in https://github.com/utajum/g-helper-linux/pull/101
* Fix keyboard backlight on ASUS TUF Gaming A16 FA608WV_FA608WV by @utajum in https://github.com/utajum/g-helper-linux/pull/96

## v1.0.76 (2026-05-18)

Hotfix for release v1.0.75. On non Asus devices and non MUX Asus devices the GPU switch section was missing.

### Commits
- Fix PCI mode not knowing that there is a dgpu by checking the boot service artifact
- Add GPU switching options to issue template [skip-ci]

### What's Changed
* Add GPU switching options to issue template [skip-ci] by @utajum in https://github.com/utajum/g-helper-linux/pull/99
* Fix PCI mode not knowing that there is a dgpu by checking the boot se… by @utajum in https://github.com/utajum/g-helper-linux/pull/100

## v1.0.75 (2026-05-17)

### Action required: re-run the install script

The GPU boot service files (`ghelper-gpu-boot.sh`, `gpu-block-helper.sh`, `ghelper-gpu-boot.service`) have been updated. Simply updating the binary is not enough - you must re-run the install script to get the new boot service:

```bash
curl -sL https://raw.githubusercontent.com/utajum/g-helper-linux/master/install/install.sh | sudo bash
```

### GPU mode switching rework
- Added PCI backend: disables dGPU via modprobe blacklist + udev hot-remove rule, no ASUS firmware required
- PCI backend works on non-ASUS laptops with a discrete NVIDIA GPU
- Switching from Eco to Standard is now live in both backends (no reboot required)
- Switching into Eco mode still requires a reboot (driver in use by display server)
- Boot script MUX=0 safety recovery now runs for both backends
- MUX=0 impossible state (Ultimate pending + Eco requested) is blocked with a notification (it can still happen if using other software like envycontrol)
- Removed "Switch Now" button from GPU driver dialog
- GPU backend selector added to Extra settings (PCI disable / ASUS WMI)
- "Optimized GPU" mode hidden behind a config flag; not shown by default
- Boot service now activates on PCI backend hardware (no ASUS WMI module required)
- Boot script retry/back-off logic for failed Eco transitions (issue #94)

### Experimental XG Mobile dock support
- Detect connected XG Mobile dock via USB HID (VID 0x0B05, known dock PIDs)
- Control dock LED ring: on/off, brightness, Aura mode and colour
- Show XG Mobile fan curve in the Fans window when dock is connected

### Diagnostics
- Boot service state dumped in diagnostics: pending trigger, retry counter, last failure, last recovery marker

### UI
- GPU button row collapses to match the number of visible buttons
- New confirm dialog component
- Optimized button is now hidden behind a config flag
- Ultimate buttons hidden in PCI backend mode

<img width="962" height="862" alt="image" src="https://github.com/user-attachments/assets/96198d16-8884-41cd-bb5b-569a30e900a0" />

## v1.0.74 (2026-05-10)

### Commits
- Update visibility condition for rear lid power row based on Z13 configuration
- update
- use old logic
- revert plus sync
- Add Z13-specific 0x1A wake probe to read led
- HidrawHelper dedup discarded AURA-capable interfaces on Z13, upstream updates

### What's Changed
* Fix ROG Z13 (2023) rear-window-LED not working by @utajum in https://github.com/utajum/g-helper-linux/pull/91

## v1.0.73 (2026-05-04)

### What's Changed
### Added:
- software fn-lock remapper (EVIOCGRAB + /dev/uinput) with per-key and
  per-model F1..F12 mappings #82
- FN-Lock title-row button on MainWindow keyboard panel (gray off,
  blue accent on)
- FN-Lock toggle hotkey (Super+F2 default) detected inside the
  remapper event loop, plus tray menu entry with active-state mark
- FnLockWindow with Behavior, Devices, Per-key map, Advanced sections
- List of default keyboard mappings per device for multimedia keys.
- per-mode platform_profile dropdowns (Silent / Balanced / Turbo) in
  ExtraWindow > Power Management, populated from
  /sys/firmware/acpi/platform_profile_choices
- IPowerManager.GetPlatformProfileChoices()
- Lang updates
- Experimental ROG Ally support

### Fixed:
- duplicate platform-profile synonym table between LinuxPowerManager
  and ExtraWindow collapsed into single source of truth
- three near-identical Raise*FromFnLock wrappers in App.axaml.cs
  collapsed via PostToApp dispatch helper
- GX651 (ROG Zephyrus G16) now correctly classified as IsSlash() and
  IsCPULight() matching upstream model tables
- Upstream AURA fixes

### Changed:
- removed `gpu_eco` action from App
- removed `pcie_aspm` UI dropdown
- removed MigrateOldConfigDir helper from AppConfig
- ModeControl.SetPerformanceMode honors per-mode platform_profile
  override
- Diagnostics report updated to expose Aura hardware-detection fields
  alongside the remaining AppConfig flags
  
  *Note that by default you have to click the FN-Lock button to enable it
  
<img width="954" height="869" alt="image" src="https://github.com/user-attachments/assets/e47fbefa-748b-4e64-be9f-b93017b3cf10" />

## v1.0.72 (2026-04-27)

### What's Changed
### Added
- Custom Aura RGB modes: Heatmap (CPU temp), GPU Mode color, Battery %, and 4-zone Gradient on Strix lightbar + keyboard
- Live CPU and GPU temperature icons in the system tray with hover tooltip
- Per-mode shell command hook in the Fans window (runs after every mode switch including auto AC/DC)
- Power-limit reapply timer to fight BIOS clobber of PPT values on certain models
- Per-AC vs per-battery keyboard backlight levels with automatic apply on AC/DC transition
- Disable notifications toggle in Extra window
- B&W tray icon quick toggle in the tray context menu
- Idempotent guard on the Raw WMI checkbox so spurious events do not restart the app
### Fixed
- Autostart now writes the correct binary path on AOT builds (#80) and detects AppImage via the APPIMAGE env var so the entry survives reboots
- Fan curves and power sliders now refresh in the Fans window when the performance mode is changed (#64)
- Window positioning now opens on the same screen as the main window with a fallback to the primary monitor on multi-monitor setups (#57)
- SVG icons embedded inside buttons no longer swallow click events
- Duplicate USB devices removed from HID enumeration
- Raw WMI mode no longer triggers a pkexec prompt every time the Extra window opens
### Changed
- Fans window grew an Advanced panel for the new mode hook and reapply timer, with the chart area resized so existing cards are not squashed
- Extra window gets the disable-notifications checkbox next to Start minimized to tray
- Language files updated across all 29 locales for the new strings
- Faster opening of secondary windows
- Misc upstream sync from Windows g-helper

<img width="542" height="854" alt="image" src="https://github.com/user-attachments/assets/a2769508-8d9d-4428-850b-450198916565" />

## v1.0.71 (2026-04-25)

### Commits
- Fix battery detection logic in SysfsHelper to prioritize laptop batteries over HID devices #77

### What's Changed
* Add quick uninstall instructions to README, install script and releas… by @utajum in https://github.com/utajum/g-helper-linux/pull/76
* Fix battery detection logic in SysfsHelper to prioritize laptop batte… by @utajum in https://github.com/utajum/g-helper-linux/pull/78

## v1.0.70 (2026-04-23)

Release notes not provided.

## v1.0.69 (2026-04-17)

# You need to copy all .so files manually or run the install script for this release

### Summary

Major update: Wayland and Xorg refresh rate control, full internationalization, framework upgrade, and hardware input fixes.

107 files changed across display backend, localization, UI, platform layer, and build infrastructure.

### Added

- **Wayland refresh rate switching** - New display backend with auto-detection: wlr-randr (Hyprland/Sway/wlroots), kscreen-doctor (KDE Plasma), gdctl (GNOME 48+), xrandr (X11). Vendored wlr-randr v0.5.0 embedded in binary, no external dependency (fixes #30)
- **Auto screen mode** - Max refresh rate + overdrive on AC, 60Hz + overdrive off on battery
- **Backlight controller selector** - Dropdown to choose between available backlight devices (e.g. nvidia_0 vs intel_backlight)
- **Internationalization** - 336 strings localized to 29 languages, system locale auto-detection, persistent language override via dropdown
- **GPU Tuning panel** - Power limit and clock lock sliders in Extra settings (NVIDIA only, auto-hidden), single pkexec prompt
- **Battery Info window** - Health, cycles, capacity, voltage, manufacturer, power draw with 2-second live refresh
- **Battery drain rate fallback** - Computes power from current_now x voltage_now when power_now unavailable (by @GamingLizard9)
- **Noto Color Emoji icons** - All UI emoji replaced with embedded PNG icons for consistent rendering across systems
- **AppImage packaging** - AppStream metadata, fixed desktop categories
- **Easter egg** - Hidden somewhere in the UI

### Fixed

- **TUF hotkey mapping** - Universal ASUS hotkey detection via MSC_SCAN codes (Windows WMI event codes), fixes ROG key and Fn keys on TUF FX517ZM and other models where KEY_* codes differ (fixes #49)
- **Keyboard brightness double-increment** - Detects brightness_hw_changed sysfs to avoid writing when kernel already handled the change (fixes #50)
- **Keyboard brightness sync** - Physical Fn keys now update ExtraWindow slider in real-time (fixes #50)
- **Display brightness polling** - 2-second timer catches external brightness changes from Fn keys or DE controls (fixes #50)
- **Gamma hidden on Wayland** - Gamma controls hidden when backend doesn't support it (fixes #50)
- **Aura hotkey sync** - Cycling aura mode via hotkey updates dropdowns and color buttons immediately
- **Power event debounce** - 3-second cooldown prevents duplicate AC/battery event handling
- **UI refresh** - Fixed 7 cases where backend state changes didn't update the UI

### Changed

- **Framework upgrade** - .NET 8 to 10, Avalonia 11.2.3 to 12.0.1, SkiaSharp 2.88 to 3.119. Binary 46MB to 32MB, AppImage 18MB
- **Footer redesign** - Version label and action buttons reorganized
- **Code cleanup** - Standardized comment styles, removed non-ASCII characters from source
- **CI** - .NET 10 SDK, wlr-randr build step, auto-generated changelog

### New Contributors
* @GamingLizard9 made their first contribution in https://github.com/utajum/g-helper-linux/pull/60

## v1.0.68 (2026-04-09)

### What's Changed
- Keyboard brightness on TUF/VivoZenPro: ApplyBrightness() now falls back to ACPI sysfs write when HID is unavailable (#50)
- PL slider spam on Zephyrus G16: Added 300ms shared debounce timer for PL1/PL2/fPPT sliders (#47)
- Fan count detection: Dynamically detect 2 vs 3 fans instead of hardcoding 3, fixes pwm3_enable ENODEV errors (#47)
- Display backlight on NVIDIA hybrid GPUs: When /sys/class/backlight/ is empty, detect and offer to load nvidia-wmi-ec-backlight via pkexec. Shows kernel parameter hints for Intel/AMD. Minimum brightness clamped to 4% to prevent black screen (#50)
- Security: gpu-block-helper.sh no longer accepts file paths as arguments; modprobe/udev content is hardcoded. Eliminates arbitrary file read/write via NOPASSWD sudoers rule. C# side drops all /tmp temp files, uses inline heredocs for pkexec fallback.
- Aura color picker shows white on normal startup: Race condition between background HID handshake and UI Loaded event caused InitAura() to lock itself out. Now retries until hardware is ready.
- Battery auto-refresh: Battery drain/charge info refreshes every 60s and immediately on AC plug/unplug (#35)
- PL slider coupling: Enforces PL1 <= PL2 <= fPPT, matching Windows G-Helper behavior (#35)
- Start minimized to tray: New checkbox in settings, plus autostart .desktop file management
- Reorder application of power limits, ASPM, and fan curves on mode change

## v1.0.65 (2026-04-05)

### What's Changed
* Latest updates by @utajum in https://github.com/utajum/g-helper-linux/pull/48

## v1.0.64 (2026-04-02)

### What's Changed
* split comment ci [skip ci] by @utajum in https://github.com/utajum/g-helper-linux/pull/44
* Fixing xrandr fallback to work on empty string, reporting refresh rate correctly by @Matthias-VdC in https://github.com/utajum/g-helper-linux/pull/41

### New Contributors
* @Matthias-VdC made their first contribution in https://github.com/utajum/g-helper-linux/pull/41

## v1.0.63 (2026-03-31)

### What's Changed
* add experimental raw WMI mode for GPU Eco on laptops without sysfs by @utajum in https://github.com/utajum/g-helper-linux/pull/33

## v1.0.62 (2026-03-17)

### What's Changed
* add Z13 and Dynamic Lighting RGB support and NVIDIA driver checks #26 by @utajum in https://github.com/utajum/g-helper-linux/pull/28

## v1.0.61 (2026-03-16)

### What's Changed
* fix PPT power limits stuck on dual-backend kernels (asus-nb-wmi + asu… by @utajum in https://github.com/utajum/g-helper-linux/pull/24

## v1.0.60 (2026-03-11)

### What's Changed
* remove diagnostic logging of unmapped ASUS key events by @utajum in https://github.com/utajum/g-helper-linux/pull/22

## v1.0.59 (2026-03-10)

### What's Changed
* fix: unreliable udev permissions on Fedora Atomic + centralize sysfs … by @utajum in https://github.com/utajum/g-helper-linux/pull/19

## v1.0.58 (2026-03-08)

### What's Changed
* Fix fan curve control and add mid fan chart support by @utajum in https://github.com/utajum/g-helper-linux/pull/18

## v1.0.57 (2026-03-08)

### What's Changed
* Add I2C-HID keyboard RGB support and fix power limit controls by @utajum in https://github.com/utajum/g-helper-linux/pull/14

## v1.0.56 (2026-03-01)

### What's Changed
* Add GPU mode switching with safety guards for NVIDIA and AMD dGPUs (must run install script) by @utajum in https://github.com/utajum/g-helper-linux/pull/12

## v1.0.55 (2026-02-24)

### What's Changed
* Add asus-armoury support, GPU mode fixes, add uninstall and appimage options to install scripts, Fix blocking reboot by @utajum in https://github.com/utajum/g-helper-linux/pull/11

## v1.0.54 (2026-02-22)

### What's Changed
* Fix TUF keyboard RGB, battery charge limits, and performance mode persistence by @utajum in https://github.com/utajum/g-helper-linux/pull/9

## v1.0.53 (2026-02-20)

Release notes not provided.

## v1.0.52 (2026-02-20)

### What's Changed
* Fix AppImage self-update 'Text file busy' error, add system diagnostics by @utajum in https://github.com/utajum/g-helper-linux/pull/8

## v1.0.51 (2026-02-20)

Release notes not provided.

## v1.0.50 (2026-02-20)

### What's Changed
* Add AppImage-aware self-update with download progress by @utajum in https://github.com/utajum/g-helper-linux/pull/7

## v1.0.49 (2026-02-20)

### What's Changed
* Add power limit validation and IsResetRequired workaround by @utajum in https://github.com/utajum/g-helper-linux/pull/6

## v1.0.48 (2026-02-20)

### What's Changed
* Sync model lists with upstream, add upstream commit tracker script [skip ci] by @utajum in https://github.com/utajum/g-helper-linux/pull/3
* Add charge limit 6080 clamping, persist limit across reboots by @utajum in https://github.com/utajum/g-helper-linux/pull/4

### New Contributors
* @utajum made their first contribution in https://github.com/utajum/g-helper-linux/pull/3
