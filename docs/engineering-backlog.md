# Engineering Backlog

Known gaps and deferred work, each with an evidence pointer and what would make the fix safe.
Entries are added by the `retro` skill (see `.claude/skills/retro/`) when a session surfaces a
defect that is real but out of scope for the change at hand. This is not a feature roadmap — the
v1 out-of-scope list lives in `CONTRIBUTING.md`.

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
