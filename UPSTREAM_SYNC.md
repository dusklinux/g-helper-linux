# Upstream Sync & Architecture Boundary Guide

> **CRITICAL INSTRUCTIONS FOR AI ASSISTANTS AND DEVELOPERS**
> This repository (`dusklinux/g-helper-linux`) is a debloated, Pure Wayland/Hyprland, PipeWire-only, dynamically themed (Matugen Material You) fork of G-Helper Linux for modern Linux kernels (7.1+ baseline).
>
> When asked to **port, sync, or merge upstream improvements** (from `seerge/g-helper`, `the-darkvoid/g-helper-linux`, or any other upstream repository), you **MUST STRICTLY ADHERE TO THE BOUNDARIES OUTLINED BELOW**. Do **NOT** perform wholesale merges or blind cherry-picks.

---

## 1. Strictly Excised Subsystems (DO NOT RE-ADD OR RESTORE)

These components were deliberately removed to debloat the application, reduce binary footprint, and modernize the stack. **Never re-import or restore them**:

| Subsystem | What was Excised | Rule for Future Syncs |
| :--- | :--- | :--- |
| **Non-English Languages** | 37 non-English language files (`Arabic.cs`, `Russian.cs`, `Chinese.cs`, `German.cs`, `French.cs`, `Spanish.cs`, etc.) | **English only**. Only maintain `src/I18n/Languages/English.cs`. If upstream adds new localization keys, add them **exclusively** to `English.cs`. |
| **X11 / XWayland Stack** | `Avalonia.X11`, `libX11`, `libxcb`, `libXrandr`, `xinput`, `X11Strut.cs`, GLX renderers | **Pure Wayland only**. Never re-add X11 packages, dependencies, P/Invokes, or XWayland workarounds. |
| **Legacy Compositor Tooling** | `vendor/wlr-randr`, `gdctl`, `kscreen-doctor`, `KwinRules.cs` | Never re-add external compositor CLI wrappers. Display and input management must use native `HyprlandBackend.cs` (or standard Wayland protocols). |
| **Arcade Game Easter Egg** | `ArcadeWindow.axaml`, `ArcadeWindow.axaml.cs` (~1,100 lines of canvas space shooter game) | **Never re-add minigames or easter eggs**. |
| **PulseAudio Fallbacks** | `pactl` commands, PulseAudio daemon fallbacks | **Pure PipeWire only** via `wpctl` and bundled `ghelper-audio`. |
| **Distro-Specific Bloat** | `NixOS.cs`, `Cosmic.cs`, `nixos/` packaging tree | Keep codebase distribution-agnostic and clean. |
| **Legacy Kernel Shims (<6.2)** | Obsolete kernel workarounds, sysfs fallback warnings | Target baseline is **Linux 7.1+**. Rely on modern `asus-armoury` firmware attributes and standard sysfs interfaces. |

---

## 2. Protected Custom Features (DO NOT OVERWRITE OR REMOVE)

These custom additions are core to this fork. When porting upstream changes, ensure these components remain 100% intact and functional:

### A. Robot Voice & PipeWire DSP Audio Engine
- **Files**: `audio-helper/`, `src/UI/Views/AudioWindow.axaml`, `src/UI/Views/VocoderWindow.axaml`, `src/UI/Views/DelayWindow.axaml`, `src/UI/Views/ReverbWindow.axaml`, `src/UI/Views/NoiseWindow.axaml`.
- **Function**: Bundled C PipeWire DSP helper (`ghelper-audio`) providing real-time microphone vocoder, robotic pitch modulation, delay, algorithmic reverb, and RNNoise suppression.
- **Rule**: **NEVER remove or alter the DSP modulation architecture.**

