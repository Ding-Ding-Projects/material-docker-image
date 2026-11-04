# Settings safety: locks, schedules, attention modes, sounds and collapsible filters

This guide covers five related controls in Settings and the shell. Every rule below is a framework-free class in `src/MaterialDockerImage/UI/SettingsSafety.*.cs` with focused checks in `tests/UiState/Program.cs`. The window code only reads their decisions. None of this has been driven in the built window yet; see the verification section.

## Element locks

Every settings row that holds a value has a lock button beside its title, and every row and every interactive element on a page has **Lock this control...** on its right-click menu: buttons, switches, check boxes, options, drop-downs, text fields and sliders. Locking an element shares one helper (`Shell/LockUi.cs`) between settings rows, page buttons and every other element. Major page action buttons (the filled and danger buttons) keep their ids `btn.<page>.<label>` in lower case, at most 40 characters, so two buttons with the same label on the same page share a lock.

### Locks on any element

`LockUi.AttachPage` walks each page when it is shown and attaches the lock menu to every supported element that does not already have one. The id comes from `ElementIdentity.Derive` (`UI/SettingsSafety.LockManager.cs`): `el.<page>.<kind-name>-<hash>`, at most 36 characters and always a valid lock id.

- An element with an `AutomationProperties.AutomationId` keeps the same lock when it moves or is renamed. No page sets one today, so in practice the next rule applies.
- Otherwise the id is built from the page, the element kind, the English name (`Loc.Original` turns a translated accessible name back into English, so switching language does not change the id) and the position among identical elements on that page, so two identical `OK` buttons get two locks.
- A name that is a sentence with numbers in it is not reversed to English, and an element whose accessible name changes for another reason gets a new id. Elements built after the page is shown are picked up the next time it is shown. Settings rows keep their own lock and are skipped.
- Not built: a context-menu lock on tabs, tab groups and on individual appearance values, and opening the unlock prompt by clicking a disabled element (it is reached from the right-click menu).

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
| Password | 8 to 128 characters, no control characters. | A PBKDF2-SHA256 hash with the same parameters as the PIN. The password is never stored, logged or exported. |
| Authenticator app | A 6-digit RFC 6238 code (SHA-1, 30 seconds, one step of clock skew either side). A code that was accepted cannot be used again. | The 20-byte secret, generated with `RandomNumberGenerator`, protected with Windows DPAPI for the current user (entropy: the install salt). |
| PIN plus password, password plus authenticator, PIN plus authenticator, password plus PIN plus authenticator | Every factor of the policy, always asked in the order PIN, password, authenticator code. | Each factor as above, in the same lock record. |

A combined policy is one bounded attempt (`LockStore.Begin`, `UnlockAttempt`). The dialog shows **Step n of m**. A factor that is right is remembered only inside that attempt, so closing the prompt starts again at the first factor. A wrong answer at any step counts once against the lock's single attempt budget and sends the attempt back to the first factor; the authenticator step is spent only when every factor is right, so a code cannot be replayed. One value cannot answer a combined policy. A factor that cannot be checked (the authenticator secret cannot be read) is reported as unavailable and never skipped.

The stored policy numbers are `None 0, PIN 1, Authenticator 2, Password 3, PIN plus password 4, Password plus authenticator 5, PIN plus authenticator 6, Password plus PIN plus authenticator 7`. Existing numbers never change, a lock file written before durations loads, and an unknown number is dropped.

Credentials live in `locks.json` in the data folder, apart from `settings.json`, exports and history. An authenticator lock needs DPAPI; where it is unavailable the wizard says so and refuses the policy. In capture mode the store is held in memory only.

Pairing an authenticator shows the secret only after **Show secret and QR code**: a QR code of the `otpauth://totp/` link, the grouped Base32 secret, copy buttons and the algorithm, digits and period. The lock arms only after one current code is typed back.

### Unlock durations

The wizard asks how long an unlock lasts (`UnlockDuration`, stored with the lock):

| Choice | The lock closes again |
| --- | --- |
| This action only | After the protected action: a button click, a drop-down or slider change, a text field that lost focus with new text, or, for a settings row, the first save that changes its values. |
| 5 minutes, 1 hour | When the time is up. The expiry is saved in `locks.json`, checked every 15 seconds while the app runs, and honoured across a restart. |
| Until the app closes | At the next launch, always. This is the default for a new lock. |

