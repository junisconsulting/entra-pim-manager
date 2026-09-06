# CLAUDE.md

Canonical project-wide rules for Entra PIM Manager. Every rule lives in exactly one place; where
another artifact owns the detail, this file points at it instead of restating it. The exception is
deliberate: rules needed *while writing code* are duplicated here from `CONTRIBUTING.md`, because
this file is loaded every session and that one is not.

## Project

A Windows tray application for activating Microsoft Entra PIM eligibilities (Directory Roles, PIM
for Groups, and PIM for Azure Resources) across multiple tenants — without UAC, admin rights, or a service install.
Per-user install to `%LocalAppData%\Programs\Entra-PIM-Manager\`, WAM-broker auth, Velopack auto-update.
Public repository under MIT (`LICENSE`); contributions are inbound=outbound, no CLA.

Assume every commit is world-readable: no tenant IDs, no internal hostnames, no credentials.

## Key documentation

- `CONTRIBUTING.md` — build/test, code conventions, security conventions, PR process, out-of-scope list
- `README.md` — user-facing feature set, install, app-registration summary
- `docs/app-registration-setup.md` — the delegated permissions (Graph scopes plus Azure Service
  Management) and the consent procedure
- `docs/engineering-backlog.md` — known gaps and deferred work, with evidence pointers
- `SECURITY.md` — vulnerability reporting

## Skills (read before writing code in these areas)

| Skill | Use for |
| --- | --- |
| `.claude/skills/entra-pim-graph-api/` | anything calling `roleManagement/*` or `identityGovernance/privilegedAccess/group/*` — endpoints, casing traps, error codes, policy rules |
| `.claude/skills/azure-rbac-pim-arm-api/` | anything calling `management.azure.com/*/providers/Microsoft.Authorization/role*Schedule*` or `roleManagementPolicyAssignments` — PIM for Azure Resources: URLs, bodies, status values, error codes, differences from Graph |
| `.claude/skills/msal-dotnet-desktop-wam/` | anything touching `Microsoft.Identity.Client`, the WAM broker, the token cache, or claims challenges |
| `.claude/skills/verify/` | the pass/fail procedure after every code change |
| `.claude/skills/release/` | cutting a release (tag → CI → GitHub release) |
| `.claude/skills/retro/` | end-of-session sweep for learnings |

Never write PIM Graph, PIM ARM or MSAL desktop code from memory — all three areas are full of
obsolete patterns that are common in training data. The skills are the authority.

## Architecture (three projects, one layering boundary)

```text
src/Entra-PIM-Manager.App.Avalonia  →  Avalonia views, ViewModels, tray   (UI only)
src/Entra-PIM-Manager.Core          →  Auth, Graph, models, services      (no UI deps)
src/Entra-PIM-Manager.Tests         →  xUnit, Moq                         (tests Core only)
```

`Core` must not reference Avalonia, WPF, or any other UI toolkit. That boundary is what keeps the
tests fast and runnable on a non-Windows host.

All Graph and ARM access goes through the service layer (`PimRoleService`, `PimGroupService`,
`PimAzureResourceService`, `PolicyService`, `TenantInfoService`). Never call `HttpClient` directly
against `graph.microsoft.com` or `management.azure.com` — that bypasses auth, retry, and the
claims-challenge handler. ARM clients come from `IArmClientFactory`.

Stack: .NET 8 LTS · Avalonia 12 + CommunityToolkit.Mvvm · MSAL.NET 4.66 + `Microsoft.Identity.Client.Broker`
· Microsoft.Graph SDK v5 · Velopack 1.2 · Serilog · xUnit + Moq.

## Commands

The project targets `net8.0-windows10.0.17763.0` — but Core and Tests carry no Windows-desktop
runtime dependency, so **build, test, and the coverage gate all run on Linux**. Only running the
app itself requires Windows.

```bash
export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$HOME/.dotnet:$PATH"   # dotnet is not on the default PATH
```

Two flags carry the whole cross-platform story:

- `-p:EnableWindowsTargeting=true` — permits the `net8.0-windows` TFM on Linux (else `NETSDK1100`).
- `-r win-x64` — on `publish` only, forces the **Windows** apphost. Without a RID, `publish` emits a
  *Linux* apphost (an extensionless file), not an `.exe`.

The exact build/test/coverage commands live in the `verify` skill (`.claude/skills/verify/`) — run it
after any code change. On Windows the plain `dotnet build -c Release -warnaserror` / `dotnet test`
from `CONTRIBUTING.md` work as documented; the flags above are the Linux-host addendum.

## Code standards

- Language: code, identifiers, comments, UI text, and commit messages — **English** (`CONTRIBUTING.md`).
  Docs are English too, with one legacy exception: `packaging/velopack/README.md` is German. Match a
  file's existing language when editing it; do not translate one as a side effect of another change.
- `<Nullable>enable</Nullable>` everywhere; no `#nullable disable` pragmas.
- All I/O is async; no `.Result`, `.Wait()`, or `.GetAwaiter().GetResult()` outside `Main` / static ctors.
- Every public async method in `Core` takes `CancellationToken ct = default` last. UI callers pass a
  token with a sensible timeout (~30 s for Graph).
