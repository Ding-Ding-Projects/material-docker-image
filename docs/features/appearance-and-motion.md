# Appearance and motion

## Appearance editor

Settings > Appearance opens the appearance editor with a live preview. Every change applies immediately, is saved, and is recorded in local history; Undo and Redo keep the last 100 changes.

- **Presets**: seven built-in presets plus your own. Apply, save as, rename, delete, export to a file and import from one (validated, 64 KiB limit, unknown fields rejected), and copy or paste a style.
- **From wallpaper**: a preset tile in the studio that reads the current Windows desktop background and sets the seed colour from it. It asks Windows for the wallpaper path (`SystemParametersInfo`, then `HKCU\Control Panel\Desktop\WallPaper`), decodes the picture at 96 pixels wide and picks the most prominent colourful bucket (4 bits per channel, weighted towards saturated pixels, transparent pixels ignored). With no picture set (a solid colour, or a gap in a slideshow) it uses the solid background colour from `HKCU\Control Panel\Colors\Background`. A missing, oversized (over 64 MiB) or undecodable picture is reported as unreadable and the seed is left alone. It only reads: nothing in the registry or the file is written. The wallpaper is read only when the tile is chosen, never in documentation captures.
- **Compare A/B**: the studio's **Compare A/B** button opens a comparison below the panes. A is the current appearance; B is a preset, the wallpaper colour or a typed seed. B's colour roles are computed by the same function the theme uses (`Theme.Roles`) into a resource dictionary that belongs to B's preview only, so the live theme does not change. Each side shows its own AA contrast verdict. **Apply B** applies the candidate as one undoable change that keeps your motion settings and every locked value.
- **Colour**: theme (light, dark, system), seed colour, chroma, surface tint and per-role overrides. The colour picker has a saturation and value field usable with the keyboard, hue and alpha sliders, and editable values in HEX, RGB, HSL, HSV, HWB, CMYK, OKLab and OKLCH with copy buttons. Contrast badges show the WCAG ratio against each role's partner; colours outside sRGB are brought back into range with a warning.
- **Shape and density**: corner radius scale and density, which change buttons, fields, chips, menus, cards and dialogs.
- **Typography**: interface and code fonts from a searchable list of installed fonts, each drawn in its own font, plus heading weight.
- **Effects**: elevation strength and an optional rainbow accent that settles on the saved colour when motion is off.

Each setting has its own reset button. Right-click the app name, any rail item or the page area and choose Edit appearance, or press `Ctrl+Shift+E`, to open a floating panel for the element under focus: it finds the colour roles that element uses and edits them in place.

## Per-element customization

Any element can be customized on its own, beyond the shared colour roles. Open the editor from **Customize this element…** (right-click menu of the page area, or the button in the **Edit appearance** panel), from the command palette (**Customize the focused element…**), or with **Pick an element to customize…**, which turns the pointer into a cross and uses the next click without pressing what is under it (Escape cancels).

- **What it edits**: background colour and text colour (the same infinite picker, with HEX, RGB and HSL), corner radius (0 to 48), font size (8 to 40) and weight (300 to 700), padding (0 to 40) and visibility (hidden elements keep their space). Each property is switched on separately and previews live on the element itself. A contrast warning appears when text and background are both set and fall under 3:1.
- **Identity**: an element is found again by its path from the window (type, name where it has one, position among siblings of the same type), for example `MainWindow/Grid/Border[2]/Button#btn.build`. The key is stable across restarts and page rebuilds while the layout around the element is unchanged; if the layout changes, the override no longer matches and is listed under Overrides until reset.
- **Persistence**: overrides live in `element-appearance.json` in the data folder (strict format, 256 KiB and 200-element limits, written atomically) and are recorded in local history when the editor closes. An unreadable file is reported and never overwritten.
- **Overrides list**: the **Overrides** tab lists every override with a plain-words description, a search with its own regular expression builder, **Reset** per element and **Reset all customized elements** (confirmed). **Reset element** and **Revert changes** work from the Element tab.
- **Locks**: a property whose appearance setting is locked in Settings (colour roles, corner radius, font size, density for padding) is shown read-only in the editor, with the same note as a locked setting row.
- **Applying**: overrides are applied to live elements when they change and to each element as it loads; removing an override restores the element's original value, binding or resource reference.

Not built: per-element overrides are not yet part of appearance presets, export or Undo, only a subset of the property types the shared contract lists (no italic, underline, shadow, spacing or alignment), and the editor has not been driven in the built window.

## Motion

Motion follows Material 3 easing and duration tokens. Choose Off, Reduced, Standard or Expressive and a speed from 0.5x to 2x.