Locks written before durations existed read as "until I lock it again" and are never closed by themselves. Locking again by hand ends the grant. `LockLifecycle` (`UI/SettingsSafety.LockManager.cs`) holds these rules; the launch pass and the timer call `LockLifecycle.Reconcile`. The record keeps only a flag and a time, never a credential.

### Locks manager

Settings has a **Locks** section: one table of every lock the app knows, whatever it guards, with the lock id, what it is, its policy, how long an unlock lasts and whether it is locked or unlocked until a time. It has the shared search with its regex builder, the filters **Locked only** and **Has a credential**, export (the shared table export) and row actions **Unlock this control...**, **Lock again**, **Change lock policy...**, **Remove lock** and **Support Tickets...**. **Remove selected locks** removes many at once. A lock that is locked is never removed: it has to be unlocked first, and a bulk removal reports the locked ones it skipped. Removal deletes the lock, its credentials, its recovery codes and its label. The command palette has **Locks manager**. Labels for locks that are not settings rows are kept in `settings.json` (`LockLabels`, descriptions only, never a credential). An export contains only descriptions, never a hash or a secret.

### Unlock ladder

Unlocking climbs these rungs (`LockStore.Stage`, `LockStore.Attempt`, `LockStore.UseRecoveryCode`):

1. **Confirm.** A lock with no policy only asks for a confirmation.
2. **Credential.** The PIN, password or authenticator code, one step at a time for a combined policy. The first two wrong answers cost nothing and the dialog says how many free tries remain.
3. **Wait.** From the third consecutive wrong answer the wait is 30 seconds, then 60, 120 and so on, doubling to a cap of one hour. The dialog shows a live countdown and the clock time the wait ends. The failure count and the wait are saved, so a restart does not clear them. While waiting, an answer is refused without being checked and without counting. Waiting alone never unlocks anything: the credential is still needed afterwards.
4. **Recovery code.** Eight single-use codes (`XXXXX-XXXXX`) are made when a PIN or authenticator policy is saved, shown once with Copy all and Save to file, and stored only as hashes. A code works at any time, including during a wait, clears the failure count and the wait, and is spent. Typing ignores case, spaces, hyphens and the look-alike letters I, L and O.

Beside every prompt the dialog names the data folder and says that deleting `locks.json` (or the whole folder) clears every lock. That is the last resort when the PIN, the authenticator and every recovery code are gone.

### Games that shorten a wait

While a wait is running, the Wait rung offers **Play to shorten the wait**. The games run in this order, and a win ends the wait early. (`UI/UnlockLadderGames.cs` makes and grades every question; `Shell/LadderGameDialogs.cs` only shows them and collects answers.)

| Rung | What it asks | Clears the wait | Otherwise |
| --- | --- | --- | --- |
| Dish | One dim sum dish, four choices. With a picture from the public catalogue it asks which dish it is; with no picture (offline, or the picture could not be fetched) it shows the Cantonese name and asks for the English one. The picture alt text never names the dish. | The right choice | Up to five wrong choices, then the sums. Wrong choices are counted per lockout, so closing the dialog does not reset them |
| Sums | Ten sums (a + b, a - b with a non-negative answer, a x b with single digits). Every one must be right | Ten right | One wrong, missing or wrong-kind answer drops to whack-a-mole |
| Whack-a-mole | A 3 by 3 board, 14 moles one at a time for one second each, 20 second round. Click a mole or press keys 1 to 9. At least 8 hits | 8 or more counted hits | A lost round ends the ladder |
| Clock | The plain wait | | The ladder is not offered again for that lockout |

What keeps it from becoming a second, weaker password:

- A win clears the wait only. It signs nobody in, mints no session and sets no cookie; the prompt returns to the credential rung and the PIN or code is still required. `LockStore.ClearWaitByLadder` changes nothing except the running wait.
- It never refunds the attempt budget and never slows the escalation: the failure count is untouched, so the next wrong answer still earns the longer wait.
- A rolling budget of **three wins per hour** (`LadderLedger`, kept in `ladder.json` beside `locks.json`) caps it. After that the Wait rung says the clock is the way through until an hour has passed. Deleting `ladder.json` only refills the budget; it does not touch any lock.
- Every question carries a random single-use nonce that is taken out of the table before anything is graded, so a wrong answer cannot be retried against the same question, a right one cannot be replayed, an answer for another lock or of the wrong kind is refused, and challenges expire (5 minutes for a dish, 10 for sums, the round plus a minute for moles). The correct answers are not on the challenge objects the views receive. This app grades in the same process as the views, so this is separation of duties inside one program, not a security boundary, and the disclosure above still applies.
- A mole round answered before its 20 seconds could have elapsed is refused. A hit counts only for a mole that was in that cell at that moment, inside the round, and each mole counts once.
- Under School mode the ladder starts at the sums: the dish rung is absent, and no copy names it.

