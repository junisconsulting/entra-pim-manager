# Contributing to Entra PIM Manager

Thanks for your interest in contributing. This document covers how to build the project, the conventions we follow, and how pull requests are reviewed.

## License grant

By submitting a pull request to this repository, you agree to license your contribution under the [MIT License](LICENSE) — the same terms as the rest of the project. This is the standard GitHub "inbound=outbound" model; no separate Contributor License Agreement is required.

## Build & test

Requires the .NET 8 SDK on Windows.

```powershell
git clone <repo-url>
cd Entra PIM Manager
dotnet restore
dotnet build -c Release -warnaserror
dotnet test
```

The build enforces warnings-as-errors and runs StyleCop analyzers; both must pass before opening a PR.

To produce a Velopack installer locally, see [packaging/velopack/README.md](packaging/velopack/README.md).

## Code conventions

### Language

- **Code, identifiers, code comments**: English.
- **UI text** (Views, ViewModels, toasts, tray menu, tooltips): English.
- **Commit messages**: English, [Conventional Commits](https://www.conventionalcommits.org/) style (`feat:`, `fix:`, `chore:`, `docs:`, `refactor:`, `test:`).

### Architecture

```text
src/Entra-PIM-Manager.App.Avalonia  →  Avalonia views, ViewModels, tray   (UI only)
src/Entra-PIM-Manager.Core          →  Auth, Graph, models, services      (no UI deps)
src/Entra-PIM-Manager.Tests         →  xUnit, Moq                         (tests against Core only)
```

`Entra-PIM-Manager.Core` **must not** reference Avalonia, WPF, or any other UI toolkit. This is the layering boundary that keeps tests simple and the core logic UI-agnostic.

### Graph access

All Microsoft Graph calls go through the service layer (`PimRoleService`, `PimGroupService`, `PolicyService`). Do not call `HttpClient` directly against `graph.microsoft.com` — this would bypass our auth, retry, and telemetry handling.

### Async & cancellation

- All I/O-bound methods are async (`Task` / `Task<T>`). No `.Result`, no `.Wait()`, no `.GetAwaiter().GetResult()` outside of explicit sync-only contexts (`Main`, static constructors).
- All public async methods in `Entra-PIM-Manager.Core` accept `CancellationToken ct = default` as the last parameter. UI-triggered operations pass a token with a sensible timeout (e.g. 30 s for Graph calls).

### Nullable reference types

`<Nullable>enable</Nullable>` is set in every project. Do not introduce `#nullable disable` pragmas.

### Error handling

Graph errors are mapped to user-friendly messages via `PimErrorMapper`. Do not surface stack traces in the UI. Exceptions are kept in logs at DEBUG level.

## Security conventions

Entra PIM Manager is a privileged-access tool — please take these constraints seriously:

- **Per-user install only.** No writes to `HKLM`, `Program Files`, no Windows services, no scheduled tasks running as SYSTEM. Autostart goes via `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` only.
- **No hardcoded tenant or client IDs.** All identifiers come from `appsettings.json` (placeholder values, committed) and `appsettings.local.json` (real values, gitignored).
- **Never log access tokens, ID tokens, refresh tokens, or justification text.** Justifications may contain sensitive incident information.
- **User identifiers in logs** are limited to the OID (object ID); never UPN or mail in plaintext.
- Ticket numbers may be logged (they are not sensitive themselves).

## Tests

- Unit tests for `Entra-PIM-Manager.Core` use JSON fixtures for Graph responses (`src/Entra-PIM-Manager.Tests/Fixtures/`).
- Coverage target: **70 % lines for `Entra-PIM-Manager.Core`**. UI code is intentionally not covered — coverage of UI is theatre.
- Integration tests that hit a real tenant are marked `[Trait("Category","Integration")]` and do not run in CI.

## Pull request process

1. **Open an issue first** for non-trivial changes — this avoids wasted work if the change does not fit the project's direction.
2. **Branch from `main`** and keep the PR focused on a single concern.
3. **CI must be green** — build, tests, and the coverage gate on `Entra-PIM-Manager.Core`.
4. **Review** by a maintainer. Once approved, the maintainer merges (squash-merge by default).

## Out of scope for v1

Pull requests in these areas need discussion first — they are tracked as backlog rather than as accepted contributions:

- Approval workflows (approver view, pending requests)
- Azure Resource Roles (separate API under `/roleManagement/azureResources`)
- Renewal or extension of expiring assignments (`action: "selfExtend"`)
- Bulk activation of multiple roles in a single step