- Graph errors are mapped through `PimErrorMapper`. Never surface a stack trace in the UI.
- Warnings are errors, and StyleCop runs as part of the build. Three rules bite constantly:
  **SA1115** (no blank line between call arguments) and **SA1515** (a single-line comment needs a
  preceding blank line) — together they make comments inside an argument list impossible; hoist the
  value to a local and comment the local instead. And **SA1204** (static members before non-static):
  a private static helper added below the instance methods fails the build — reorder it, or drop
  `static` / inline it.
- Icons in XAML are **text glyphs** (`Text="&#x2605;"` in a `TextBlock`), not hand-drawn
  `PathIcon` geometry. A path whose coordinates do not match the control's viewbox renders as a
  half-visible smudge at 12-16 px, and nothing in the build catches it — the trap has already
  cost two rounds in `TrayPopupWindow.axaml` (the close x, the pin star). Vector icons come from
  the resource dictionary or not at all.
- Comment intent, not mechanics: the maintainers are identity admins as much as developers.

## Security conventions

This is a privileged-access tool; these are not style preferences.

- **Per-user install only.** No `HKLM`, no `Program Files`, no Windows service, no SYSTEM scheduled
  task. Autostart goes through `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` only.
- **Never log access, ID, or refresh tokens — and never log justification text**, which may contain
  incident detail. User identifiers in logs are limited to the OID; never UPN or mail. Ticket numbers
  are fine.
- **No hardcoded tenant or client IDs.** Placeholders live in
  `src/Entra-PIM-Manager.App.Avalonia/appsettings.json` (committed); real values live in
  `appsettings.local.json` next to it (gitignored) or in the per-user config under `%LocalAppData%`.
- **Delegated permissions only**, least privilege. The Graph scopes in that `appsettings.json` plus
  the ARM scope derived per cloud in `EntraCloudInfo.ResourceManagerScopes` (Azure Service
  Management → `user_impersonation`) are the whole surface — adding one is a decision, not a
  detail, and needs admin consent in every tenant. Graph and ARM scopes are never mixed in one
  token request.

## Platform facts

- Minimum OS: Windows 10 1809 / Windows Server 2019 — the WAM broker requires it.
- WAM redirect URI: `ms-appx-web://microsoft.aad.brokerplugin/{client-id}`, public client flows enabled.
- App Registrations are configured **per tenant** (`TenantAppRegistrations`: tenant id, client id,
  cloud, label) — a multi-tenant registration appears once per tenant it is used in; the list is
  also the tenant whitelist. The registration an enrollment uses is derived from (cloud, tenant) by
  `EntraPimManagerOptions.ClientIdFor` — never persisted per account. Consent is per-tenant.
  Pre-0.7.0 per-cloud client ids are folded in at startup by `LegacyRegistrationMigration`.
- Directory-role and PIM-for-Groups endpoints differ in casing and shape — see the
  `entra-pim-graph-api` skill rather than inferring symmetry.
- PIM for Azure Resources is an ARM API (`management.azure.com`, api-version `2020-10-01`), not
  Graph: PascalCase bodies, `PUT` under a client GUID, no validation-only mode, an unsatisfied
  authentication context comes back as HTTP 400. See the `azure-rbac-pim-arm-api` skill.
- `AvaloniaUseCompiledBindingsByDefault` is on: binding errors are build errors, not runtime surprises.

## Known gaps

Three are open: **releases are not code-signed** (every published build ships unsigned; app-control
environments block installs until a cert lands in CI), the unverified Velopack Desktop-shortcut
suppression, and the `.slnx` that SDK 8.0.x cannot parse. All are recorded with their evidence and
their "what makes the fix safe" in `docs/engineering-backlog.md` — read it there before touching
any of them, and do not "fix" them casually.

## Learning Loop

When a session reveals that a documented procedure was wrong or incomplete, that a command behaved
differently than a skill claims, or a non-obvious environment fact — persist it **in the same
session**: correct the affected skill or this file, add a backlog entry, or write a memory. Use the
`retro` skill (`.claude/skills/retro/`) for the end-of-session sweep and its placement rules.
A wrong doc is worse than no doc.

## Commit style

Conventional commits: `feat:`, `fix:`, `refactor:`, `docs:`, `test:`, `chore:`

Before committing a multi-file feature: run `/ponytail-review` on the diff, decide each finding with
the user, then run a correctness pass with `/code-review` — ponytail-review explicitly excludes bugs,
so neither pass substitutes for the other. Both are deliberately NOT part of `verify` — verify is a
deterministic pass/fail gate; the reviews are judgment passes on the final diff.

Pick the `/code-review` level by what the diff touches, not by its size. The deciding question:
what does a mistake here cost the user's tenant or their machine?

- **low** — Avalonia views, XAML, ViewModel wiring; refactors beyond what `-warnaserror`, StyleCop
  and compiled bindings already prove
- **medium** — read-only Graph / ARM call paths (listing eligibilities, policies, tenant info),
  parsing and error mapping, changes to services or models with many callers
- **high** — anything that mutates tenant state (activation, deactivation, schedule requests),
  MSAL / token cache / scope / claims-challenge code, persisted settings and migrations, the
  installer and autostart footprint (`HKCU\...\Run`, Velopack, per-user install paths)

Skip the correctness pass only for docs- or typo-level diffs where the build is the whole proof.
When a diff spans levels, the highest-touched level wins.