The dialogs use the real keyboard path (number keys for choices and cells, Enter on the last sum), a numeric seconds-left readout beside the round (never colour-only), a live region for the score, no animation, and large cells.

Not built: the access-control style keypad for PIN entry; locks on tabs, groups and individual appearance values; locked items labelled in the command palette and tab searches. Grading runs in the same process as the views. The three games have been checked as rules only; none has been driven in the built window.

## School mode

School mode keeps one shared record, `school-mode.json`, in `DingDingProjects\shared` under local application data, so every app on this computer shares it. It stores whether the mode is on, the name the user gave it and, optionally, a PIN (4 to 12 digits) or password (6 to 64 characters) as a salted PBKDF2-SHA256 hash. The secret is never stored or logged in plain text.

- Turning it on needs nothing. Turning it off asks for the PIN or password when one is set; a wrong answer is counted, and from the third in a row it starts a persisted wait that doubles up to an hour (the same limiter as the lock ladder).
- The mode can be renamed. This app then uses the chosen name on its mode controls, its settings row and its palette entry in place of the shipped one. Other copy that mentions the mode by name is rewritten to the chosen name when the mode has been renamed, in English only.
- A running app checks the record every two seconds and switches live when another app changes it. An older build's per-app setting is adopted into the record the first time.
- Deleting `school-mode.json` resets the mode, its name and its PIN. The settings row says so; the mode is a lock you set for yourself, not security.
- The ladder starts at the sums and the dim sum surprise never appears while it is on. English is forced (`Loc.SchoolMode`).

**Passkey choice.** Under Data and privacy the mode can be turned off with Windows Hello user verification instead of the PIN or password. The record stores only the choice (`Method`: `secret` or `passkey`), never a passkey, template or secret; Windows shows its own verification dialog (`UserConsentVerifier`, which is why the app targets `net10.0-windows10.0.19041.0`). Choosing the passkey is refused with the plain reason when Windows Hello has no device, is not set up, is disabled by policy or is busy. At turn-off time an unavailable Windows Hello is reported and falls back to the PIN or password, and to a free switch-off only when no PIN or password is set either. While the mode is on, changing the method needs the same proof as turning it off. An unknown stored method reads as the PIN or password. The pure rules are in `UI/SchoolPasskey.cs` and are checked in `tests/UiState/PasskeyQrChecks.cs`; the Windows Hello calls (`Services/WindowsHello.cs`) have not been run, because they show a Windows prompt.

**Hidden under the mode.** The Language setting, both funny-level sliders, the personal wording row and its preview, the Language command in the palette, the Language field of the setup wizard and the personal wording file drop are absent (not disabled, and absent from settings search) while the mode is on, and come back when it is turned off. One list, `SchoolHiddenControls`, decides this. The scheduled-settings editor can still offer a language rule, and the language row of the Locks manager is unchanged; neither takes effect while the mode forces English.

Not built: proving the live switch between two real running apps, and driving the passkey choice and the hidden controls in the built window.

## Dim sum surprise

At startup there is a 10 percent chance, from one fresh random draw per launch, of a small toast with a dim sum dish: its name in the active language mode and, when it can be fetched, its picture. It cannot be switched off. It stays polite by staying out of the way: it never appears during first-run setup, with a dialog open, while an update is ready, while an operation runs, in quiet hours, under School mode or in a capture run, and never twice in a launch. It does not take focus, announces itself through a polite live region, fades in only when motion is on, and removes itself after nine seconds (longer while hovered or focused).

Names and pictures come only from the public dim sum catalogue at `Ding-Ding-Projects/dim-sum-photos`:

