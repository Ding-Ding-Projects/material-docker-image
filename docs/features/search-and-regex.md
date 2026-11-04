# Search and the regular expression builder

Every search and filter field in the application, including lists, tables, logs, source viewers, pickers, menus, settings, the command palette and tab search, uses the same search field. Each field has its own regular expression builder beside it.

## Plain text first

A search field matches the text exactly as you type it, ignoring case. Characters such as `.`, `*` or `(` have no special meaning in this mode.

Regular expressions are opt-in. Turn on the `.*` toggle at the end of the field, or insert something from the builder. Each field remembers its mode, flags and sample text only while it exists. Nothing is written to disk unless you choose **Save to my patterns** or **Remember this experiment**, or you apply a replacement in files (see below).

## Opening the builder

- Press `Alt+R` while the field, its `.*` toggle or its builder button has focus.
- Select the builder button (tune icon) next to `.*`, or right-click `.*`.

The builder opens as a panel attached to the field. It opens below the field, or above it when there is no room below, and its content scrolls when the window is small (960 x 720 and up). `Tab` moves through its controls and stays inside the panel. `Esc` or **Done** closes it and returns focus to the field. Clicking elsewhere also closes it, and focus stays where you clicked.

## What the builder offers

- **Pattern**: the raw pattern, synchronized both ways with the search field. If the pattern is invalid, the builder shows the framework's message, and the panel underlines the character where the error was found.
- **Flags**: toggle chips for IgnoreCase, Multiline, Singleline, IgnorePatternWhitespace, ExplicitCapture, CultureInvariant, RightToLeft and NonBacktracking. IgnoreCase also applies to plain-text mode. RightToLeft runs on the backtracking engine. NonBacktracking requires the linear-time engine and reports patterns that need backtracking instead of falling back.
- **Guided insertion** at the caret:
  - **Text**: plain text with automatic escaping, and a list of alternatives inserted as `(?:a|b)`.
  - **Classes**: `\d \w \s` and their negations, `.`, Unicode categories such as `\p{L}` (with a negate option), and a custom or negated `[set]` editor that escapes `] \ ^ -` for you.
  - **Anchors**: `^ $ \b \B \A \z`.
  - **Groups**: capturing, named `(?<name>...)`, non-capturing, lookahead and lookbehind (positive and negative), atomic, alternation `|`, and backreferences `\1` or `\k<name>`. Group buttons wrap the selected part of the pattern.
  - **Repeat**: `? * +`, their lazy forms, and `{n}`, `{n,}`, `{n,m}` with an optional lazy suffix.
