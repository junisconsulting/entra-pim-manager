# Engineering Backlog

Known gaps and deferred work, each with an evidence pointer and what would make the fix safe.
Entries are added by the `retro` skill (see `.claude/skills/retro/`) when a session surfaces a
defect that is real but out of scope for the change at hand. This is not a feature roadmap — the
v1 out-of-scope list lives in `CONTRIBUTING.md`.

---

## Releases are not code-signed — no signing exists anywhere in the pipeline

**Evidence:** `.github/workflows/release.yml` contains no signing step, no certificate secret and
never passes `-SignParams`; `packaging/velopack/build.ps1` only *warns* "Building UNSIGNED". Every
published release to date (0.4.x, 0.5.0) ships unsigned binaries — confirmed in the field
2026-08-14: `Get-AuthenticodeSignature` on the installed 0.5.0 stub and app exe returns
`NotSigned`.

**Why it matters:** managed environments (AppLocker/WDAC/EDR) block unknown unsigned executables.
A customer environment that had whitelisted one version's binaries (by hash) blocks **every
update** — observed as a Setup that "partially succeeds" (files written, launch denied) or an app
that silently never starts. Unsigned releases make each update a support ticket at every
app-control customer, and hash-whitelisting on the customer side is a treadmill because Velopack
updates change every hash.

**What makes the fix safe:** a junis code-signing certificate (OV/EV) provisioned as a CI secret,
`build.ps1 -SignParams` wired into the release workflow on `windows-latest`, and one release
verified with `Get-AuthenticodeSignature` = `Valid` on Setup.exe, Update.exe, the stub and the app
exe. Then app-control customers can replace per-version hash rules with a single publisher rule.
Until that lands, the docs must say releases are unsigned (they do, as of 2026-08-14) — not imply
the opposite.

---

## Velopack Desktop shortcut suppression is unverified

**Evidence:** `packaging/velopack/build.ps1:97` passes `--shortcuts StartMenuRoot` and its comment
claims this suppresses the Desktop shortcut. Velopack 1.2 marks the `Velopack.Windows.Shortcuts`
runtime API `[Obsolete]` with: *"Desktop and StartMenuRoot shortcuts are now created and removed
automatically when your app is installed / uninstalled."* The flag is therefore likely ignored and
a Desktop shortcut may appear despite the comment.

**Why it matters:** the app is tray-first; an unwanted Desktop icon is a visible defect, and the
comment currently documents behaviour nobody has confirmed.

**What makes the fix safe:** a real installer run on Windows that observes what shortcuts actually
get created. Only then decide between removing the misleading comment, dropping the flag, or
actively removing the Desktop shortcut at first run.

**Do not** simply delete the `CS0618` suppression in
`src/Entra-PIM-Manager.App.Avalonia/Services/ShortcutService.cs` — it is deliberate. The runtime
`Shortcuts` API is the only way to let a user opt out, and it preserves the **AppUserModelId** that
toast notifications depend on; a hand-rolled `.lnk` would lose it.

---

## `Entra-PIM-Manager.slnx` is unusable with SDK 8.0.x

**Evidence:** `dotnet build Entra-PIM-Manager.slnx` on SDK 8.0.421 fails with
`MSB4068: The element <Solution> is unrecognized`. The `.slnx` solution format requires SDK 9.0.2xx
or newer.

**Why it matters:** every local build and the `verify` skill work around it by building the three
`.csproj` files individually. CI is unaffected (it runs `dotnet restore` / `dotnet build` without a
solution argument), so the breakage is invisible there.

**What makes the fix safe:** either pin the repo to an SDK that understands `.slnx` via a
`global.json` (and confirm CI's `setup-dotnet` still resolves), or add a classic `.sln` alongside.
Both are decisions about the supported toolchain, not cleanups — do not change this as a side
effect of another task.

---

## `.claude/manual-test-checklist.md` describes a UI that no longer exists

**Evidence:** the checklist still asks the tester to verify a `ConfigurationWindow` at first start
(§5) and a tray-menu **"Sign out"** / **"Eligible Roles…"** / **"Active Assignments…"** entry (§1,
§4). None of them exist: configuration moved into the Settings slide-in, and `App.axaml:22-38`
defines the menu as Open / Refresh / Settings… / Start with Windows / Exit. The paths in §5 and §7
also predate the 0.4.0 move to `%LocalAppData%\junis\Entra-PIM-Manager\`.

**Why it matters:** the checklist is the release gate — the `release` skill refuses to tag without a
signed-off run. A gate that asks for things that cannot be found trains the tester to wave items
through, which is worse than having no gate.

**What makes the fix safe:** a full pass on Windows against the current UI, rewriting each item to
what is actually on screen. Do this as its own change with the app in front of you, not as a
side effect of a feature — guessing at the wording is how the drift happened in the first place.
Section 1b (sovereign cloud, added in 0.4.2) is current and should be kept as-is.