- The catalogue index is read from its raw URL and trimmed into a small cache (`dimsum\catalog-cache.json` in the data folder) that records the source URL and revision (ETag); it is refreshed after seven days with a conditional request.
- A picture is downloaded once from the published `catalog-v1*` release assets (tried in order), must start with the PNG signature and be under 12 MB, and is kept in a cache of at most 24 pictures. No photo is stored in this project, and no photo is attached to its releases.
- If the catalogue or picture cannot be had, the toast shows a dish name from a short built-in list of names with the line "No picture is available right now." and the dish rung asks a name question instead. Nothing is drawn or generated locally.
- The only requests are two public reads of that catalogue. No personal data is sent and nothing is tracked.

The rules, parsing and address building are checked in code (`DimSumRules`); the live fetch has not been run inside the app.

Disclosure shown every time: a lock is a speed bump you set for yourself, not security. It does not protect anything from another person who has this computer. Anyone who can delete the data folder can remove every lock.

## Support Tickets

The recovery route for a locked-out person is dressed as a support desk. It is reached from the unlock prompt (**Forgotten your password?**), from the footer of the lock wizard, the ladder and the Locks manager (**Support Tickets...**), from the Tools page (**Support Tickets**) and from the command palette. The code is `Shell/SupportDeskUi.cs` and `UI/SettingsSafety.SupportTickets.cs`.

- **Filing.** A category (forgot my PIN, forgot my password, lost my authenticator, lost my recovery codes, something else), a severity and a description of up to 1,000 characters. The ticket number is generated locally (`TKT-yyyyMMdd-0001`, counting up within a day). A blank description is refused. The list holds at most 200 tickets.
- **Severity.** It is recorded and then treated exactly like every other severity, and the screen says so.
- **Status.** New, acknowledged, investigating, resolved. It advances on request (**Advance status**), by itself every 20 seconds while the screen is open, or straight to resolved (**Resolve**, which still records every reply). Each step adds a canned reply. The factual sentence is always shown; a playful lead-in is added by the funny level.
- **Resolution.** The resolution opens the application-data folder in the platform's file manager (`PageParts.OpenFolder`, which reports a failure to launch) and shows the exact path with a copy button. The path shown and the folder opened are the same value. The app never deletes anything: `SupportDesk.Resolve` returns only the folder to open, and a check asserts that neither source file contains a delete call.
- **The disclosure.** One plain line, outside the comedy and identical at every funny level and in every language mode, states: nothing is sent anywhere, a ticket exists only on this computer, no network request is made, no data is collected, and nobody is reading it. A check pins its exact wording and asserts that the source files contain no network call.
- **Storage.** Tickets live in `tickets.json` in the data folder, so deleting the data folder clears them with the locks they point at. The list uses the shared table: search with its regex builder, export, row actions and bulk Advance, Resolve and Remove.

No real person, company, case system or response time is named or implied.

## Authenticator tool

Tools has an **Authenticator** section with two parts, chosen at the top: **My accounts** and **App locks**.

### My accounts (general authenticator)

Keeps the time-based one-time codes for any account you register.

- **Adding**: paste an `otpauth://totp/` link (issuer, algorithm SHA1, SHA256 or SHA512, 6 to 8 digits and the period are taken from the link, never replaced by defaults) or enter the issuer, account, Base32 secret, algorithm, digits and period by hand. Spaces, hyphens and lower case in a secret are tolerated; counter-based (HOTP) links, unsupported algorithms, repeated parameters and links over 2,048 characters are refused with a plain reason.
- **Codes**: each account shows its current code grouped for reading (`123 456`), the seconds left as a number and a bar (never colour only; the number turns to the warning colour in the last five seconds) and the next code. **Copy** puts the current code on the clipboard.
- **Managing**: search (with its own regular expression builder), rename, move up or down and delete with confirmation. At most 200 accounts; an exact duplicate is refused.
- **Storage**: one file, `authenticator.bin`, protected for the current Windows account with DPAPI. The whole document is encrypted, so issuers and names are protected as well as secrets. A file this account cannot open, or that is damaged, is reported and never overwritten. Nothing is written to `settings.json`, local history, notifications, logs or exports.
- **Backup**: **Export encrypted backup…** writes AES-256-GCM with a key derived from a passphrase (at least 8 characters) by PBKDF2-SHA256 (600,000 iterations), a random salt and nonce. **Import encrypted backup…** validates everything before adding anything, skips accounts already present and refuses wrong passphrases, changed files, unsafe key settings and oversize files.
- **Capture mode** shows three labelled sample accounts with example data and disables adding and backups, so a HuiShot never shows a real secret or code.

