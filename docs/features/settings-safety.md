# Settings safety: locks, schedules, attention modes, sounds and collapsible filters

This guide covers five related controls in Settings and the shell. Every rule below is a framework-free class in `src/MaterialDockerImage/UI/SettingsSafety.*.cs` with focused checks in `tests/UiState/Program.cs`. The window code only reads their decisions. None of this has been driven in the built window yet; see the verification section.

## Element locks

Every settings row that holds a value has a lock button beside its title.

- A locked row is read-only: its control is disabled and a note says "Locked. Unlock this setting to change it."
- Locking needs no confirmation. Unlocking asks for one, and the confirmation repeats the disclosure below.
- Locks are stored in `settings.json` as `LockedSettings` (row identifiers, at most 200) and every lock or unlock is recorded in local history ("Locked setting: ...").
- Import and Reset all keep locked rows at their current values (`ElementLocks.Preserve`). Reset leaves the locks themselves in place.
- The command palette controls (theme, language, sounds, motion, contrast, the attention modes and School mode) and the accessibility menu are disabled while their row is locked, so a lock cannot be walked around.

Disclosure shown every time: a lock is a speed bump you set for yourself, not security. It does not protect anything from another person who has this computer. To clear every lock, remove `LockedSettings` from `settings.json` (the Data and privacy section shows the folder) or use Reset all after unlocking.

Not implemented: PIN, password and TOTP policies, locks on arbitrary elements through a context menu, and the Support Tickets recovery desk.

## Scheduled language and theme

Settings, Schedules. A rule sets the language or the theme for a time window.

| Field | Meaning |
| --- | --- |
| Setting | Language or Theme |
| Source | Local (the rule's own value) or File (the value comes from the source file) |
| Days | Every day, or chosen weekdays |
| From, until | Local clock times, `HH:mm`. The end is not included. |
| Priority | -5 to 5 in the editor (stored range -100 to 100) |

Semantics, all covered by checks:

- Times are in this computer's time zone and follow daylight saving changes.
- A window is from the start up to, but not including, the end.
- Equal start and end mean the whole day (still limited to the selected weekdays).
- A start later than the end crosses midnight. The part before midnight uses that day, the part after midnight belongs to the window that started the day before.
- A rule with an unparseable time, no weekday selected or a disabled switch is never active.
- Higher priority wins; equal priorities: the later rule in the list wins. Language and theme are decided independently.
- At most 50 rules; duplicate or missing identifiers are repaired on load.

A schedule only lends its value. `UserSettings.ApplyScheduled` keeps the person's own choice as the base and `ToJson` always writes the base, so the saved file never contains a borrowed value. When the window ends the base returns. If the person changes the language or theme by hand while a schedule is active, that choice becomes the new base.

The runtime (`Services/ScheduleRuntime.cs`) evaluates every 20 seconds, when rules change and when the source file changes, and shows one non-blocking notice when the applied value changes.

### Source file

An optional local JSON file, chosen in Settings, Schedules:

```json
{ "version": 1, "language": "Cantonese", "theme": "Light" }
```

Both fields are optional, no other fields are accepted, the version must be 1 and the file must be under 4 KiB. The whole file is refused if anything is wrong. The file is watched with a debounce. A missing or invalid file pauses the rules that use it, shows one warning with an "Open schedules" action, and leaves the person's own settings in effect. Nothing is fetched from a network.

Not implemented: scheduling density, colours, fonts, motion or the display name, a date range, the HTTPS API source and the Home Assistant source.

Every rule add, change, enable, disable and delete, and every change to the source file, is saved with a local history record.

## Attention modes

All five are independent switches in Settings, Focus modes, and in the command palette. `AdhdRules.Resolve` is the single decision the window reads.

| Mode | What it changes |
| --- | --- |
| Focus | Hides the version line and the accessibility button, narrows the rail and hides its labels, and shows an Exit focus button in the title bar. Nothing is removed that cannot be brought back in one step. |
| Low stimulation | Turns off action sounds and animation, and hides the Momentum chip. It adds to, and never overrides, Reduced motion and the Windows animation setting. |
| Time awareness | Shows a clock in the title bar. |
| One thing at a time | Opening another destination reuses the current tab. |
| Momentum | Shows a chip with the number of operations finished in a row this session. A failure or a cancel resets it. The streak is never saved. |

Momentum is fed by `AppHost.Execute`, so it counts real Docker operations only.

## Action sound effects

One playback path, `Services/ActionSounds.cs`. A cue plays only when the Action sounds switch is on, Low stimulation is off and the window is not producing captures (`SoundCueRules.MayPlay`). Playback runs on a background task, swallows device errors and never changes the action's result. A visible message always accompanies the cue. The switch applies immediately and is saved.

| Cue | Triggered by | Windows sound |
| --- | --- | --- |
| Success | An operation or build finished successfully | Asterisk |
| Failure | An operation or build failed | Hand |
| Warning | A warning notification, or a canceled operation | Exclamation |
| Removal | A remove, prune, kill or delete operation finished successfully | Question |
| Copy | Text was copied to the clipboard (after the clipboard accepted it) | Beep |
| Drop | A dropped wording file was applied | tada.wav, or Asterisk if missing |
| DropRefused | A dropped file was refused | Beep |

The mapping is `SoundCueRules.Mapping`; a check requires every cue to have a row. The same table appears in Settings, Sounds and narration. No audio file ships with the app. There is no volume or quiet-hours control.

## Collapsible filters

`CollapseFilterState` stores only collapsed surfaces in `UserSettings.CollapsedFilters` (at most 64 keys of at most 60 characters).

- Every `DataTable` with filter chips has a chevron beside its search box (key `table.<name>.filters`). Collapsed, it shows "Filters · N on". The search box stays visible.
- Storage statistics (`storage.stats`) and the Activity action filters (`activity.actions`) use `CollapsibleBar`.
- The chevron has an accessible name that says whether it will show or hide, and is reachable by keyboard.

## Languages

All new copy has Hong Kong Cantonese wording in `Services/LocStrings.Settings.cs`. A check requires Cantonese wording for a sample of the new strings and the English-then-Cantonese form in bilingual mode.

## Verification

Run `build.bat -Tests`. The UI-state project covers locks, schedule boundaries (start, last minute, end, cross-midnight, weekday ownership, whole day, priority, file values), the base-versus-live behaviour of the language and theme, the attention rules, the sound mapping and the collapsed state. During development one boundary check was watched failing on a deliberately wrong weekday rule before it was fixed, and a gap it did not cover (a same-day window using the wrong weekday) led to an extra check.

Not verified: any interaction in the built window, real sound playback, the file watcher, the 20-second timer across a real boundary, screen reader names and narrow layouts. These stay open on the capability inventory.
