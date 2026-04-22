# Icon sets

G-Helper Linux ships 21 icon sets, switchable at runtime via Extra →
Appearance → Icon Set. The default is `noto`. Selection changes apply live
across every open window; the chosen slug is persisted in
`~/.config/ghelper/config.json` under the `icon_set` key.

## How discovery works

Every folder under this directory is a set. The slug is the folder name.
At build time, the `GenerateIconSets` MSBuild task enumerates these folders
and emits `IconSetsGenerated.g.cs` with a string array of slugs - this list
is baked into the binary (AOT-safe, zero runtime enumeration cost).

The `ValidateIcons` MSBuild task simultaneously enforces that every set
contains the same base SVGs as the `noto` reference set (bold variants,
ending in `-bold.svg`, are optional - only `tabler`, `phosphor`, and
`tabler-phosphor` currently ship them; all other sets fall back to base
glyphs for section headers). If any set is missing one of the 15 required
base icons, the build fails before compilation starts.

## Adding a new set

1. Create a new folder under this directory, e.g. `fluent-3d/`.
2. Drop the 15 required SVG files named exactly:
   `bolt`, `leaf`, `balance`, `rocket`, `cyclone`, `gamepad`, `herb`,
   `desktop`, `bulb`, `keyboard`, `gear`, `battery`, `package`, `power`,
   `warning` (all `.svg`).
3. Optionally add any `*-bold.svg` variants for section headers. Missing
   bold variants fall back to the non-bold version automatically.
4. Rebuild. The dropdown populates itself from the folder list; the display
   name is title-cased from the slug (`fluent-3d` → "Fluent 3d"; rename to
   `fluent-3D` if you want the capital preserved, or add logic to
   `IconSets.DisplayName`).
5. Append an attribution entry below.

## Semantic icon names

Required (15): `bolt`, `leaf`, `balance`, `rocket`, `cyclone`, `gamepad`,
`herb`, `desktop`, `bulb`, `keyboard`, `gear`, `battery`, `package`,
`power`, `warning`.

Optional bold variants (5, used in section headers): `bolt-bold`,
`gamepad-bold`, `desktop-bold`, `keyboard-bold`, `battery-bold`.

## Attribution

### `noto/`