Standards: codes come from RFC 4226 and RFC 6238 and are checked against all 18 published RFC 6238 vectors through the account model.

Not built: registering from a QR image file, the clipboard or a camera, grouping, a warning when the system clock is far off (this needs a trusted time source), a secrets export behind super confirmation, and any driving of the section in the built window.

### App locks

Tools has an **Authenticator** section for the app's own locks. It pairs an authenticator app with a lock, checks a code for a lock without using it up or counting it as an unlock attempt, lists the locks that have a policy (with their recovery codes left and **Remove policy**) and runs **Run RFC 6238 self-test**, which computes the 18 published RFC 6238 vectors (SHA-1, SHA-256, SHA-512, 8 digits) with the build's own code. The command palette has **Authenticator for locks**. A lock registered in an authenticator on the same computer is mostly for fun, since the key sits beside the lock it opens.

The QR encoder (`UI/SettingsSafety.Qr.cs`) is dependency free and draws nothing itself: byte mode, level L, versions 1 to 10 (271 bytes). Text beyond that is refused rather than truncated, and an otpauth link for a lock fits easily.

## Scheduled settings

Settings, Schedules. A rule lends one setting for a time window and, optionally, a date range. Seven settings can be scheduled; each is decided on its own.

| Setting | Allowed values | What it changes while lent |
| --- | --- | --- |
| Language | English, Cantonese, Bilingual | The interface language |
| Theme | Dark, Light, System | The colour scheme |
| Density | Compact, Comfortable, Spacious | Control height and padding |
| Accent colour | `#RRGGBB` | The seed colour that all colour roles derive from |
| Font size | Whole number 12 to 22 | The base font size |
| Motion | Off, Reduced, Standard, Expressive | The motion level |
| Display name | 1 to 80 characters, no control characters | The title bar and window title |

| Field | Meaning |
| --- | --- |
| Source | Local (the rule's own value), File, HTTPS (the value for the rule's setting comes from that source) or Home Assistant (on and off map to two values) |
| Days | Every day, or chosen weekdays |
| From, until | Local clock times, `HH:mm`. The end is not included. |
| First date, last date | Optional local calendar dates, `yyyy-MM-dd`, both days included. Empty means no bound on that side. |
| Priority | -5 to 5 in the editor (stored range -100 to 100) |

Semantics, all covered by checks:

- Times are in this computer's time zone and follow daylight saving changes.
- A window is from the start up to, but not including, the end.
- Equal start and end mean the whole day (still limited to the selected weekdays and dates).
- A start later than the end crosses midnight. The part before midnight uses that day, the part after midnight belongs to the window that started the day before. The weekday and the date range are both judged on the day the window started, so a window that starts on the last date finishes after midnight.
- Weekdays and dates must both match. An unreadable date makes the rule inactive and is refused by the editor.
- A rule with an unparseable time, no weekday selected or a disabled switch is never active.
- Higher priority wins; equal priorities: the later rule in the list wins. Each setting is decided independently of the others, so a priority on one setting never blocks another.
- At most 50 rules; duplicate or missing identifiers are repaired on load.

A schedule only lends its value. `UserSettings.ApplyScheduled` keeps the person's own choice as the base for every lent setting and `ToJson` always writes the bases, so the saved file never contains a borrowed value (a font size of 14.5 comes back as 14.5). When the window ends the base is put back. If the person changes a lent setting by hand, that choice becomes the new base instead of being overwritten.

The runtime (`Services/ScheduleRuntime.cs`) evaluates every 20 seconds, when rules change, when the source file changes and whenever a network source answers, and shows one non-blocking notice when the applied values change.

### Value sources

Rules whose source is File, HTTPS or Home Assistant take their value from outside. A rule whose source cannot supply a value stays inactive, and your own setting stays in effect.

**Local file.** An optional JSON file chosen in Settings, Schedules, watched with a debounce and only ever read from disk:

```json
{ "version": 1, "language": "Cantonese", "theme": "Light", "density": "Compact", "accent": "#AA3366", "fontSize": 16, "motion": "Reduced", "displayName": "Night desk" }
```