- **Transition pattern**: page and tab changes use the pattern chosen in the studio cards or the Transition pattern row: shared axis (the default; moves along the direction of travel in rail or tab order), fade through (fades in while growing slightly), container transform (grows out of a smaller container) or slide (comes in from the side of travel). Choosing a pattern plays it on the live preview. With Off nothing animates; with Reduced, Reduced motion or Low stimulation nothing moves and pages change with a 200 ms fade only.
- **Spring bounce**: 0% to 100% (default 30%). At the Expressive level the emphasized easing used for movement and scale becomes a spring curve that overshoots its end by up to about 20% before settling; opacity never bounces. The studio's **Spring** easing chip draws the curve for the current bounce. At every other level, and whenever Reduced motion, Low stimulation or the Windows animation setting stops movement, the slider is disabled with the reason shown.
- Dialogs grow in over a fading scrim, notifications slide in and fade out, and the output panel animates its height.
- The rail indicator grows from its centre, buttons respond to presses, switches slide and expanders reveal their content.
- Table rows fade in on first load.

Both new motion values are saved in settings.json (`TransitionPattern`, `SpringBounce`), are part of Undo, local history and the settings export, can be locked like every other appearance value (lock ids `transition` and `springbounce`), are kept when a preset is applied, and are validated in imported presets.

Reduced motion, Low stimulation, the Windows animation setting and capture mode always win over the saved level. Every animation ends on the element's normal state, so an interrupted animation never leaves anything hidden or moved.

## Language

English, Cantonese and Bilingual modes cover the shell and every destination page: headers and subtitles, section titles, field labels and helper text, buttons and menu items, empty states, dialog titles and bodies, tooltips, accessible names, status lines and notifications. Cantonese is written Hong Kong style in Traditional characters; Docker terms such as Compose, BuildKit, Dockerfile and image digest stay in English where that reads naturally. Bilingual shows the English text first, then the Cantonese text after a middle dot.

- The phrase list lives in `Services/Loc.cs` (shell labels) and `Services/LocStrings.*.cs` (page copy). Entries whose key contains `{0}`, `{1}` and so on are sentence templates: the numbers, names and paths in the English message stay exactly as the engine or the app produced them, and only the wording around them is translated.
- Status lines under page titles are translated phrase by phrase (`Loc.Line`), so counts, context names and times stay literal.
- Docker CLI output, engine data (image, container, volume and network names, tags, digests, log lines) and error text returned by Docker are shown as returned and are never translated. A few long explanations built from several fragments, or from conditional wording that does not form one fixed sentence, are still English in every mode; School mode forces English everywhere.
- Personal wording files still win over the built-in Cantonese. Clearing them restores the built-in text.

## Application icon, title-bar logo and display name

- **Application icon**: a multi-size icon (16, 20, 24, 32, 40, 48, 64, 128 and 256 px) is set as the executable icon, the window icon and the Squirrel setup icon. It is the `deployed_code` Material Symbol drawn in the seed colour on a dark rounded tile. `build/make-icon.py` regenerates `Assets/Icon/app.ico` and `app-256.png` from the bundled font; the outputs are committed, so a normal build does not need Python.
- **Title-bar logo**: Settings > Appearance > Title-bar logo chooses the default glyph, any bundled Material Symbol by name (case, spaces and underscores ignored, with clickable suggestions), or a local PNG, ICO or JPG. A custom image is checked by its actual bytes (extension and signature must agree), limited to 1 MiB and 2048 by 2048 pixels, copied into the `logo` folder of the app data folder and never uploaded. Use default logo resets it and deletes the copy. A missing or damaged file, or an unknown symbol, shows the default logo with a notice. Each change is saved at once and recorded as one local history revision. The logo changes presentation only: the package id, executable name, data folders and update feed are untouched.
- **Display name**: Settings > About > Display name accepts 1 to 80 characters without control characters. Save or Enter applies it to the title bar and window title; Reset returns the shipped name Material Docker Image in one action. A rename or reset that really changes the name records exactly one local history revision; saving the same name records nothing. About shows the shipped product name and the diagnostics report keeps it, so a renamed copy is still identifiable. The settings, history and logo folders do not move.

Not built yet: crop, fit and background editing for a custom logo, scheduled settings and appearance export of the logo. Status: covered by UI-state checks for name and logo validation, stored-file handling and fallback; interactive verification in the built window and the installed shortcut icon are pending.

## Status

Built and covered by UI-state checks for colour conversions, contrast, presets, import validation and undo. The animations, templates and floating panel have not yet been exercised interactively on Windows.