- **Blocks**: build a pattern from plain-language lines such as `start of line`, `literal v`, `capture major: digits 1 to 3 times`, `one of cat|dog`, `optional literal s`, `end of line`. Literal text is escaped. **Use this pattern** or **Insert at caret** applies the result.
- **More**: characters by code point (`\xHH`, `\uHHHH`, surrogate pairs), `\n \r \t \f \v \e \a \0`, `\Z`, `\G`, class subtraction `[a-z-[aeiou]]`, conditionals `(?(1)a|b)`, balancing groups `(?<push-pop>...)`, inline options `(?i-s:...)`, comments, and the atomic stand-in `(?>item+)` for possessive quantifiers. Constructs .NET does not have are listed with the reason.
- **Library**: ready-made patterns for an image reference, an image tag, a sha256 digest, a semantic version, IPv4 and IPv6 addresses, a port number, a container or image ID, an ISO 8601 date, an email address, a URL, a Windows path, an environment variable `NAME=value` line and a log level. Each has a worked example. **Use** replaces the pattern; **Insert** adds the pattern at the caret.
- **My patterns**: **Save to my patterns** stores the name, pattern and flags in `regex-patterns.json` in the application data folder (`%LOCALAPPDATA%\DingDingProjects\MaterialDockerImage`). The file holds at most 200 entries, and you can remove any of them. The sample text is never saved with a pattern. The replacement and the test cases are saved with it only when **Also save the replacement and the test cases** is ticked (it is ticked by default), and loading the pattern restores them. **Import…** adds patterns from a library file (existing names are kept; patterns that do not compile are skipped and reported) and **Export…** saves all of them to a file you choose. A filter box searches names and patterns. **Recent in this field** lists valid patterns used in that field during this session. The list is kept in memory only.
- **Explanation**: a tree that describes each token of the pattern in plain English. It covers literals, escapes, classes, Unicode categories, anchors, groups, lookarounds, alternation, quantifiers, backreferences, inline options and comments. Selecting a row highlights that token in the pattern, and moving the caret in the pattern selects the deepest row at that position. **Detail** (Overview, Standard, Full) limits how many levels are shown and exported. **Copy explanation** and **Save explanation…** export the tree as indented text, Markdown or JSON at that detail (up to 2000 tokens).
- **Test**: paste up to 64 KiB of sample text. The builder highlights matches, shows zero-width matches as thin markers, gives the match count, and lists capture groups in a table (match, group number, name, value, position). **Previous match** and **Next match** (`Shift+F3` and `F3`) step through the matches with wrap-around, mark the current one in a second colour and select it in the sample box.
- **Replace preview**: a replacement using `$1`, `${name}`, `$0` or `$&`, `` $` ``, `$'`, `$+`, `$_` and `$$`. The preview shows the sample after replacement and warns about references to groups that do not exist (.NET inserts those as plain text). A palette inserts each reference at the caret, **Replacement explained** describes every piece in plain words, and **Insert characters** turns typed escapes (`\n`, `\t`, `\xHH`, `\uHHHH`, `\u{...}`) into the characters themselves, because .NET replacement text has no escapes of its own. **Insert as literal text** doubles every `$`. Nothing in the application is changed.
- **Split preview**: shows how the sample is cut at each match (up to 2000 pieces). With capture groups, the captured text is returned between the pieces, as .NET does.
- **Test cases**: lines starting with `+` must match and lines starting with `-` must not match. The builder reports how many hold. Kept in memory for the field only.
- **Diagnostics and timing**: a static check flags nested unbounded repeats, repeated or overlapping alternatives under a repeat and adjacent `.*.*`. **Run timing probe** measures wall-clock time on growing inputs with the backtracking engine (250 ms per probe, 3 s total) and warns about catastrophic backtracking. .NET exposes no step count or match trace, so none is shown.
- **Convert and copy as**: C# (verbatim string, regular string, code for finding, testing, replacing or splitting), PowerShell, a JSON string, command lines for `grep -E`, `grep -P`, ripgrep and Select-String, and code for JavaScript, Python, Java, Go and PHP. The `grep -E` conversion works construct by construct and refuses what grep cannot express (lazy quantifiers, lookarounds, atomic groups, conditionals, inline options, Unicode escapes and some flags); rewrites that change meaning are labelled. The other targets use the flavor table: a construct the target does not support stops the conversion and names it, and a construct it reads differently is listed and marks the result as not identical in meaning. The command lines use the folder and file patterns from **Search in files** when they are filled in; their quoting is for a POSIX shell (PowerShell for Select-String). ripgrep gets `-P` when only PCRE2 can read the pattern.
- **Parameters**: write `{{name}}` or `{{name:default}}` in a pattern. The pattern still compiles as written, so a saved pattern with placeholders stays valid. The Parameters tab lists the placeholders, takes a value for each and fills the pattern in a single pass: values are escaped as literal text unless **Insert values as pattern fragments** is ticked, a value that itself looks like a placeholder is never expanded, and a filled pattern that does not compile is refused. **Turn selection into parameter** writes the placeholder for you. Using a saved pattern that has placeholders tells you how many to fill.
- **History**: **Remember this experiment** stores the pattern, flags, sample (up to 16 KiB), replacement and test cases in `regex-experiments.json` in the application data folder (50 experiments, 1 MiB at most; the oldest go first). Nothing is stored until you press the button. **Load** restores a whole experiment, **Forget** removes one and **Forget all** clears the file. The sample is part of what is stored, so do not remember text you would not keep on disk.
- **Flavor comparison**: lists, for the constructs this pattern uses, which of seven engines (.NET, PCRE2, JavaScript, Python re, Go RE2, Rust regex as used by ripgrep, and `grep -E`) read them the same way, read them differently or do not support them. The table is written by hand (37 constructs) and each probe was run against the real engines while it was written; nothing is executed in another engine when you use it. It can be copied as text.
- **Search in files**: searches the files of a folder with the pattern (flags and plain-text mode included). Folder, file patterns (`*.log;*.txt;!*.min.js`, `?`, `**`; a slash means a path from the folder), subfolders and the largest file are set in the panel and remembered while the field exists. `.git` and `node_modules` are skipped, links and junctions are never followed, binary files (a NUL byte in the first 8 KiB) are skipped, and UTF-8 and UTF-16 with a byte order mark are read. Results list file, line, column and the line text; the limits are 2 MiB per file (adjustable up to 16 MiB), 5000 files, 1000 results, 200 matches per file, 128 MiB in total and 15 seconds, and each limit that stops a search is reported. A whole drive is refused.
  - **Preview replacement** is a dry run: it writes nothing and needs no backup. It lists, per file, the number of replacements and the first changes with line and column. Files that are not valid in their encoding, read-only files and files over a limit are skipped with the reason.
  - **Apply…** is available only after a preview of the current pattern, replacement, folder and file patterns, and asks for confirmation. It re-reads each file and skips any that changed since the preview (SHA-256 comparison), writes a byte-exact undo snapshot of every file it will change into `regex-undo` in the application data folder, then writes each file with its byte order mark and line endings preserved. If a write fails, the files already written are restored. A plan covers at most 500 files and 256 MiB, and the 10 newest snapshots are kept. The event is recorded in Local History (the summary, not the file contents).
  - **Undo** on a snapshot restores the original bytes of each file that still has the content the replacement wrote; a file edited afterwards is left alone and reported.