Every field except `version` is optional. No other field is accepted, a field may appear only once, the nesting depth is limited, the version must be 1 and the file must be under 4 KiB. Anything wrong refuses the whole file so nothing is applied from a half-valid document.

**HTTPS.** One address, entered by the person, that returns the same document (up to 64 KiB).

- Only `https://` addresses are accepted. A user name or password in the address, a fragment, whitespace, and query parameter names that look like secrets (token, key, password, auth, signature and similar) are refused, so nothing secret is ever saved in plain text in `settings.json`.
- The app sends only a `GET`, with no cookies and no credentials, waits 10 seconds at most, reads 64 KiB at most and does not follow a redirect to plain http.
- Checking is automatic no more often than every 5 minutes (choices from 5 minutes to 24 hours), and only while an enabled rule uses the source. A "Check now" button asks at once.
- If the server cannot be reached, times out, answers an error, answers something too large or answers a document that fails validation, the last good answer stays in use, **no setting changes**, and the state line says so, for example "Offline: The server could not be reached. The last good answer from 25 minutes ago stays in use and nothing was changed." With no earlier good answer, nothing from the source is applied. Errors are fixed wording; the address and server text are never copied into messages or logs.

**Home Assistant.** A base address, an entity id such as `input_boolean.night_mode`, and a long-lived access token.

- The token is encrypted with Windows data protection (DPAPI, current user) and stored in its own file `ha-token.bin` in the data folder. It is never written to `settings.json`, exported, logged or shown again; a check proves `UserSettings` has no property that could hold a secret. Without a token the state line says "No token is stored" and nothing is requested. A token that cannot be decrypted (for example a copied file) is reported as unreadable.
- Plain `http://` is accepted only for loopback, private-network and link-local addresses and for local names (`localhost`, a name without a dot, `.local`, `.lan`, `.home.arpa`). Every other host must use `https://`, so the token is never sent in clear text over the internet.
- The app sends only `GET /api/states/<entity>` with the bearer token, waits 10 seconds at most and reads 64 KiB at most. The answer must be for the same entity.
- The state `on` applies the rule's value; `off` applies the optional value for off (nothing if it is empty). Every other state (`unavailable`, `unknown`, a number) applies nothing.
- Checking runs every 1 minute to 24 hours as chosen (default 5 minutes) while an enabled rule uses the source. Failure behaves exactly like the HTTPS source: the last good state stays and nothing changes.

Every rule add, change, enable, disable and delete, every change to a source and every token store or removal (the token itself is never recorded) is saved with a local history record.

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

One playback path, `Services/ActionSounds.cs`. A cue plays only when the Action sounds switch is on, Low stimulation is off, the window is not producing captures, the volume is above zero and it is not inside the quiet hours (`SoundCueRules.MayPlay`). Playback runs on a background task and any playback problem is swallowed, so a missing audio device never changes the result of an action.

| Cue | Triggered by | Windows sound |
| --- | --- | --- |
| Success | An operation or build finished successfully | Asterisk |
| Failure | An operation or build failed | Hand |
| Warning | A warning notification, or a canceled operation | Exclamation |
| Removal | A remove, prune, kill or delete operation finished successfully | Question |
| Copy | Text was copied to the clipboard (after the clipboard accepted it) | Beep |
| Drop | A dropped wording file was applied | tada.wav, or Asterisk if missing |
| DropRefused | A dropped file was refused | Beep |

The mapping is `SoundCueRules.Mapping`; a check requires every cue to have a row. The same table appears in Settings, Sounds and narration. No audio file ships with the app.

### Volume

Settings, Sounds and narration, Action sound volume: 0 to 100 percent, saved as `SoundVolume` (default 100) and applied to the very next sound, with a "Play a test sound" button.

