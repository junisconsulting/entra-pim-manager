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

**Why it matters:** managed environments block unknown unsigned executables — observed as a Setup
that "partially succeeds" (files written, launch denied at `CreateProcess`) or an app that
silently never starts. Field evidence 2026-08-14 (Defender events 1121/1122): the ASR rule
"Use advanced protection against ransomware" (`C1DB55AB-C21A-4637-BB3F-A12568109D35`, block mode)
denied a minutes-old unsigned release as "untrusted or unsigned", while the "prevalence, age, or
trusted list" rule fired in audit; a ~12-hour-old unsigned release ran on the same machine
because its hash had accrued cloud reputation — no IT action in between. Consequence: every
fresh release is dead on arrival at such customers for roughly a day, and auto-update stages a
blocked binary that kills the installed app. Signing fixes this durably: the blocking rule's
trigger is literally "untrusted and unsigned", and reputation then attaches to the constant
publisher instead of each release's day-old hash.

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

---

## A device-code account cannot recover from a changed scope set

**Evidence:** `MsalAuthService.AcquireForDeviceCodeAccountAsync`
(`src/Entra-PIM-Manager.Core/Auth/MsalAuthService.cs`) renews strictly silently and deliberately
has no interactive fallback — the comment there explains why. Field evidence 2026-09-03/04, while
testing the 0.8.0 scope addition: one device-code enrollment logged
`MsalUiRequiredException / AADSTS65001 "The user or administrator has not consented"` once per
refresh tick for over eight hours (1020 warnings, 22:00:44 → 06:27:49) and never recovered; MSAL
answered most of those from its throttle cache (`MsalThrottledUiRequiredException`). The broker
path (`AcquireForAccountAsync`, same file) catches `MsalUiRequiredException` and goes interactive,
so broker enrollments repair themselves on the next refresh.

**Why it matters:** every scope change invalidates the cached grant, so *all* device-code
enrollments start failing at once on the first start after such an update. The tenant group shows
the generic "Couldn't load eligibilities for this tenant" (`PimErrorMapper.DescribeFetchFailure`
has no arm for `AADSTS65001`), which names neither the cause nor the way out, and the refresh loop
keeps retrying once a minute against a throttled MSAL. The user's only repair is to remove the
account and re-run the device-code enrollment — which the app never tells them.

**What makes the fix safe:** the recovery must not silently become a broker sign-in — that would
defeat the reason the account uses device code (federated IdP, see the comment in
`AcquireForDeviceCodeAccountAsync`). The minimum honest fix is diagnostic, not automatic: map
`AADSTS65001` in `DescribeFetchFailure` to "this tenant has not consented to the app's current
permissions — an admin must grant consent, then add this account again", and verify it on a
device-code enrollment in a tenant where consent was deliberately withheld. A re-enrollment button
on the failing row is the larger follow-up; it needs the device-code UI flow driven from the
account list, which does not exist yet.
