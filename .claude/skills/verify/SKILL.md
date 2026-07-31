---
name: verify
description: Project verify procedure for Entra PIM Manager — run after any code change. Build with warnings-as-errors and StyleCop, unit tests, and the Core coverage gate; publish smoke only when packaging inputs changed. Use when asked to verify changes, before declaring work done, or when the /verify skill bootstraps.
---

# Entra PIM Manager Verify Procedure

All commands run from the repository root. Steps 1–2 always run; steps 3–4 are conditional on what
changed (check `git status` / the session's edits). Report every failure with its output — never
summarize a red step as "mostly fine".

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

137 tests, ~0.1 s. `--no-build` is deliberate: step 1 already built with `-warnaserror`, and a
rebuild here would silently drop that flag.

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
no drift. Current headroom: 80.8 % against a 70 % gate. `TestResults/` is gitignored.

## 4. Publish smoke — only when packaging inputs changed

Run this when a `.csproj`, `packaging/velopack/**`, `app.manifest`, or anything under `Assets/`
changed:

```bash
dotnet publish src/Entra-PIM-Manager.App.Avalonia/Entra-PIM-Manager.App.Avalonia.csproj \
  -c Release -r win-x64 --self-contained true -p:EnableWindowsTargeting=true -o artifacts/win-x64
test -f artifacts/win-x64/Entra-PIM-Manager.exe || echo "FAIL: no Windows apphost produced"
```

`-r win-x64` is what makes it an `.exe`. Without a RID, publish emits a *Linux* apphost — an
extensionless file that looks like success until someone tries to run it on Windows.
`artifacts/` is gitignored.

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