### B. Matugen (Material You) Live Dynamic Theming
- **Files**: `src/Helpers/MatugenTheme.cs`, `src/UI/Styles/GHelperTheme.axaml`, `src/UI/Controls/WaveformView.cs`, `src/UI/Controls/SpectrumView.cs`, `src/UI/Controls/KnobControl.cs`.
- **Function**: Automatically parses colors from `$XDG_CONFIG_HOME/matugen/generated/hyprland-colors.lua` (with runtime fallback to `~/.config/matugen/...` via `Environment.GetFolderPath`), dispatches live `ThemeChanged` events, and updates all Avalonia resources in real time.
- **Rule**:
  - **No hardcoded static UI colors** (`#4CC2FF`, `#06B48A`, `#50C878`, `#262626`, `#1C1C1C`, `#000000`, etc.) in AXAML templates, custom views, dialogs, charts, dials, meters, or buttons.
  - Always use dynamic resource bindings (`{DynamicResource AccentColor}`, `{DynamicResource WindowBackground}`, `{DynamicResource PanelBackground}`, `{DynamicResource TextForeground}`, `{DynamicResource TextDim}`, `{DynamicResource AccentForeground}`) or the `MatugenTheme` helper getters (`MatugenTheme.GetAccentBrush()`, `MatugenTheme.GetAccentForegroundBrush()`, `MatugenTheme.GetPanelBackgroundBrush()`, `MatugenTheme.GetTextForegroundBrush()`, `MatugenTheme.GetTextDimBrush()`).
  - **Never hardcode usernames** in paths.

### C. Default Icon Set
- **Icon Set**: **Papirus** (`src/UI/Assets/Icons/papirus`) is the primary default icon theme.

### D. Versioning & Self-Updater Channel
- **Release Channel**: Points to `dusklinux/g-helper-linux` (`src/UI/Views/UpdatesWindow.axaml.cs`).
- **Semantic Version**: `v2.x.x` (managed in `src/GHelper.Linux.csproj`). Never downgrade to `1.x.x`.

---

## 3. What to Accept and Port from Upstream

When reviewing upstream commits, cherry-pick and adapt **only** the following categories of improvements:

1. **Hardware Support & Quirks**:
   - New ASUS ROG / TUF / Zephyrus / Flow / Strix / ProArt model IDs and DMI definitions.
   - New fan curve hardware registers and temperature sensor mappings.
   - ACPI / WMI GUID updates for keyboard backlight, Slash lighting, or AniMe Matrix.
2. **Kernel & Firmware Attribute Drivers**:
   - Updates for `asus-armoury` and `asus-nb-wmi` attribute handling.
   - Power limit (SPL/SPPT/FPPT) and PPT curve updates for new AMD/Intel CPUs.
   - Dynamic Boost and TGP target adjustments for new NVIDIA/AMD GPUs.
3. **GPU Switching & PCIe Power Management**:
   - Safe dGPU unbind/rescan improvements and race-condition guards.
   - Bug fixes in `GPUModeControl.cs` or `GpuQueryGate.cs`.
4. **Hardware Telemetry & Battery Optimization**:
   - Battery health, charge cycle calculation, and charge threshold limit updates.
   - Intel / AMD CPU undervolting MSR/mailbox register enhancements.

---

## 4. Upstream Sync Step-by-Step AI Checklist

When performing a sync, follow this workflow:

1. **Inspect Upstream Diff**: Review commits individually. Categorize every change (hardware quirk, UI feature, language addition, etc.).
2. **Filter Out Debloated Code**:
   - Discard any changes to non-English language files (port only new English translation keys to `English.cs`).
   - Discard any X11/XWayland, PulseAudio, or legacy desktop workarounds.
   - Discard minigame/easter-egg additions.
3. **Adapt Hardware Improvements**: Port hardware definitions, WMI fixes, and GPU mode logic.
4. **Ensure Matugen Theming Compatibility**: If any new UI controls, dialogs, or windows are introduced from upstream:
   - Convert all static hex colors to `{DynamicResource ...}` tokens or `MatugenTheme` getters.
   - Ensure the new controls subscribe to `MatugenTheme.ThemeChanged` if doing custom drawing.
5. **Verify Version & Build**:
   - Ensure `<Version>` in `src/GHelper.Linux.csproj` remains on `2.x.x`.
   - Run the automated test suite: `dotnet run -c Debug --project tests/GHelper.Linux.Tests` (must pass 100%).
   - Run native build: `bash build.sh`.
