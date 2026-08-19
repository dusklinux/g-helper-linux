# Instructions for AI Agents & Pair Programmers

> **CRITICAL INSTRUCTIONS FOR ALL AI AGENTS WORKING ON THIS CODEBASE**
> Always read [UPSTREAM_SYNC.md](./UPSTREAM_SYNC.md) before porting, syncing, or proposing changes.

## Core Rules & Invariants

1. **Debloated Subsystems (Never Re-Add)**:
   - **Languages**: `src/I18n/Languages/English.cs` is the **only** language file. All 37 other translations were intentionally deleted. Do not recreate them.
   - **Display / Windowing**: Pure Wayland via `Avalonia.Wayland` and `HyprlandBackend.cs`. Do not add X11/XWayland, `libX11`, `xinput`, or `wlr-randr`.
   - **Audio**: Pure PipeWire via `wpctl` and `ghelper-audio`. Do not add `pactl` or PulseAudio fallbacks.
   - **Easter Eggs**: Do not restore `ArcadeWindow` or minigames.
   - **Kernel Target**: Linux 7.1+ baseline. No legacy (<6.2) kernel workarounds.

2. **Protected Features (Never Remove or Break)**:
   - **Robot Voice / DSP Audio**: Keep all PipeWire DSP vocoder, delay, reverb, and pitch modulation features in `audio-helper/` and `AudioWindow.axaml` intact.
   - **Dynamic Theming (Matugen / Material You)**:
     - Real-time watcher in `src/Helpers/MatugenTheme.cs` reading `$XDG_CONFIG_HOME/matugen/generated/hyprland-colors.lua`.
     - All UI controls, popups, dialogs, visualizers, meters, and text **must** use dynamic Matugen brushes (`{DynamicResource ...}` or `MatugenTheme` getters).
     - **Never hardcode hex colors** (`#4CC2FF`, `#06B48A`, `#50C878`, `#262626`, `#1C1C1C`, etc.) or user paths.
   - **Icons**: Default is Papirus.

3. **Versioning & Testing**:
   - Version must remain `2.x.x` in `src/GHelper.Linux.csproj`.
   - All tests (`dotnet run -c Debug --project tests/GHelper.Linux.Tests`) must pass 100%.
   - Verify native build (`bash build.sh`).
