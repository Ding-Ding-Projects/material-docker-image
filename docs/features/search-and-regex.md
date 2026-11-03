# Search and the regular expression builder

Every search and filter field in the application, including lists, tables, logs, source viewers, pickers, menus, settings, the command palette and tab search, uses the same search field. Each field has its own regular expression builder beside it.

## Plain text first

A search field matches the text exactly as you type it, ignoring case. Characters such as `.`, `*` or `(` have no special meaning in this mode.

Regular expressions are opt-in. Turn on the `.*` toggle at the end of the field, or insert something from the builder. Each field remembers its mode, flags and sample text only while it exists. Nothing is written to disk unless you choose **Save to my patterns**.

## Opening the builder

- Press `Alt+R` while the field, its `.*` toggle or its builder button has focus.
- Select the builder button (tune icon) next to `.*`, or right-click `.*`.

The builder opens as a panel attached to the field. It opens below the field, or above it when there is no room below, and its content scrolls when the window is small (960 x 720 and up). `Tab` moves through its controls and stays inside the panel. `Esc` or **Done** closes it and returns focus to the field. Clicking elsewhere also closes it, and focus stays where you clicked.

## What the builder offers

- **Pattern**: the raw pattern, synchronized both ways with the search field. If the pattern is invalid, the builder shows the framework's message, and the panel underlines the character where the error was found.
- **Flags**: toggle chips for IgnoreCase, Multiline, Singleline, IgnorePatternWhitespace, ExplicitCapture and CultureInvariant. IgnoreCase also applies to plain-text mode.
- **Guided insertion** at the caret:
  - **Text**: plain text with automatic escaping, and a list of alternatives inserted as `(?:a|b)`.
  - **Classes**: `\d \w \s` and their negations, `.`, Unicode categories such as `\p{L}` (with a negate option), and a custom or negated `[set]` editor that escapes `] \ ^ -` for you.
  - **Anchors**: `^ $ \b \B \A \z`.
  - **Groups**: capturing, named `(?<name>...)`, non-capturing, lookahead and lookbehind (positive and negative), atomic, alternation `|`, and backreferences `\1` or `\k<name>`. Group buttons wrap the selected part of the pattern.
  - **Repeat**: `? * +`, their lazy forms, and `{n}`, `{n,}`, `{n,m}` with an optional lazy suffix.
- **Library**: ready-made patterns for an image reference, an image tag, a sha256 digest, a semantic version, IPv4 and IPv6 addresses, a port number, a container or image ID, an ISO 8601 date, an email address, a URL, a Windows path, an environment variable `NAME=value` line and a log level. Each has a worked example. **Use** replaces the pattern; **Insert** adds the pattern at the caret.
- **My patterns**: **Save to my patterns** stores the name, pattern and flags in `regex-patterns.json` in the application data folder (`%LOCALAPPDATA%\DingDingProjects\MaterialDockerImage`). The file holds at most 200 entries, and you can remove any of them. Sample and replacement text are never saved. **Recent in this field** lists valid patterns used in that field during this session. The list is kept in memory only.
- **Explanation**: a tree that describes each token of the pattern in plain English. It covers literals, escapes, classes, Unicode categories, anchors, groups, lookarounds, alternation, quantifiers, backreferences, inline options and comments. Selecting a row highlights that token in the pattern.
- **Test**: paste up to 64 KiB of sample text. The builder highlights matches, shows zero-width matches as thin markers, gives the match count, and lists capture groups in a table (match, group number, name, value, position).
- **Replace preview**: a replacement using `$1`, `${name}`, `$0` or `$&`, and `$$`. The preview shows the sample after replacement and warns about references to groups that do not exist. Nothing in the application is changed.
- **Copy pattern**, **Copy as C# string** (a verbatim `@"..."` literal), and **Export JSON** (pattern, flags, engine, dialect, sample and replacement).

## Engine and limits

Matching uses .NET regular expressions (`System.Text.RegularExpressions`). The builder states which engine runs the pattern:

- **Non-backtracking engine** (`RegexOptions.NonBacktracking`), used whenever the pattern allows it. Matching time grows linearly with the text, so a pattern such as `(a+)+$` cannot hang the application.
- **Backtracking engine**, used only for constructs the non-backtracking engine does not support: lookarounds, backreferences, atomic groups and conditionals. Each match is limited to 250 ms.

Limits:

- Patterns can be up to 4096 characters.
- Sample text can be up to 64 KiB (UTF-8); longer text is cut, and the builder says so.
- At most 2000 matches are highlighted.
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

Patterns run locally in-process; nothing is sent anywhere. The builder never reads files on its own. The only file it writes is your explicitly saved patterns, or an export you choose to save.
