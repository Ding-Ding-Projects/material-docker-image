# Regex builder: RegexBuddy parity matrix

The regular expression builder is held to the functional baseline of the official [RegexBuddy manual](https://www.regexbuddy.com/manual.html) (family list reviewed on 2026-11-04 against the maintainers' parity checklist). This is an independent implementation: no RegexBuddy code, text or assets are used.

**Status is stated per topic, never as a family tick.** The parity requirement stays **open**. Several topics cannot exist on the .NET regular expression engine, and several others are not built yet.

- **implemented**: built, covered by a focused check in `tests/UiState/Program.cs`, and applicable to the .NET engine.
- **partial**: some of the topic works; the reason and the missing part are stated.
- **unavailable**: not built, or impossible on this engine; the exact reason is stated.

**Not verified:** none of the interface below has been exercised in the running application or captured as a HuiShot. Every "implemented" row is backed by logic checks on the framework-free engine classes only. The checks do not prove layout, focus order, keyboard use inside the popover, dialogs or pixels.

## Engine binding

- Engine: `System.Text.RegularExpressions` on .NET 10, regular expression dialect ".NET". The builder never silently substitutes another engine.
- The non-backtracking engine is used whenever the pattern allows it. Patterns that need backtracking (lookarounds, backreferences, atomic groups, conditionals, balancing groups, `RightToLeft`) run on the backtracking engine with a 250 ms limit per match.
- Flags offered (8): IgnoreCase, Multiline, Singleline, IgnorePatternWhitespace, ExplicitCapture, CultureInvariant, RightToLeft, NonBacktracking. RightToLeft uses the backtracking engine. NonBacktracking means "require the linear-time engine": a pattern that needs backtracking is reported as unsupported instead of falling back, and combining it with RightToLeft is refused.
- Limits: 4096 pattern characters, 64 KiB sample, 2000 highlighted matches, 2 s per test, 256 KiB replace output, 2000 split pieces, 200 test cases, 200 saved patterns.

## Matrix

Source files: `RegexBuilderEngine.cs` (flags, compile, test, replace), `RegexBuilderExplain.cs` (explanation tree), `RegexBuilderLibrary.cs` (snippets, tokens, composers, saved patterns), `RegexBuilderConstructs.cs` (rare constructs and plain-language blocks), `RegexBuilderOperations.cs` (split, test cases, diagnostics, timing), `RegexBuilderConvert.cs` (conversions), `RegexBuilderLibraryIO.cs` (import and search), `SearchBuilderPopover.cs` (view).

### 1. Construction and syntax

| ID | Topic | Status | How, or why not |
| --- | --- | --- | --- |
| syntax.raw | Raw pattern editing, synchronized with the search field | implemented | Pattern box in the popover, two-way with the field. |
| syntax.literal | Literal text with automatic escaping | implemented | Text tab, `RegexBuilderLibrary.Literal`. |
| syntax.chars | Escaped, nonprinting, 8-bit and Unicode characters | implemented | `\n \r \t \f \v \e \a \0` tokens; More tab inserts `\xHH`, `\uHHHH` or a surrogate pair from a code point or character; `\cA` composer exists in code (no button). |
| syntax.shorthand | Shorthand classes `\d \w \s` and negations | implemented | Classes tab. |
| syntax.posix | POSIX bracket classes | unavailable | .NET has no POSIX classes. Listed in the More tab with the replacement (`\p{L}`, `\d`, ranges). |
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
| replace.other-refs | Last group `$+`, context `` $` `` / `$'`, whole subject `$_` | partial | The engine applies them; the builder neither explains nor inserts them. |
| replace.parse-explain | Independently parsed and explained replacement text | unavailable | Only the missing-group check exists; there is no replacement explanation tree. |
| replace.encoded-chars | Literal and encoded characters inside the replacement | unavailable | No insertion helper for the replacement text. |
| replace.conditionals | Conditional replacement | unavailable | .NET replacement syntax has no conditionals. |

### 3. Analysis and exports

| ID | Topic | Status | How, or why not |
| --- | --- | --- | --- |
| analysis.tree | Token-by-token explanation tree | implemented | Existing tree; every family covered by a check, with group numbering as .NET assigns it. |
| analysis.select-tree-to-pattern | Selecting a tree row selects the token | implemented | Row selection highlights the span in the pattern box. |
| analysis.select-pattern-to-tree | Moving the caret selects the tree row | unavailable | Not built. |
| analysis.editable-tree | Editing through the tree | unavailable | Not built. |
| analysis.precision | Adjustable explanation precision | unavailable | One level of detail. |
| analysis.invalid-edits | Explanation of invalid intermediate patterns | implemented | Error nodes; the framework error is shown with the error position underlined. |
| analysis.diagnostics | Tips and diagnostics | partial | Static backtracking findings (nested quantifier, repeated and overlapping alternatives, adjacent wildcards). No style tips. |
| analysis.help | Contextual syntax help | partial | Tooltips and per-control help text; no per-token help pages. |
| analysis.exports | Analysis export formats | unavailable | The pattern, C# string, converted code and a JSON exchange file can be copied or saved. The explanation tree itself cannot be exported. |

### 4. Flavor and version comparison and conversion

| ID | Topic | Status | How, or why not |
| --- | --- | --- | --- |
| flavor.select | Select target flavors, languages and engine versions | unavailable | Only the .NET engine is bundled and run. |
| flavor.compare | Compare regex semantics across flavors | unavailable | Same reason. The builder does not claim equivalence. |
| convert.csharp | Convert to C# (verbatim string, regular string, code with options and timeout) | implemented | Quoting and option names verified. |
| convert.powershell | Convert to PowerShell | implemented | Single-quote doubling and `[regex]::new` options verified; not executed in PowerShell by the checks. |
| convert.json | JSON string escaping | implemented | Round trip verified for quotes, tabs, newlines and Unicode. |
| convert.grep | Convert to `grep -E` | partial | Construct-by-construct converter; lazy quantifiers, lookarounds, atomic groups, conditionals, inline options, Unicode escapes, `\t`-style escapes, class subtraction and several flags are refused with the reason; lossy rewrites (`\d`, `\w`, `\A`, groups) are labelled. Checked by comparing .NET results on ASCII text; not executed with a real grep. |
| convert.preview-accept | Preview before accepting a conversion; keep the original | implemented | The converted text is a separate output; the pattern is never changed. |
| convert.other-languages | Java, JavaScript, Python, PHP, Perl, Ruby, others | unavailable | Not built. |

### 5. Testing and data

| ID | Topic | Status | How, or why not |
| --- | --- | --- | --- |
| test.match | Match testing with highlighting, counts, zero-width markers | implemented | Test section. |
| test.captures | Result and capture table, unmatched groups | implemented | Table with match, group, name, value, position; "did not participate". |
| test.replace | Replace testing | implemented | See replace.preview. |
| test.split | Split testing | implemented | Split section, bounded to 2000 pieces; capture groups explained. |
| test.suites | Expected outcomes (must match / must not match) | partial | Per-field list with pass and fail report. Kept in memory only; not saved with a pattern. |
| test.navigation | Next and previous match navigation | unavailable | Not built. |
| test.scopes | Whole-subject, line and page scopes | unavailable | Whole sample only. |
| test.files | File and clipboard subjects | partial | Text can be pasted into the sample box. There is no file open or save for subjects. |
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
| code.other | Other languages and APIs | unavailable | Not built. |
| code.templates | Editable source templates | unavailable | Fixed templates. |
| code.escaping | Language-specific quoting | implemented | C# regular and verbatim, PowerShell single quotes, JSON, shell single quotes. |

### 9. Saved collections and parameterized reuse

| ID | Topic | Status | How, or why not |
| --- | --- | --- | --- |
| library.builtin | Built-in snippets with worked examples | implemented | 14 snippets, each checked against its example and counter-example. |
| library.user | Save, load, remove, search my patterns | implemented | `regex-patterns.json` in the app data folder, atomic write, at most 200; filter box. |
| library.import-export | Import and export collections | implemented | Import skips name clashes and patterns that do not compile; export saves a library file. File dialogs not exercised interactively. |
| library.metadata | Descriptions, replacement and test data in a collection | partial | Name, pattern and flags only. Sample, replacement and test cases are deliberately not saved. |
| library.parameters | Parameterized patterns | unavailable | Not built. |

### 10. File and folder GREP

| ID | Topic | Status | How, or why not |
| --- | --- | --- | --- |
| grep.all | File and folder search and replace with backups | unavailable | Not built. A mass file replacement tool is outside this Docker application and would need its own backup and restore design. |

### 11. Multiple regexes and history

| ID | Topic | Status | How, or why not |
| --- | --- | --- | --- |
| history.recent | Recent patterns, recall | partial | Twelve valid patterns per field, this session only (privacy: nothing is persisted without Save). |
| history.independent | Independent experiments without cross-contamination | implemented | Every search field owns its pattern, flags, sample, replacement, test cases and blocks. |
| history.experiments | Navigable history of full experiments with data | unavailable | Only patterns are remembered. |

### 12. Integration and automation

| ID | Topic | Status | How, or why not |
| --- | --- | --- | --- |
| integration.all | External tool launch and return, command line, action files, automation | unavailable | Not built. |

### 13. Help, editing, preferences and layout

| ID | Topic | Status | How, or why not |
| --- | --- | --- | --- |
| ui.keyboard | Keyboard access | partial | `Alt+R` opens, `Tab` cycles inside the panel, `Esc` closes. Not exercised in a running window. |
| ui.localization | Localization | partial | Cantonese entries cover the builder's labels, captions, palette descriptions and fixed messages (`LocStrings.Regex.cs`). Engine-generated error text and parser reasons, grep-conversion reasons, and the built-in snippet descriptions stay in English. |
| ui.layout | Configurable panels, docking, fonts, palettes | unavailable | The popover is a fixed layout that follows the application's appearance. |
| ui.preferences | Editor and operation preferences | unavailable | Only saved patterns persist. |
| ui.nested-search | Library and my-patterns filters carry their own builder | partial | They are plain filter boxes; a builder inside the builder was not added because nested popup behavior could not be verified. |

## Open items

Anything marked partial or unavailable above is open. The parity requirement is complete only when every applicable row has been exercised in the built application and every .NET-impossible row is kept as a documented limitation. Interactive Windows verification of the popover (layout, focus, dialogs, localization), and HuiShots, remain to do.
