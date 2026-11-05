# Build companion

`MaterialDockerImageBuild.exe` is a small, separate Windows window for people who prefer a button to a command prompt. It does not replace `build.bat` or `build-installer.bat`; it runs them. Every release attaches it beside the installer.

## What it does

- **Carries its own source.** The executable contains the complete source of the commit it was built from (every tracked file plus the generated changelog, about 10 MB packed) and a manifest with each file's SHA-256. When no checkout is found it unpacks that source once per commit to `%LOCALAPPDATA%\DingDingProjects\MaterialDockerImageBuild\source\<commit>` and builds there, so a downloaded companion builds with nothing else present. Unpacking refuses rooted paths, `..`, drive or stream colons, files the manifest does not list and any file whose hash differs; it writes to a temporary sibling and renames it into place only after every check. An intact earlier unpack of the same commit is reused; a damaged one is replaced. The unpacked copy has no git history, so the build scripts read the commit from its manifest and keep the packed changelog.
- **Finds the repository.** It uses `--repo <path>` when given, otherwise walks up from its own folder looking for `build.bat`, otherwise uses the source packed inside it. **Change folder** still lets you choose another checkout, and the folder dialog opens on its own only when the packed source is missing or could not be unpacked (the reason is shown in the log). A `--repo` path that is relative, contains characters `cmd.exe` would interpret (`"`, `%`, `^`, `!`, `<`, `>`, `|`) or has no `build.bat` is refused.
- **Setup check.** Lists `build.bat`, `build-installer.bat`, `download-dependencies.bat`, `.tools/dotnet-path.txt`, `.tools/node-path.txt` and `out/publish/MaterialDockerImage.exe`. The three batch files are required. The two `.tools` files are created by the first build: the batch files download and verify the pinned .NET SDK and Node.js themselves (see `build/dependencies.json`), without administrator rights. The companion only reports and explains this; it never elevates and never installs anything itself.
- **Build.** Runs the repository's `build.bat`, with `-Version x.y.z` when a version is entered.
- **Build and run.** Runs `build.bat`, then starts `out/publish/MaterialDockerImage.exe` only if the build exited with code 0 and the file exists.
- **Build installer.** Runs `build-installer.bat -Version x.y.z`. The version is required. The installer is unsigned, so Windows may show an unknown-publisher warning.
- **Live progress.** Standard output and standard error stream into the log view while the build runs (standard error lines are prefixed `[err]`). A phase label (starting, preparing dependencies, compiling and publishing, packaging installer) is inferred from the output text and is a hint only; the progress bar is indeterminate while running.
- **Real result.** The window shows elapsed time, the real exit code and a state chip: Succeeded, Failed, Cancelled or Could not start. Success is reported only for exit code 0 without cancellation.
- **Cancel** (button or Esc) kills the whole process tree. **Copy log** (button or Ctrl+Shift+C) copies the complete log.
- **Language.** The language button cycles English, Cantonese and bilingual labels from a small local string table. Build output stays as the tools print it.

Keyboard: Ctrl+B build, Ctrl+R build and run, Ctrl+I installer, Esc cancel, Ctrl+Shift+C copy log. Every control has an automation name; the version field has help text; the state chip is a polite live region.

## How commands are run

Each batch file is started as `cmd.exe /d /c ""<repo>\build.bat" -Version 1.2.3"` with the repository as the working directory. The version must match `^\d+\.\d+\.\d+$` (ASCII digits only, no trailing text). Nothing the user types reaches a command line unvalidated. The logic lives in `src/BuildCompanion/Logic/` (`BuildPlan`, `ArgumentValidation`, `BuildRunner`, `RunStates`, `PhaseTracker`, `RepoLocator`, `SetupCheck`, `CompanionStrings`) and has no WPF dependency, so the UI-state test project compiles it directly.

## Failure modes

| Situation | Result |
| --- | --- |
| Batch file exits non-zero | Failed, exit code shown, application not launched |
| `out/publish/MaterialDockerImage.exe` missing after Build and run | Could not start (never success) |
| Cancelled | Cancelled, process tree killed |
| Invalid version | Field error; the installer button stays disabled; nothing is run |
| Missing `build.bat` family | Setup check shows them missing; actions disabled |
| Clipboard unavailable | Message shown; log is unchanged |

## Appearance

Material 3 dark theme with the same colour roles and button shapes as the main application (`src/BuildCompanion/Theme.xaml` mirrors the role keys of `src/MaterialDockerImage/Themes/Material.xaml`; the Material Symbols font file is linked from the main project, not copied). There is no light theme or appearance editor in the companion.

## Packaging

`build/publish-companion.ps1 [-Version x.y.z]` first packs the complete source into `out/companion/source.zip` with its manifest (`EMBEDDED-SOURCE.json`) and embeds it, then publishes `src/BuildCompanion` as a self-contained, single-file `win-x64` executable to `out/companion/MaterialDockerImageBuild.exe`, refuses any extra output file, rebinds the source inventory and writes `out/receipts/companion.json` (source, version, SHA-256, size, and the packed source's commit, file count and zip hash). Before writing the receipt it runs the published executable with `--embedded-source-commit FILE`, which writes the packed commit and exits without a window, and fails unless that commit equals the commit being built. `build-installer.bat` runs it after packaging and copies the executable and receipt into `out/releases`, so the release workflow uploads both. The workflow verifies that both appear in the published release assets and that the receipt source equals the workflow commit.

## Verification

- `build.bat -Tests` runs the companion checks inside the UI-state suite: version validation (including Unicode digits, trailing newline and injected text), exact argument building for all three actions, unsafe repository paths, exit-code-to-state mapping, repository discovery, string table completeness, and real runs of a stand-in `build.bat` through the companion's own `cmd.exe /c` route (stdout and stderr streaming, exit 0, exit 7, missing application, and cancellation of a running tree).
- `build/publish-companion.ps1` produced the single-file executable and receipt.
- Not yet verified: interactive use of the window (clicking each action, the folder picker, copy log, language cycling, keyboard shortcuts), a real end-to-end `build.bat` run driven from the window, screen-reader behaviour, and behaviour at other display scales. No window was opened during development of this feature.