14 SVGs from Google's Noto Emoji project
(<https://github.com/googlefonts/noto-emoji>), licensed under the SIL Open
Font License 1.1 (OFL). `power.svg` is a hand-crafted IEC 5009 power symbol
in red, matching the visual of the `power.png` that shipped before the
icon overhaul (PR #72).

### `tabler/`

Pure Tabler Icons (<https://github.com/tabler/tabler-icons>), MIT licensed.
Base glyphs use the `outline/` variants (thin stroke, 24x24 viewBox); the
5 bold variants for section headers (`bolt-bold`, `battery-bold`,
`desktop-bold`, `gamepad-bold`, `keyboard-bold`) use the corresponding
`filled/` variants from the same library.

### `phosphor/`

Pure Phosphor Icons (<https://github.com/phosphor-icons/core>), MIT
licensed. Base glyphs use the `regular` weight (256x256 viewBox); the 5
bold variants for section headers use the `bold` weight from the same
library.

### `tabler-phosphor/`

Mixed set originally extracted from PR #72 (`src/UI/Styles/Icons.axaml`
in that PR) and wrapped into standalone SVGs here. Base glyphs use Tabler
outline (<https://github.com/tabler/tabler-icons>, MIT); bold variants
use Phosphor bold (<https://github.com/phosphor-icons/core>, MIT). Both
licences compatible.

### `openmoji/`

Colour variant from the OpenMoji project
(<https://github.com/hfg-gmuend/openmoji>), licensed under CC BY-SA 4.0.
Attribution: "OpenMoji - the open-source emoji and icon project. License:
CC BY-SA 4.0". 14 emojis plus the native U+23FB POWER SYMBOL as
`power.svg`.

**Note:** the `viewBox` attribute on each SVG has been tightened to the
glyph's content bounding box (+1.5-unit margin) so icons render at
comparable visual size to Noto/Twemoji/Blobmoji in G-Helper's fixed
icon slots. The upstream SVGs use a 72x72 viewBox that reserves
whitespace for baseline alignment during inline-with-text rendering -
without the tightening, icons like `leaf` and `battery` would fill only
~40-50% of the slot. Path data, fills, and attribution are unchanged.
Run `./scripts/tighten-icon-viewboxes.sh src/UI/Assets/Icons/openmoji`
after refreshing from upstream to re-apply.

### `openmoji-black/`

Monochrome outline variant from the same OpenMoji project, same CC BY-SA
4.0 licence and attribution requirements. 14 emojis plus native U+23FB.
Same `viewBox` tightening applied as `openmoji/` (see note above).

### `twemoji/`

Twitter Emoji, community-maintained jdecked fork
(<https://github.com/jdecked/twemoji>), graphics licensed under CC BY 4.0.
14 emojis; `power.svg` is copied from `noto/` because Twemoji does not
include U+23FB.

### `fluent-flat/`

Microsoft Fluent UI Emoji, Flat style
(<https://github.com/microsoft/fluentui-emoji>), MIT licensed. 14 emojis;
`power.svg` is copied from `noto/` because the Fluent library does not
include a power symbol variant. `viewBox` attributes have been tightened
via `scripts/tighten-icon-viewboxes.sh` for consistent visual size in
G-Helper's fixed icon slots (gains ~10% here since upstream Fluent uses
a tighter viewBox than OpenMoji).

### `blobmoji/`

The "blob" emoji style Google used in Android 4.4-8.0, preserved by the
community at <https://github.com/C1710/blobmoji>, Apache 2.0 licensed. 14
emojis; `power.svg` is copied from `noto/` because Blobmoji does not
include U+23FB.

### `fluent-color/`

Microsoft Fluent UI Emoji, Color style (detailed rendering with gradients,
richer than Flat) from <https://github.com/microsoft/fluentui-emoji>, MIT
licensed. 14 emojis; `power.svg` is copied from `noto/` because the Fluent
library does not include a power symbol variant. `viewBox` attributes have
been tightened via `scripts/tighten-icon-viewboxes.sh` for consistent
visual size in G-Helper's fixed icon slots.

### `fluent-high-contrast/`

Microsoft Fluent UI Emoji, High Contrast style (monochrome black-on-white
for accessibility) from <https://github.com/microsoft/fluentui-emoji>, MIT
licensed. 14 emojis; `power.svg` from `noto/`. SVG `fill="#212121"` was
normalised to `#FFFFFF` at download time so glyphs render white on the
dark G-Helper theme. `viewBox` tightening applied.

### `lucide/`

Lucide Icons (<https://github.com/lucide-icons/lucide>), a community fork
of Feather Icons. ISC licensed. 2px-stroke outline style, 24x24 viewBox.
Distinct from Tabler's sharper geometry - Lucide favours softer, rounded
terminals.

Mappings: bolt→`zap`, leaf→`leaf`, balance→`scale`, rocket→`rocket`,
cyclone→`wind`, gamepad→`gamepad`, herb→`leafy-green`, desktop→`monitor`,
bulb→`lightbulb`, keyboard→`keyboard`, gear→`settings`, battery→`battery`,
package→`package`, power→`power`, warning→`triangle-alert`.

### `phosphor-light/`

Light-stroke weight variant of Phosphor Icons (MIT,
<https://github.com/phosphor-icons/core>). Same semantic mapping as the
base `phosphor/` set: lightning, leaf, scales, rocket, fan,
game-controller, plant, desktop, lightbulb, keyboard, gear, battery-full,
package, power, warning.

No `*-bold.svg` variants ship with this set - the Icon control falls
back to base for section headers. `fill` normalised to `#FFFFFF`.

### `material-outlined/`

Google Material Symbols, outlined style
(<https://github.com/google/material-design-icons>), Apache 2.0 licensed.
viewBox is `0 -960 960 960` (Google's standard for Material Symbols web
SVGs).

Mappings: bolt→`bolt`, leaf→`eco`, balance→`balance`, rocket→`rocket_launch`,
cyclone→`air`, gamepad→`gamepad`, herb→`grass`, desktop→`monitor`,
bulb→`lightbulb`, keyboard→`keyboard`, gear→`settings`,
battery→`battery_full`, package→`package`, power→`power_settings_new`,
warning→`warning`.

Source SVGs had no `fill` attribute (default black); `fill="#FFFFFF"` was
injected on the `<svg>` element at download time.

### `bootstrap/`

Bootstrap Icons (<https://github.com/twbs/icons>), MIT licensed. 16x16
viewBox, classic web-utility style.

Mappings: bolt→`lightning-charge`, leaf→`tree` (Bootstrap has no plain
"leaf"), balance→`yin-yang` (represents balance of opposing forces;
Bootstrap has no literal scale), rocket→`rocket-takeoff`,
cyclone→`tornado`, gamepad→`joystick`, herb→`flower2`,
desktop→`pc-display`, bulb→`lightbulb`, keyboard→`keyboard`, gear→`gear`,
battery→`battery-full`, package→`box-seam`, power→`power`,
warning→`exclamation-triangle`.

### `iconoir/`

Iconoir (<https://github.com/iconoir-icons/iconoir>), MIT licensed. 24x24
viewBox, 1.5px stroke.

Mappings include creative substitutions where Iconoir lacks a direct match:

- bolt→`flash`, leaf→`leaf`, rocket→`rocket`, cyclone→`spiral` (great
  match - coiled shape), gamepad→`gamepad`, desktop→`computer`,
  bulb→`light-bulb`, gear→`settings`, battery→`battery-full`,
  package→`package`, warning→`warning-triangle`.
- balance→`weight` - Iconoir has no balance scale; `weight` (a shipping
  weight with dimension markings) is the closest semantic fallback.
- herb→`flower` - no "herb" icon; `flower` stands in for plant/nature.
- keyboard→`input-field` - Iconoir has no keyboard icon; `input-field` is
  a horizontally-oriented rectangle that visually rhymes with a keyboard.
- power→`system-shut` - classic circle-with-vertical-line power symbol,
  just named `system-shut` in Iconoir.

### `pixelarticons/`

Retro 8-bit pixel-art icons from
<https://github.com/halfmage/pixelarticons>, MIT licensed. 24x24 viewBox,
every path snapped to a 1-unit pixel grid.

Mappings: bolt→`zap`, leaf→`leaf`, balance→`scale`, cyclone→`wind`,
gamepad→`gamepad`, desktop→`monitor`, bulb→`lightbulb`, gear→`settings-cog`,
battery→`battery-full`, package→`package`, power→`power`.

Creative substitutions for missing icons:

- rocket→`send` - Pixelarticons has no rocket; `send` (paper-plane shape)
  captures the outbound-launch motif.
- herb→`tree-pine` - no "herb"; a pine tree stands in for plant/nature.
- keyboard→`keyboard-music` - only keyboard variant; includes a small
  musical note on top.
- warning→`warning-diamond` - diamond-shaped warning sign (road-hazard
  style) since Pixelarticons has no triangle warning.

### `fluent-system/`

Microsoft Fluent UI System Icons
(<https://github.com/microsoft/fluentui-system-icons>), MIT licensed. This
is Microsoft's modern UI icon library (distinct from Fluent Emoji). 24x24
viewBox, clean filled glyphs. `fill="#212121"` normalised to `#FFFFFF`.

Mappings: bolt→`Flash/flash`, leaf→`Leaf One/leaf_one`,
balance→`Scales/scales`, rocket→`Rocket/rocket`,
cyclone→`Fluid/fluid` (swirl pattern - closest to cyclone motif),
gamepad→`Games/games`, herb→`Plant Grass/plant_grass` (grass tuft),
desktop→`Desktop/desktop`, bulb→`Lightbulb/lightbulb`,
keyboard→`Keyboard/keyboard`, gear→`Settings/settings`,
battery→`Battery 10/battery_10` (full 10-segment battery),
package→`Box/box`, power→`Power/power`, warning→`Warning/warning`.

### `papirus/`

Papirus Icon Theme
(<https://github.com/PapirusDevelopmentTeam/papirus-icon-theme>), GPL-3.0
licensed (compatible with G-Helper's own GPL-3.0). 16x16 symbolic
monochrome style from `Papirus/16x16/symbolic/` and `Papirus/16x16/`
(panel/actions). Many individual icons derive from GNOME freedesktop
icons (generally CC BY-SA 3.0) which Papirus re-packages and relicences
as GPL-3.0.

SVGs use a `ColorScheme-Text` CSS class with `color:#444444` (or
`#dfdfdf` in a couple of cases) plus `fill:currentColor` paths. The CSS
`color` value was rewritten to `#FFFFFF` at download time so glyphs
render white on the dark theme.

Mappings leverage Papirus' domain overlap with G-Helper's laptop
performance control (this set feels very "at home" next to GNOME's
power profile indicators):

- bolt→`boost` (Papirus' custom boost/turbo action icon)
- leaf→`power-profile-power-saver-symbolic` (GNOME's leaf icon for eco
  power profile - semantically perfect)
- balance→`power-profile-balanced-symbolic` (GNOME's balance-scale icon
  for the balanced power profile)
- rocket→`emoji-travel-symbolic` (travel/rocket glyph from the emoji
  category picker)
- cyclone→`weather-tornado-symbolic` (tornado - closest to "cyclone")
- gamepad→`input-gaming-symbolic`
- herb→`emoji-nature-symbolic` (leaf/plant glyph from the emoji nature
  category)
- desktop→`video-display-symbolic`
- bulb→`emoji-objects-symbolic` (lightbulb - the standard glyph for the
  emoji "Objects" category)
- keyboard→`input-keyboard-symbolic`
- gear→`configure` (Papirus' settings-symbolic is a symlink here)
- battery→`battery-100` (full-charge panel icon)
- package→`package-x-generic-symbolic` (generic package mimetype)
- power→`system-shutdown` (standard power-off glyph)
- warning→`state-warning`

Several icons in Papirus ship as git symlinks inside the symbolic folder,
which GitHub raw returns as plain-text paths; the download step resolved
each symlink to the actual target file.

### `game-icons/`

Game-Icons.net collection (<https://github.com/game-icons/icons>),
CC BY 3.0 licensed. 512x512 (some 256x256 "badge" variants) white-on-black
SVGs pulled from several artists: Lorc, Delapouite, Sbed, Skoll,
Lord Berandas, and the project's shared `badges/` folder. The black
background rectangle (`<path d="M0 0h512v512H0z"/>`) and badge background
circle (`<circle r="128"/>`) were stripped at download time so glyphs
render transparent on the dark G-Helper theme.

Mappings (per-artist attribution):

- bolt→`badges/bolt`, leaf→`badges/leaf`, warning→`badges/exclamation` -
  circular badge style with white ring + white glyph.
- balance→`delapouite/weight-scale` - double-pan balance scale (justice
  scales shape).
- battery→`sbed/battery-pack` - 4-cell battery pack (no simple battery
  exists in the set).
- bulb→`lorc/bulb`, cyclone→`lorc/tornado`, gear→`lorc/cog`,
  herb→`lorc/sprout`, rocket→`lorc/rocket`.
- desktop→`skoll/pc` - tower-and-monitor PC silhouette.
- gamepad→`delapouite/gamepad`, keyboard→`delapouite/keyboard`,
  package→`delapouite/cardboard-box`.
- power→`lord-berandas/power-button` - classic circle-with-vertical-line
  power symbol.

### `icomoon-free/`

IcoMoon-Free by Keyamoon (<https://github.com/Keyamoon/IcoMoon-Free>),
CC BY 4.0 / GPL free license. 16x16 flat pictograms with
`fill="#000000"`, normalised to `#FFFFFF` at download time.

Mappings include many creative substitutions (IcoMoon-Free has only ~491
icons and several required glyphs are missing):

- desktop→`display`, gear→`cog`, keyboard→`keyboard`, leaf→`leaf`,
  power→`power`, rocket→`rocket`, warning→`warning`.
- balance→`meter2` - needle-on-arc gauge (IcoMoon has no scale icon).
- battery→`power-cord` - plug/cord (no battery in the set).
- bolt→`magic-wand` - wand with sparkle (no lightning bolt in the set).
- bulb→`sun` - radiating rays (no lightbulb in the set).
- cyclone→`spinner11` - circular segmented spinner that reads as a
  rotating vortex.
- gamepad→`pacman` - Pac-Man silhouette, an iconic gaming symbol.
- herb→`tree` - no "herb" icon; tree stands in for plant/nature.
- package→`box-add` - box with plus mark, the closest package-like glyph.
