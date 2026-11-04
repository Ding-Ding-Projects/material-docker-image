# Appearance and motion

## Appearance editor

Settings > Appearance opens the appearance editor with a live preview. Every change applies immediately, is saved, and is recorded in local history; Undo and Redo keep the last 100 changes.

- **Presets**: seven built-in presets plus your own. Apply, save as, rename, delete, export to a file and import from one (validated, 64 KiB limit, unknown fields rejected), and copy or paste a style.
- **Colour**: theme (light, dark, system), seed colour, chroma, surface tint and per-role overrides. The colour picker has a saturation and value field usable with the keyboard, hue and alpha sliders, and editable values in HEX, RGB, HSL, HSV, HWB, CMYK, OKLab and OKLCH with copy buttons. Contrast badges show the WCAG ratio against each role's partner; colours outside sRGB are brought back into range with a warning.
- **Shape and density**: corner radius scale and density, which change buttons, fields, chips, menus, cards and dialogs.
- **Typography**: interface and code fonts from a searchable list of installed fonts, each drawn in its own font, plus heading weight.
- **Effects**: elevation strength and an optional rainbow accent that settles on the saved colour when motion is off.

Each setting has its own reset button. Right-click the app name, any rail item or the page area and choose Edit appearance, or press `Ctrl+Shift+E`, to open a floating panel for the element under focus: it finds the colour roles that element uses and edits them in place.

## Motion

Motion follows Material 3 easing and duration tokens. Choose Off, Reduced, Standard or Expressive and a speed from 0.5x to 2x.

- Moving between rail destinations slides in the direction of the rail; other page changes fade through. Tabs slide.
- Dialogs grow in over a fading scrim, notifications slide in and fade out, and the output panel animates its height.
- The rail indicator grows from its centre, buttons respond to presses, switches slide and expanders reveal their content.
- Table rows fade in on first load.

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
