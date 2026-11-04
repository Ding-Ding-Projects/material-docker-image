# Settings safety: locks, schedules, attention modes, sounds and collapsible filters

This guide covers five related controls in Settings and the shell. Every rule below is a framework-free class in `src/MaterialDockerImage/UI/SettingsSafety.*.cs` with focused checks in `tests/UiState/Program.cs`. The window code only reads their decisions. None of this has been driven in the built window yet; see the verification section.

## Element locks

Every settings row that holds a value has a lock button beside its title, and every row and every major page action button (the filled and danger buttons) has **Lock this control...** on its right-click menu. Locking an element shares one helper (`Shell/LockUi.cs`) between settings rows and page buttons. Button lock ids are `btn.<page>.<label>` in lower case, at most 40 characters, so two buttons with the same label on the same page share a lock.

- A locked row or button is disabled, and a page that re-enables it (for example after an async action) is overruled. The right-click menu stays available on a locked element and offers **Unlock this control...**. The settings lock button also stays enabled as the unlock target. A disabled button cannot be clicked or focused, so unlocking a page button needs its context menu.
- Locks are stored in `settings.json` as `LockedSettings` (identifiers, at most 200) and every lock or unlock is recorded in local history ("Locked setting: ..."). The history never holds a credential.
- Import and Reset all keep locked rows at their current values (`ElementLocks.Preserve`). Reset leaves the locks themselves in place. A lock on a button has no settings value to preserve.
- The command palette controls (theme, language, sounds, motion, contrast, the attention modes and School mode) and the accessibility menu are disabled while their row is locked, so a lock cannot be walked around.

### Lock policies

The first time a control is locked, a wizard asks what unlocking should require. The choice is kept per lock and reused on the next lock; **Change lock policy...** reopens the wizard on an unlocked control and **Remove lock policy...** deletes the credential.

| Policy | Unlock asks for | Stored as |
| --- | --- | --- |
| None | A confirmation that repeats the disclosure below. | Nothing. |
| PIN | 4 to 12 digits. | A PBKDF2-SHA256 hash (150,000 iterations) with a random per-hash salt, mixed with a random per-install salt kept in the lock file. The PIN is never stored, logged or exported. |
| Authenticator app | A 6-digit RFC 6238 code (SHA-1, 30 seconds, one step of clock skew either side). A code that was accepted cannot be used again. | The 20-byte secret, generated with `RandomNumberGenerator`, protected with Windows DPAPI for the current user (entropy: the install salt). |

Credentials live in `locks.json` in the data folder, apart from `settings.json`, exports and history. An authenticator lock needs DPAPI; where it is unavailable the wizard says so and refuses the policy. In capture mode the store is held in memory only.

Pairing an authenticator shows the secret only after **Show secret and QR code**: a QR code of the `otpauth://totp/` link, the grouped Base32 secret, copy buttons and the algorithm, digits and period. The lock arms only after one current code is typed back.

### Unlock ladder

Unlocking climbs these rungs (`LockStore.Stage`, `LockStore.Attempt`, `LockStore.UseRecoveryCode`):

1. **Confirm.** A lock with no policy only asks for a confirmation.
2. **Credential.** The PIN or authenticator code. The first two wrong answers cost nothing and the dialog says how many free tries remain.
3. **Wait.** From the third consecutive wrong answer the wait is 30 seconds, then 60, 120 and so on, doubling to a cap of one hour. The dialog shows a live countdown and the clock time the wait ends. The failure count and the wait are saved, so a restart does not clear them. While waiting, an answer is refused without being checked and without counting. Waiting alone never unlocks anything: the credential is still needed afterwards.
4. **Recovery code.** Eight single-use codes (`XXXXX-XXXXX`) are made when a PIN or authenticator policy is saved, shown once with Copy all and Save to file, and stored only as hashes. A code works at any time, including during a wait, clears the failure count and the wait, and is spent. Typing ignores case, spaces, hyphens and the look-alike letters I, L and O.

Beside every prompt the dialog names the data folder and says that deleting `locks.json` (or the whole folder) clears every lock. That is the last resort when the PIN, the authenticator and every recovery code are gone.

Not built: the dim sum, ten sums and whack-a-mole rungs that can shorten a wait, with their hourly budget and server-side style grading; School mode starting the ladder at the sums; password and combined policies; unlock durations; locks on tabs, groups and appearance values; locks on every rendered element; the Support Tickets recovery desk.

Disclosure shown every time: a lock is a speed bump you set for yourself, not security. It does not protect anything from another person who has this computer. Anyone who can delete the data folder can remove every lock.

## Authenticator tool

Tools has an **Authenticator** section for the app's own locks. It pairs an authenticator app with a lock, checks a code for a lock without using it up or counting it as an unlock attempt, lists the locks that have a policy (with their recovery codes left and **Remove policy**) and runs **Run RFC 6238 self-test**, which computes the 18 published RFC 6238 vectors (SHA-1, SHA-256, SHA-512, 8 digits) with the build's own code. The command palette has **Authenticator for locks**. A lock registered in an authenticator on the same computer is mostly for fun, since the key sits beside the lock it opens.

Not built: a general authenticator for arbitrary accounts (live codes with countdown and next-code peek, import from an image, the clipboard or a camera, grouping and search, a secrets export behind super confirmation).

The QR encoder (`UI/SettingsSafety.Qr.cs`) is dependency free and draws nothing itself: byte mode, level L, versions 1 to 10 (271 bytes). Text beyond that is refused rather than truncated, and an otpauth link for a lock fits easily.

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

Run `build.bat -Tests`. The UI-state project covers locks (including `tests/UiState/LockChecks.cs`: Base32 and the RFC 4648 vectors, the RFC 4226 and RFC 6238 vectors, skew and replay, PBKDF2 hashing, recovery codes, the backoff table, the ladder stages with a fake clock across a restart, the credential file never holding a PIN, code or secret, real DPAPI, and the QR encoder checked against a published Reed-Solomon vector and against matrices an independent scanner decoded), schedule boundaries (start, last minute, end, cross-midnight, weekday ownership, whole day, priority, file values), the base-versus-live behaviour of the language and theme, the attention rules, the sound mapping and the collapsed state. During development one boundary check was watched failing on a deliberately wrong weekday rule before it was fixed, and a gap it did not cover (a same-day window using the wrong weekday) led to an extra check.

Not verified: the lock wizard, the ladder dialog, the disabled behaviour of page buttons, the Authenticator section and a real phone scanning the QR code; any interaction in the built window, real sound playback, the file watcher, the 20-second timer across a real boundary, screen reader names and narrow layouts. These stay open on the capability inventory.
