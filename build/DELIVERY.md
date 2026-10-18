# Windows delivery inventory

The supported product build is native WPF on .NET 10, self-contained for Windows x64. Run the root build.bat or build-installer.bat entrypoint. The dependency fetcher uses build/bootstrap.ps1. Its reviewed download versions and hashes live in dependencies.json. Bootstrap is user scoped and does not change global PATH or require elevation.

The release job runs on windows-2025. It needs PowerShell, Git, GitHub CLI, .NET SDK 10.0.401, NuGet 6.14.0 and Squirrel.Windows 2.0.1. The first three are provided by that selected hosted image; the remaining tools are detected or obtained by the exact root entrypoint. No signing credential is required. A fresh-host bootstrap run is unverified until its actual workflow receipt exists.

The single job builds, packages and publishes. It runs no tests, lint, static analysis or UI checks. This allows a release to ship from source whose local checks would fail. Build and publication failures remain real failures. Safe logs and receipts upload even after a failure, with bounded retention. Locally, build.bat -Tests invokes only the standalone core executable, and build.bat -DockerSmoke enables its explicitly requested live Docker route. Neither route builds the GUI or runs in the release workflow.

Every branch push and manual dispatch gets a unique version derived from run number and attempt. Tag pushes are excluded. The release targets the exact source SHA and is read back as normal, not draft or prerelease. Expected assets are Setup.exe, RELEASES, the full Squirrel package, generated deltas where available, and source/hash receipts. Packages are unsigned. The workflow timestamp is measured from the API-reported workflow start through successful release creation, then stored in the release notes; the release API's publication time remains available separately.

The self-contained output bundles the .NET runtime. Docker Engine and registries are user-selected integration targets, not bundled runtime prerequisites. Product runtime, installer installation, updater behavior, GUI companions, and deployed documentation require their own evidence. This inventory does not claim those checks ran.

## Bootstrap completeness

- [x] release: exact SDK, NuGet and Squirrel versions and download digests declared.
- [x] release: root entrypoints automatically invoke dependency bootstrap.
- [ ] release: fresh hosted execution and cache-miss bootstrap receipt.
- [ ] release: downloaded installer and current-host installed-update verification.