- **Convert POSIX classes** (More tab) rewrites `[[:alpha:]]` and the other POSIX classes as .NET sets; the diagnostics warn when a pattern contains one, because .NET reads it as a different set.
- **Copy pattern**, **Copy as C# string** (a verbatim `@"..."` literal), and **Export JSON** (pattern, flags, engine, dialect, sample and replacement).

The full list of RegexBuddy topics, with what is implemented, partial or unavailable on .NET, is the [parity matrix](regex-parity.md).

## Editing, templates, layout and preferences

- **Edit through the tree.** Select a row of the Explanation and set, change or remove its quantifier, wrap it in a group or lookaround, unwrap a group, or delete it. The pattern text is regenerated and checked; an edit that would make it invalid, or silently change what it means, is refused with the reason.
- **Code templates.** The Convert section's code for C#, PowerShell, JavaScript, Python, Java, Go and PHP comes from templates you can edit, one per language and kind of code. Placeholders such as `{{pattern}}` are filled with text already quoted for the language, so a template cannot change the quoting. Templates are saved in `regex-templates.json` in the application data folder.
- **Preferences and layout.** Choose which of the 16 panels are shown and their order, the default flags for new search fields, the match time limit (50 to 1000 ms), the font size of the pattern box and the default explanation detail. They are remembered in `regex-preferences.json` and loaded at start-up. The pattern and the preferences panel cannot be hidden.
- **Keyboard.** `F6` and `Shift+F6` move through the panels that are shown, `Ctrl+1` to `Ctrl+0` jump to the first ten, `Alt+Home` returns to the pattern. The list is shown in the Preferences and layout panel.
- **Filters inside the builder.** The Library, My patterns and Unicode block filters are search fields with their own builder, which opens beside the first one.

