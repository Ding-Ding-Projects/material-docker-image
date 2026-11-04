# Regex builder: RegexBuddy parity matrix

The regular expression builder is held to the functional baseline of the official [RegexBuddy manual](https://www.regexbuddy.com/manual.html) (family list reviewed on 2026-11-04 against the maintainers' parity checklist). This is an independent implementation: no RegexBuddy code, text or assets are used.

**Status is stated per topic, never as a family tick.** The parity requirement stays **open**: 50 of 83 topics are implemented, 18 are partial and 15 are unavailable. Most of the unavailable ones cannot exist on the .NET regular expression engine (debugger trace, step counts, possessive quantifiers, recursion, class intersection, branch reset, replacement conditionals); the rest are not built (editable tree, line and page scopes, hex view, comparing several patterns, editable code templates, panel layout and preferences).

- **implemented**: built, covered by a focused check in `tests/UiState/Program.cs`, and applicable to the .NET engine.
- **partial**: some of the topic works; the reason and the missing part are stated.
- **unavailable**: not built, or impossible on this engine; the exact reason is stated.

**Not verified:** none of the interface below has been exercised in the running application or captured as a HuiShot. Every "implemented" row is backed by logic checks on the framework-free engine classes only (`tests/UiState/Program.cs` and `tests/UiState/RegexRemainingChecks.cs`). The checks do not prove layout, focus order, keyboard use inside the popover, dialogs, the folder browser, the new tabs and sections, or pixels. The folder search and its undo were exercised on temporary folders by the checks, not on a real project.

## Engine binding

- Engine: `System.Text.RegularExpressions` on .NET 10, regular expression dialect ".NET". The builder never silently substitutes another engine.
- The non-backtracking engine is used whenever the pattern allows it. Patterns that need backtracking (lookarounds, backreferences, atomic groups, conditionals, balancing groups, `RightToLeft`) run on the backtracking engine with a 250 ms limit per match.
- Flags offered (8): IgnoreCase, Multiline, Singleline, IgnorePatternWhitespace, ExplicitCapture, CultureInvariant, RightToLeft, NonBacktracking. RightToLeft uses the backtracking engine. NonBacktracking means "require the linear-time engine": a pattern that needs backtracking is reported as unsupported instead of falling back, and combining it with RightToLeft is refused.
- Limits: 4096 pattern characters, 64 KiB sample, 2000 highlighted matches, 2 s per test, 256 KiB replace output, 2000 split pieces, 200 test cases, 200 saved patterns, 50 remembered experiments (1 MiB, 16 KiB sample each). Folder search: 2 MiB per file by default (16 MiB ceiling), 5000 files (20000 ceiling), 1000 results (5000 ceiling), 200 matches per file, 128 MiB read in total, 15 s; replacement plans cover at most 500 files and 256 MiB, and 10 undo snapshots are kept.

## Matrix

Source files: `RegexBuilderEngine.cs` (flags, compile, test, replace), `RegexBuilderExplain.cs` (explanation tree), `RegexBuilderExport.cs` (tree export, token under the caret), `RegexBuilderLibrary.cs` (snippets, tokens, composers, saved patterns), `RegexBuilderConstructs.cs` (rare constructs and plain-language blocks), `RegexBuilderOperations.cs` (split, test cases, diagnostics, timing), `RegexBuilderReplacement.cs` (replacement parsing and explanation), `RegexBuilderNavigation.cs` (match navigation), `RegexBuilderParameters.cs` (parameters and POSIX class translation), `RegexBuilderFlavors.cs` (flavor table, scanner, comparison), `RegexBuilderGrep.cs` (folder search, dry run, apply, undo), `RegexBuilderHistory.cs` (remembered experiments), `RegexBuilderConvert.cs` and `RegexBuilderConvertTargets.cs` (conversions), `RegexBuilderLibraryIO.cs` (import and search), `SearchBuilderPopover.cs` and `SearchBuilderPopover.Tools.cs` (view).

### 1. Construction and syntax

| ID | Topic | Status | How, or why not |
| --- | --- | --- | --- |
| syntax.raw | Raw pattern editing, synchronized with the search field | implemented | Pattern box in the popover, two-way with the field. |
| syntax.literal | Literal text with automatic escaping | implemented | Text tab, `RegexBuilderLibrary.Literal`. |
| syntax.chars | Escaped, nonprinting, 8-bit and Unicode characters | implemented | `\n \r \t \f \v \e \a \0` tokens; More tab inserts `\xHH`, `\uHHHH` or a surrogate pair from a code point or character; `\cA` composer exists in code (no button). |
| syntax.shorthand | Shorthand classes `\d \w \s` and negations | implemented | Classes tab. |
| syntax.posix | POSIX bracket classes | partial | Not native: .NET reads `[[:alpha:]]` as a set of ordinary characters plus a stray bracket (the diagnostics say so). The More tab has **Convert POSIX classes**, which rewrites all 13 classes into .NET sets (letters, digits and case as Unicode; punctuation, graph, print and control as ASCII). Negated `[:^name:]` is refused with the reason. |
| syntax.custom-class | Custom and negated character sets | implemented | Classes tab; members and ranges escaped; reverse ranges refused. |
| syntax.subtraction | Class subtraction `[a-z-[aeiou]]` | implemented | More tab and `RegexBuilderConstructs.Subtraction`. |
| syntax.intersection | Class intersection | unavailable | .NET offers only subtraction. |
| syntax.unicode-categories | Unicode general categories `\p{..}` / `\P{..}` | implemented | Classes tab, all 37 category codes. |
| syntax.unicode-blocks-scripts | Unicode blocks and scripts | partial | `\p{IsGreek}` is a palette entry and is explained; other `\p{Is…}` blocks can be typed in the pattern but have no picker. |
| syntax.quantifiers | `? * + {n} {n,} {n,m}` greedy and lazy | implemented | Repeat tab, palette, `Quantifier` composer, blocks. |
| syntax.possessive | Possessive quantifiers | unavailable | .NET has none. The More tab inserts the equivalent atomic group `(?>item+)` and says so. |
| syntax.anchors | Anchors and boundaries `^ $ \A \z \Z \G \b \B` | implemented | Anchors tab and palette. |
| syntax.alternation | Alternation | implemented | Groups tab; escaped alternatives list. |
| syntax.groups | Capturing, named (`(?<n>)`, `(?'n')`), non-capturing and atomic groups | implemented | Groups tab and palette. |
| syntax.backrefs | Numbered and named backreferences | implemented | Groups tab. |
| syntax.recursion | Recursion and subroutine calls | unavailable | .NET does not support them. Balancing groups are the .NET way to match nested pairs. |
| syntax.balancing | Balancing groups `(?<a-b>)` | implemented | More tab, `Balancing` composer; a nested-bracket pattern is verified. |
| syntax.conditionals | Conditionals `(?(1)a\|b)` | implemented | More tab, `Conditional` composer; branch selection verified. |
| syntax.lookaround | Lookahead and lookbehind, positive and negative | implemented | Groups tab. |
| syntax.mode-modifiers | Inline options `(?i-s)` and `(?i-s:..)` | implemented | More tab; letters validated. |
| syntax.comments | `(?#..)` comments and `x`-mode comments | implemented | Palette and explanation. |
| syntax.selection-aware | Selection-aware insertion and grouping | implemented | Group buttons wrap the selection (existing behavior). |
| syntax.options | Regex options | implemented | 8 flags above. |
| syntax.branch-reset | Branch reset, `\K`, `\R`, `\X` | unavailable | Not .NET syntax. Listed in the More tab. |

### 2. Replacement construction

| ID | Topic | Status | How, or why not |
| --- | --- | --- | --- |
| replace.preview | Replace preview with `$1`, `${name}`, `$0`, `$&`, `$$` | implemented | Replace section; verified including names and `$$`. |
| replace.warnings | Warnings for references to missing groups | implemented | Verified. |
| replace.other-refs | Last group `$+`, context `` $` `` / `$'`, whole subject `$_` | implemented | `$+`, `` $` ``, `$'` and `$_` are in the replacement palette and explained in plain words; the parser is compared with `Match.Result` on eight replacement cases. |
| replace.parse-explain | Independently parsed and explained replacement text | implemented | `RegexReplacementParser` splits replacement text into literals and the nine reference kinds, flags references to groups the pattern lacks (.NET inserts those literally) and explains each piece under the replacement box. Its own expansion is compared with the framework's on every case, and a deliberately wrong expander is shown to fail that comparison. |
| replace.encoded-chars | Literal and encoded characters inside the replacement | implemented | .NET replacement text has no escapes. The helper turns typed `\n \r \t \\ \0 \xHH \uHHHH \u{...}` into the characters themselves and inserts them at the caret; a second button doubles every `$` so ordinary text is inserted as typed. |
| replace.conditionals | Conditional replacement | unavailable | .NET replacement syntax has no conditionals. |

### 3. Analysis and exports

| ID | Topic | Status | How, or why not |
| --- | --- | --- | --- |
| analysis.tree | Token-by-token explanation tree | implemented | Existing tree; every family covered by a check, with group numbering as .NET assigns it. |
| analysis.select-tree-to-pattern | Selecting a tree row selects the token | implemented | Row selection highlights the span in the pattern box. |
| analysis.select-pattern-to-tree | Moving the caret selects the tree row | implemented | Moving the caret or selection in the pattern box selects the deepest displayed row (`RegexTreeExport.NodeAt`, checked on nested groups and token boundaries). The view wiring has not been exercised in a running window. |
| analysis.editable-tree | Editing through the tree | unavailable | Not built. |
| analysis.precision | Adjustable explanation precision | implemented | Detail selector: Overview (top level), Standard (three levels) or Full. It drives both the tree and the export. |
| analysis.invalid-edits | Explanation of invalid intermediate patterns | implemented | Error nodes; the framework error is shown with the error position underlined. |
| analysis.diagnostics | Tips and diagnostics | partial | Static backtracking findings (nested quantifier, repeated and overlapping alternatives, adjacent wildcards). No style tips. |
| analysis.help | Contextual syntax help | partial | Tooltips and per-control help text; no per-token help pages. |
| analysis.exports | Analysis export formats | implemented | The explanation tree exports as indented text, Markdown (backtick-safe code spans) or JSON (nested tokens with family, position and description), at the chosen detail; copy or save to a file. Bounded to 2000 tokens. The pattern, C# string, converted code and JSON exchange file export as before. |

### 4. Flavor and version comparison and conversion

| ID | Topic | Status | How, or why not |
| --- | --- | --- | --- |
| flavor.select | Select target flavors, languages and engine versions | partial | The comparison lets you choose which of seven engines are shown (.NET, PCRE2, JavaScript, Python re, Go RE2, Rust regex, grep -E). Each engine is a single named version; there is no version picker. |
| flavor.compare | Compare regex semantics across flavors | partial | Static comparison of the constructs the pattern uses, from a hand-written table of 37 constructs. Every probe in the table was run against the real engine while the table was written (PHP's PCRE2, Node, Python 3.14, Go, ripgrep, GNU grep) and the .NET column is checked against .NET on every test run. The application does not run any other engine on your pattern, so it states what the table says and nothing more. The version remarks in the table notes (for example "Python 3.11 and later") come from documentation and were not each tested. |
| convert.csharp | Convert to C# (verbatim string, regular string, code with options and timeout) | implemented | Quoting and option names verified. |
| convert.powershell | Convert to PowerShell | implemented | Single-quote doubling and `[regex]::new` options verified; not executed in PowerShell by the checks. |
| convert.json | JSON string escaping | implemented | Round trip verified for quotes, tabs, newlines and Unicode. |
| convert.grep | Convert to `grep -E` | partial | Construct-by-construct converter for `grep -E`; lazy quantifiers, lookarounds, atomic groups, conditionals, inline options, Unicode escapes, `\t`-style escapes, class subtraction and several flags are refused with the reason; lossy rewrites (`\d`, `\w`, `\A`, groups) are labelled. `grep -P` keeps PCRE2 constructs and refuses .NET-only ones using the flavor table. Checked by comparing .NET results on ASCII text; the `grep -P` and ripgrep command lines were also run once by hand against the real tools on a quoting-heavy pattern, but the `grep -E` output was not. |
| convert.preview-accept | Preview before accepting a conversion; keep the original | implemented | The converted text is a separate output; the pattern is never changed. |
| convert.other-languages | Java, JavaScript, Python, PHP, Perl, Ruby, others | partial | JavaScript, Python, Java, Go and PHP, with exact string quoting and option names, plus command lines for ripgrep and Select-String. Constructs a target cannot read stop the conversion; different readings are listed and mark the result lossy. The Java check uses the PCRE2 column because the table has no Java column. The JavaScript, Python, Java, Go, PHP, grep -P, ripgrep and Select-String output was run by hand against the real tools once; Perl, Ruby and others are not built. |

### 5. Testing and data

| ID | Topic | Status | How, or why not |
| --- | --- | --- | --- |
| test.match | Match testing with highlighting, counts, zero-width markers | implemented | Test section. |
| test.captures | Result and capture table, unmatched groups | implemented | Table with match, group, name, value, position; "did not participate". |
| test.replace | Replace testing | implemented | See replace.preview. |
| test.split | Split testing | implemented | Split section, bounded to 2000 pieces; capture groups explained. |
| test.suites | Expected outcomes (must match / must not match) | implemented | Per-field list with pass and fail report. Saved with a saved pattern (when the box is ticked) and with a remembered experiment; the sample text is stored only with an experiment. |
| test.navigation | Next and previous match navigation | implemented | Previous and Next match buttons (F3 and Shift+F3) step through the matches with wrap-around, select the match in the sample box and mark it in the highlighted view. The index logic is checked; the view has not been exercised in a running window. |
| test.scopes | Whole-subject, line and page scopes | unavailable | Whole sample only. |
| test.files | File and clipboard subjects | partial | Text can be pasted into the sample box, and **Search in files** searches the files of a folder. There is still no way to load one file as the sample or save a subject. |
| test.hex-encoding | Hex view, encodings, line endings | unavailable | Sample is plain UTF-16 text, cut to 64 KiB (UTF-8). |
| test.timeout | Bounded execution with a timeout warning | implemented | 250 ms per match, 2 s per test; timeouts reported in test, replace and split. |

### 6. Debugger

| ID | Topic | Status | How, or why not |
| --- | --- | --- | --- |
| debug.trace | Match-attempt trace and backtracking steps | unavailable | The .NET engine exposes no step or trace hook. Nothing is simulated or animated. |
| debug.scopes | Debug from cursor or selected match | unavailable | Same reason. |

### 7. Benchmark and diagnostics

| ID | Topic | Status | How, or why not |
| --- | --- | --- | --- |
| bench.growth | Input-growth timing and catastrophic-backtracking warning | partial | "Run timing probe" measures wall-clock time on the sample repeated 1 to 16 times and on an `a…a!` probe with the backtracking engine, 250 ms limit per probe, 3 s total, cancellable. It is a measurement on a few inputs, not a proof. |
| bench.steps | Step and backtracking counts | unavailable | Not exposed by .NET; the builder says so. |
| bench.compare | Compare several patterns on matching and non-matching subjects | unavailable | One pattern at a time. |

### 8. Code and escaping

| ID | Topic | Status | How, or why not |
| --- | --- | --- | --- |
| code.csharp | Generated C# for match, test, replace, split | implemented | Convert section. |
| code.powershell | Generated PowerShell | partial | Match, test, replace, split expressions; no surrounding script. |
| code.other | Other languages and APIs | partial | Match, test, replace and split snippets for JavaScript, Python, Java, Go and PHP (see convert.other-languages). Other languages are not built. |
| code.templates | Editable source templates | unavailable | Fixed templates. |
| code.escaping | Language-specific quoting | implemented | C# regular and verbatim, PowerShell single quotes, JSON, shell single quotes, JavaScript literal, Python raw or escaped, Java, Go raw or interpreted, PHP single-quoted delimited. |

### 9. Saved collections and parameterized reuse

| ID | Topic | Status | How, or why not |
| --- | --- | --- | --- |
| library.builtin | Built-in snippets with worked examples | implemented | 14 snippets, each checked against its example and counter-example. |
| library.user | Save, load, remove, search my patterns | implemented | `regex-patterns.json` in the app data folder, atomic write, at most 200; filter box. |
| library.import-export | Import and export collections | implemented | Import skips name clashes and patterns that do not compile; export saves a library file. File dialogs not exercised interactively. |
| library.metadata | Descriptions, replacement and test data in a collection | partial | A saved pattern keeps its name, pattern, flags and, when the box is ticked, its replacement and test cases. A description is supported in the file but the interface has no field for it yet. The sample text is deliberately never saved with a pattern. |
| library.parameters | Parameterized patterns | implemented | Placeholders `{{name}}` and `{{name:default}}`: the Parameters tab lists them, takes a value for each, and fills the pattern in one pass (values escaped as literal text unless fragments are chosen; a filled pattern that does not compile is refused). The selection can be turned into a parameter. A pattern with placeholders still compiles, so saved entries stay valid. |

### 10. File and folder GREP

| ID | Topic | Status | How, or why not |
| --- | --- | --- | --- |
| grep.all | File and folder search and replace with backups | partial | **Search in files**: folder, file patterns (with exclusions), subfolders, size limit, binary and link skipping, bounded results with file, line and column; replace in files as a dry run first, then an explicit apply that checks every file against its previewed SHA-256, writes a byte-exact undo snapshot first, preserves BOM and line endings, restores on failure, and can be undone later (a file edited after the apply is left alone). Missing: saving the result list, date and attribute filters, encodings beyond UTF-8 and UTF-16 with a byte order mark, context lines and opening a result in an editor. The snapshot is kept in the app data folder (10 newest), and the Local History log records the event, not the file contents. |

### 11. Multiple regexes and history

| ID | Topic | Status | How, or why not |
| --- | --- | --- | --- |
| history.recent | Recent patterns, recall | implemented | Twelve valid patterns per field in the session. **Remember this experiment** stores the full experiment on disk so it survives a restart. |
| history.independent | Independent experiments without cross-contamination | implemented | Every search field owns its pattern, flags, sample, replacement, test cases and blocks. |
| history.experiments | Navigable history of full experiments with data | implemented | **History** tab: remembers pattern, flags, sample (up to 16 KiB), replacement and test cases only when you press Remember, at most 50 experiments and 1 MiB in `regex-experiments.json`; load, forget one, forget all. Oversized text is refused, never cut. |

### 12. Integration and automation

| ID | Topic | Status | How, or why not |
| --- | --- | --- | --- |
| integration.all | External tool launch and return, command line, action files, automation | partial | Copy as a command line for `grep -E`, `grep -P`, ripgrep and Select-String, using the folder and file patterns of **Search in files**. No external tool launch and return, action files or automation. |

### 13. Help, editing, preferences and layout

| ID | Topic | Status | How, or why not |
| --- | --- | --- | --- |
| ui.keyboard | Keyboard access | partial | `Alt+R` opens, `Tab` cycles inside the panel, `Esc` closes, `F3` and `Shift+F3` step through matches. Not exercised in a running window. |
| ui.localization | Localization | partial | Cantonese entries cover the builder's labels, captions, palette descriptions and fixed messages (`LocStrings.Regex.cs`). Engine-generated error text and parser reasons, grep-conversion reasons, and the built-in snippet descriptions stay in English. |
| ui.layout | Configurable panels, docking, fonts, palettes | unavailable | The popover is a fixed layout that follows the application's appearance. |
| ui.preferences | Editor and operation preferences | unavailable | Only saved patterns persist. |
| ui.nested-search | Library and my-patterns filters carry their own builder | partial | They are plain filter boxes; a builder inside the builder was not added because nested popup behavior could not be verified. |

## Open items

Anything marked partial or unavailable above is open. The parity requirement is complete only when every applicable row has been exercised in the built application and every .NET-impossible row is kept as a documented limitation. Interactive Windows verification of the popover (layout, focus, dialogs, localization, the folder browser), and HuiShots, remain to do.

## How the flavor table was verified

Each construct in `RegexBuilderFlavors.cs` has a small probe pattern and a text it must match. During development every probe was run with the real engines: PHP (PCRE2), Node, Python 3.14, Go, ripgrep and GNU grep 3.0 (`-E`), and the table was corrected until the match and no-match results agreed with every cell. Two JavaScript cells (the `s` and `m` options) can only be written as flags, so they were checked separately. The probe for right-to-left matching is empty because the other engines have no equivalent. Only the .NET column is re-checked automatically (`RegexRemainingChecks`), including a deliberately corrupted cell that must be caught. Same-meaning notes such as "ASCII only" and the version remarks come from documentation, not from the probes.
