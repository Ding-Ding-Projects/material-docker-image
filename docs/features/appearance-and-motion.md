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

## Status

Built and covered by UI-state checks for colour conversions, contrast, presets, import validation and undo. The animations, templates and floating panel have not yet been exercised interactively on Windows.