## Folder search extras, action files and the command line

- **Search in files** accepts a modified-after and modified-before time (honoured by the replacement preview too) and up to 5 context lines. Results can be saved as JSON (loadable again), text with context or CSV, and a result opens in the external editor from Settings, at its line for Visual Studio Code, Notepad++ and Sublime Text.
- **Action files.** A search (pattern, flags, replacement, folder, file patterns, subfolders, context lines) can be saved and opened as a small JSON file. Opening one fills in the builder and runs nothing.
- **Start-up arguments.** `--regex-pattern TEXT [--regex-flags imsxncrb] [--regex-replacement TEXT] [--regex-folder PATH] [--regex-globs LIST]` or `--regex-action FILE` opens the command palette with its builder filled in. The flag letters are i IgnoreCase, m Multiline, s Singleline, x IgnorePatternWhitespace, n ExplicitCapture, c CultureInvariant, r RightToLeft and b NonBacktracking; without `--regex-flags` no flag is on. **Copy command line** in the builder writes the matching arguments.
- **Engine versions.** The flavor comparison offers a version for Python and JavaScript. The newest version is the table itself; older versions change the constructs whose support arrived later.

## Engine and limits

Matching uses .NET regular expressions (`System.Text.RegularExpressions`). The builder states which engine runs the pattern:

- **Non-backtracking engine** (`RegexOptions.NonBacktracking`), used whenever the pattern allows it. Matching time grows linearly with the text, so a pattern such as `(a+)+$` cannot hang the application.
- **Backtracking engine**, used only for constructs the non-backtracking engine does not support: lookarounds, backreferences, atomic groups and conditionals. Each match is limited to 250 ms.

Limits:

- Patterns can be up to 4096 characters.
- Sample text can be up to 64 KiB (UTF-8); longer text is cut, and the builder says so.
- At most 2000 matches are highlighted and 2000 split pieces are shown.
- A test stops after 2 seconds in total.
- Replace output is cut at 256 KiB.

Tests and replace previews run off the interface thread and are cancelled when you keep typing. An invalid pattern never raises an error dialog. The field shows a clear message, filtering shows nothing until the pattern is fixed, and bulk actions refuse to run. If a backtracking pattern times out while filtering a list, the field stops matching for that query and asks you to simplify it.

## Escaping rules

To match one of `\ * + ? | { [ ( ) ^ $ . #` literally in a regular expression, put a backslash before it. With IgnorePatternWhitespace, a space must also be escaped. Inside `[ ]`, escape `\ ] [ ^ -`. **Insert escaped text** does this for you. Plain-text mode needs no escaping.

## Closing tabs by text

The tab context menu and the command palette offer **Close tabs containing text…** and **Close tabs not containing text…**. Both open the same dialog, which has its own search field and builder. The dialog shows a live preview of which tabs will close, a count, and the tabs that stay open with the reason. An empty or invalid query closes nothing. The workspace tab never closes. Pinned tabs stay open unless you turn on **Include pinned tabs**. Tabs whose page has unsaved work always stay open.

## Coverage guard

`src/MaterialDockerImage/UI/SearchSurfaceCatalogue.cs` lists every search surface by ID and owner page. A test in `tests/UiState` scans the application source and fails in three cases:

- A search field is constructed but not registered in the catalogue.
- A registered surface no longer exists in the source.
- A search-like text box bypasses the shared search field without a documented exemption.

There are two exemptions. The Docker Hub search term is a remote query sent to `docker search`; its results table has its own search and builder. The image tag entry only borrows the search field's style.

## Security

Patterns run locally in-process; nothing is sent anywhere. The builder reads files only in **Search in files**, when you press a button, and only in the folder you named. It writes your explicitly saved patterns, remembered experiments you asked for, exports you choose to save, and, when you confirm **Apply…**, the files of a previewed replacement together with their undo snapshots.
