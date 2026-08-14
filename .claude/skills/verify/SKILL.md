---
name: verify
description: Project verify procedure for Entra PIM Manager — run after any code change. Build with warnings-as-errors and StyleCop, unit tests, the Core coverage gate, and a Velopack win-x64 Setup.exe so a fresh installable build with the current changes is always available for local testing. Use when asked to verify changes, before declaring work done, or when the /verify skill bootstraps.
---

# Entra PIM Manager Verify Procedure

All commands run from the repository root. Steps 1, 2 and 4 always run; step 3 is conditional on
whether `Core` changed (check `git status` / the session's edits). Report every failure with its
output — never summarize a red step as "mostly fine".

The projects target `net8.0-windows10.0.17763.0`, but neither `Core` nor `Tests` pulls in a
Windows-desktop runtime dependency, so **the whole procedure runs on Linux**. Only launching the app
needs Windows — see "Out of scope".

## 0. Environment (Linux hosts)

```bash
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$HOME/.dotnet:$PATH"
```

`dotnet` is not on the default PATH — the SDK lives at `~/.dotnet` (installed without root). On
Windows, skip this step and drop `-p:EnableWindowsTargeting=true` from every command below.

## 1. Build — always

`Entra-PIM-Manager.slnx` is **not parseable by SDK 8.0.x** (`MSB4068: The element <Solution> is
unrecognized` — .slnx needs 9.0.2xx+). Build the three projects individually:

```bash
dotnet build src/Entra-PIM-Manager.Core/Entra-PIM-Manager.Core.csproj -c Release -warnaserror -p:EnableWindowsTargeting=true
dotnet build src/Entra-PIM-Manager.App.Avalonia/Entra-PIM-Manager.App.Avalonia.csproj -c Release -warnaserror -p:EnableWindowsTargeting=true
dotnet build src/Entra-PIM-Manager.Tests/Entra-PIM-Manager.Tests.csproj -c Release -warnaserror -p:EnableWindowsTargeting=true
```

Expect ~3 s each once restored. This step is the type check, the StyleCop pass, and — because
`AvaloniaUseCompiledBindingsByDefault` is on — the XAML binding check, all at once.

Most first-attempt failures here are the two StyleCop rules described in CLAUDE.md, "Code standards".

## 2. Unit tests — always

```bash
dotnet test src/Entra-PIM-Manager.Tests/Entra-PIM-Manager.Tests.csproj -c Release --no-build -p:EnableWindowsTargeting=true
```

Full pass in well under a second. `--no-build` is deliberate: step 1 already built with
`-warnaserror`, and a rebuild here would silently drop that flag.

## 3. Coverage gate — only when `Core` changed

The gate applies to `Entra-PIM-Manager.Core` only (`coverlet.runsettings` restricts measurement to
`[EntraPimManager.Core]*`, so the report's overall line-rate *is* the Core line-rate). UI code is
deliberately uncovered.

```bash
dotnet test src/Entra-PIM-Manager.Tests/Entra-PIM-Manager.Tests.csproj -c Release --no-build \
  -p:EnableWindowsTargeting=true \
  --collect:"XPlat Code Coverage" --settings coverlet.runsettings --results-directory ./TestResults
pwsh ./build/check-coverage.ps1 -MinimumLineRate 0.70
```

`pwsh` is available on Linux, so this is byte-for-byte the same gate CI runs — no reimplementation,
no drift. Comfortable headroom against the 70 % gate (85 % as of 2026-08). `TestResults/` is
gitignored.

## 4. Package — always (installable Setup.exe with the current changes)

Every verify run produces `packaging/velopack/releases/Entra-PIM-Manager-win-Setup.exe` — a real
Velopack installer the user runs **over their existing install** on Windows. That is deliberately
the realistic test path (same install mechanism as a release, single file to copy) and it prevents
the expensive failure mode of testing a stale build. Cost: seconds when incremental, a few minutes
cold; it also doubles as the apphost + package smoke test for `.csproj` / `packaging/velopack/**` /
`app.manifest` / `Assets/` changes.

The version is derived, never typed: next patch above the latest tag, plus a `-local.<timestamp>`
prerelease suffix. Each build therefore installs over the previous test build, while the next real
release (`0.x.y` without suffix) still upgrades over any local build. This is purely mechanical —
which digit the *real* next release bumps is decided by the rules in the `release` skill, "Choose
the version".

```bash
BASE=$(git describe --tags --abbrev=0 | sed 's/^v//')
VERSION="${BASE%.*}.$(( ${BASE##*.} + 1 ))-local.$(date +%Y%m%d%H%M)"
dotnet publish src/Entra-PIM-Manager.App.Avalonia/Entra-PIM-Manager.App.Avalonia.csproj \
  -c Release -r win-x64 --self-contained true -p:EnableWindowsTargeting=true \
  -p:Version=$VERSION -o packaging/velopack/publish
test -f packaging/velopack/publish/Entra-PIM-Manager.exe || echo "FAIL: no Windows apphost produced"
vpk '[win]' pack --channel win --packId Entra-PIM-Manager --packVersion $VERSION \
  --packDir packaging/velopack/publish --mainExe Entra-PIM-Manager.exe \
  --packTitle "Entra PIM Manager" --packAuthors "junis GmbH" \
  --icon src/Entra-PIM-Manager.App.Avalonia/Assets/app-icon.ico \
  --shortcuts StartMenuRoot --outputDir packaging/velopack/releases
```

Traps, all verified (see also `packaging/velopack/README.md`):

- `-p:Version=` is not optional — without it the assembly silently reports `1.0.0`.
- `-r win-x64` is what makes it an `.exe`; without a RID, publish emits a *Linux* apphost.
- The `'[win]'` directive must be shell-quoted, or vpk silently targets the host OS (AppImage).
- `vpk` args mirror `packaging/velopack/build.ps1` (the Windows/CI path — its backslash paths do
  not run under Linux pwsh); if the pack options there change, mirror them here.
- Local builds are **unsigned** (SmartScreen will warn once) and must never be promoted to a
  release. `packaging/velopack/publish|releases` are gitignored.
- **Unsigned builds cannot be tested on machines with application control** (AppLocker/WDAC/EDR —
  typical on customer VMs). Symptom, seen in the field 2026-08: Setup extracts everything, then
  fails launching the app with `Access is denied (0x80070005)` — "Install Partially Succeeded",
  and the machine is left with an unlaunchable `current\`; repair by re-running the signed release
  Setup from GitHub. Signed CI releases pass (publisher rule on the junis cert). Test unsigned
  builds on unmanaged machines only, or sign on Windows via `build.ps1 -SignParams`. Diagnose via
  `Setup.exe --verbose --log <path>` plus the AppLocker `EXE and DLL` (8004) / `CodeIntegrity`
  (3077) event logs.

After packing, tell the user the Setup.exe is ready and name the path — they copy that one file to
Windows, quit the running tray instance, and run it to upgrade in place.

## Reporting

Report each step as pass/fail with the failing output verbatim. A step that was skipped because its
condition did not apply is reported as skipped, with the reason — not as passed.

## Out of scope

- **Running the app.** It is Windows-only (WAM broker, UWP toasts). On Linux, verify proves it
  builds and that `Core` behaves — nothing about the running UI. Real-device behaviour is covered by
  `.claude/manual-test-checklist.md`, which is a pre-release gate, not part of verify.
- **Anything touching a tenant.** No sign-in, no Graph calls, no activation. Never activate a real
  PIM role to "check" a change.
- **`/ponytail-review`.** A judgment pass on the final diff before committing (see CLAUDE.md,
  Commit style), not a pass/fail check.
- **Linting/formatting beyond StyleCop.** No separate linter exists — do not invent one.