- At 100 percent the Windows system sound plays at its own level, exactly as before.
- Below 100 percent the app finds the WAV file Windows plays for the cue (from the person's own sound scheme in the registry, falling back to the stock file in the Windows media folder), scales its sample amplitude in memory (`WavScaler`, 8, 16 and 24 bit PCM) and plays that copy through `SoundPlayer`. `SoundPlayer` and the system sounds have no volume of their own, which is why a scaled copy is used. One scaled copy per cue is cached for the current volume and dropped when the volume changes. Nothing is written to disk and the Windows volume is never touched.
- At 0 percent nothing plays. If the cue's file cannot be found, is not plain PCM or cannot be read (for example the sound scheme has no sound for it), the cue stays silent rather than playing at full volume.

### Quiet hours

Settings, Sounds and narration, Quiet hours: a switch and a daily window (`QuietHoursStart`, `QuietHoursEnd`, default 22:00 to 07:00, off by default). Inside the window no cue plays even when sounds are on.

- Local clock; the start is included and the end is not. A start later than the end crosses midnight. Equal times mean no window. Unreadable times and a disabled switch never silence anything (`QuietHours.IsQuiet`).
- Quiet hours silence cues only. Notifications, narration and every visible result are unchanged.
- The settings page, the notification path (`Notifier.SoundsEnabled`) and `ActionSounds.Emit` all use the same quiet-hours and volume decision, so no caller can bypass it.

## Collapsible filters

`CollapseFilterState` stores only collapsed surfaces in `UserSettings.CollapsedFilters` (at most 64 keys of at most 60 characters).

- Every `DataTable` with filter chips has a chevron beside its search box (key `table.<name>.filters`). Collapsed, it shows "Filters · N on". The search box stays visible.
- Storage statistics (`storage.stats`) and the Activity action filters (`activity.actions`) use `CollapsibleBar`.
- The chevron has an accessible name that says whether it will show or hide, and is reachable by keyboard.

## Languages

All new copy has Hong Kong Cantonese wording in `Services/LocStrings.Settings.cs` and, for the extended schedules, network sources, volume and quiet hours, `Services/LocStrings.Schedules.cs`. A check requires Cantonese wording for a sample of the new strings and the English-then-Cantonese form in bilingual mode.

## Verification

Run `build.bat -Tests`. The UI-state project covers locks (including `tests/UiState/LockPolicyChecks.cs`: the password and every combined policy with ordered steps, a shared attempt budget, replay and skew on a combined lock, hashes in place of plain values, unlock durations with a fake clock across a restart, stable element ids, the Locks manager list, search, removal and export, the ticket desk with its status, persistence, bounds, export, exact disclosure and the absence of network and delete code, and Cantonese wording for every new string; and `tests/UiState/LockChecks.cs`: Base32 and the RFC 4648 vectors, the RFC 4226 and RFC 6238 vectors, skew and replay, PBKDF2 hashing, recovery codes, the backoff table, the ladder stages with a fake clock across a restart, the credential file never holding a PIN, code or secret, real DPAPI, and the QR encoder checked against a published Reed-Solomon vector and against matrices an independent scanner decoded), schedule boundaries (start, last minute, end, cross-midnight, weekday ownership, whole day, priority, file values), the base-versus-live behaviour of the language and theme, the attention rules, the sound mapping and the collapsed state. During development one boundary check was watched failing on a deliberately wrong weekday rule before it was fixed, and a gap it did not cover (a same-day window using the wrong weekday) led to an extra check.

Not verified: the lock wizard with its new policy and duration controls, the ladder dialog with its steps, the disabled behaviour of every newly lockable element, the timed relock in a running window, the Locks section, the Support Tickets screen (including the folder that opens), the Authenticator section and a real phone scanning the QR code; any interaction in the built window, real sound playback, the file watcher, the 20-second timer across a real boundary, screen reader names and narrow layouts. These stay open on the capability inventory.
Run `build.bat -Tests`. The UI-state project covers locks, schedule boundaries (start, last minute, end, cross-midnight, weekday ownership, whole day, priority, file values), date-range boundaries (both end dates, the day before and after, cross-midnight windows that start on the last date, weekday plus date, leap days and malformed dates), all seven settings and their value validation, source payload validation with many malformed documents (unknown, duplicate, nested, oversized, wrongly typed and half-valid), https and Home Assistant address rules, entity and token rules, Home Assistant state mapping, poll cadence, last-good behaviour on failure (including a check that a failed poll changes no setting), the base-versus-live behaviour of every lent setting, attention-mode decisions, quiet-hours boundaries (including across midnight), the volume gate and `WavScaler` on synthetic and real WAV bytes. Cantonese wording and its bilingual form are checked for samples of the new strings.

Not verified: any interaction in the built window, real sound playback at reduced volume on an audio device, a real https or Home Assistant server (no check makes a network request), DPAPI storage of the token on a real profile, the file watcher, the 20-second timer across a real boundary, screen reader names and narrow layouts. These stay open on the capability inventory.
