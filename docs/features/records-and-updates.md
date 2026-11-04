# Records, exports, shortcuts and updates

How the app shows its own history, exports tables, labels shortcuts, explains controls and keeps itself current.

## Changelog

Tools > Changelog shows the running version, build time and runtime, then the release history.

- The history is generated when the app is built, from this repository's git log, by `build/make-changelog.ps1`. It is written to `src/MaterialDockerImage/Assets/changelog.json`, embedded in the app and never fetched at run time. The same history always produces the same file.
- Changes are grouped under the first `vX.Y.Z` tag that contains them, newest first. Changes after the newest tag are listed as unreleased. Merge commits are left out; every entry is an original commit subject with its commit date and its full SHA.
- The category (Added, Changed, Fixed, Removed, Documentation, Other) comes from the first word of the subject. A subject that starts with another word is Other, not guessed.
- The short SHA on each entry opens the real commit on GitHub, after you confirm the browser launch.
- Search uses the same search box as the rest of the app, so the regex builder is next to it. The query is matched against the version, category, subject, date and SHA.
- From date and To date accept `YYYY-MM-DD` or your regional date format. A date that cannot be read is reported under the boxes and your text is kept. Search and dates must both match.
- Copy as Markdown and Export Markdown write the filtered view. The file states the repository, the range, the search, how many of the total changes it holds, and keeps full SHAs as text.
- The page says that newer releases may exist: it lists the tags that existed when the build was made. A build made without the generator (a plain checkout) says it has no changelog.

## Table exports

Every table (images, containers, volumes, activity and the rest) has an export button. It exports or copies the visible rows, after search and filters, as CSV, TSV, JSON, Markdown or HTML.

| Format | Escaping |
| --- | --- |
| CSV, TSV | Cells containing the separator, a quote or a line break are quoted, quotes doubled. CRLF row ends. |
| JSON | Schema `material-docker-image/table-export`, version 1, with `columns`, `count` and `rows`. Columns that share a header are numbered (`Name`, `Name (2)`). |
| Markdown | Pipes and backslashes are escaped, line breaks become `<br>`. |
| HTML | A complete UTF-8 page. Text, headers and the title are HTML-escaped, so a cell holding markup is shown, not run. |

Cells hold the text the table shows, not the underlying object, and the confirmation says so.

## Menu shortcuts

A menu item that is also a command gets its shortcut text from the registered command gesture (`Ui.CommandMenuItem`), so the text shown is the key that works. Keys that belong to one list only (Delete, Enter, Alt+Up, Alt+Down, Ctrl+A, Ctrl+C) are typed next to their handlers. A test scans the source and fails if a menu hand-types any other shortcut.

## Info icons

The info icon (`Ui.Info`) has the accessible name "About" followed by the control name, the explanation as its tooltip and a popover with a worked example where one helps. Every setting that is saved shows one: hand-written guidance when the row has it, otherwise the explanation plus the built-in default and how to undo the change. Field labels that lacked guidance were given some.

## Updates

- Only an installed copy updates itself. It is recognised by an `Update.exe` beside the `app-x.y.z` folder; a portable or development run skips the check and Settings says why.
- About eight seconds after start (and when you press Check now in Settings > Data and privacy) the app reads the `RELEASES` file of the latest GitHub release, parses it strictly, and compares the newest full package for this app with the running version. A newer version is downloaded with `Update.exe --update` and staged. A failed or slow check leaves the current version running and says so.
- A banner above the workspace then names the new version and says the build is unsigned, so Windows may warn about an unknown publisher. Restart to update asks for confirmation first. Later hides the banner for the session. The app is never restarted without that confirmation.
- Settings > Data and privacy > Check for updates turns the background check off. Check now always works.
- Not yet done: a feed signature check, download progress, deferral choices, and any run against a real published feed or installed copy.
