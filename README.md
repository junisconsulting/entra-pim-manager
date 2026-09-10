# Entra PIM Manager

A Windows tray application for activating Microsoft Entra Privileged Identity Management (PIM) eligibilities — Directory Roles, Group Memberships and Azure resource roles — from one place, across multiple tenants, without UAC, admin rights, or service installation.

<p>
  <img src="docs/screenshot1.png" alt="Entra PIM Manager screenshot 1" width="32%" />
  <img src="docs/screenshot2.png" alt="Entra PIM Manager screenshot 2" width="32%" />
  <img src="docs/screenshot3.png" alt="Entra PIM Manager screenshot 3" width="32%" />
</p>

## Using it

New to the app? **[User guide](docs/user-guide.md)** — English · **[Anleitung](docs/user-guide.de.md)** — Deutsch.

Covers signing in, activating a role, choosing Azure scopes, ending a role early, and what to do
when something fails. The sections below are for the person setting it up.

## Features

- One-click activation of PIM eligibilities from the system tray — Entra directory roles, PIM for Groups, and Azure resource roles (Azure RBAC at management-group, subscription, resource-group or resource scope)
- Narrowed Azure activations — a role held on a management group asks where it applies, with nothing preselected: pick single subscriptions, management groups, or deliberately the entire scope. A picked set can be saved under a name and starred, so it sits with your pinned roles for next time
- Multi-tenant: sign in with multiple admin accounts; eligibilities and active assignments are grouped per tenant
- One App Registration entry per tenant — a multi-tenant registration reused across tenants, a customer's own single-tenant registration, or a mix
- Multi-cloud: Global and Entra China (21Vianet) side by side, each with its own App Registration
- WAM-broker authentication (no embedded WebView, no in-app password prompts), with a device-code fallback for tenants whose federated IdP forces seamless SSO onto the wrong account
- Activation form with justification, ticket reference, and a duration slider in 0.5 h steps (bounded by the per-role policy maximum)
- Live watchdog — the list refreshes automatically when assignments are activated, deactivated, or expire
- Search across role name, type, tenant and Azure scope — a subscription name finds its roles
- Favorites for recurring justifications
- Drag-and-drop reordering of accounts in Settings, and a short alias per account ("EADM") in place of a long UPN
- Per-user install to `%LocalAppData%\Entra-PIM-Manager\` — no UAC, no HKLM, no Windows service
- Optional Windows autostart (enabled by default on first install, toggleable in Settings)
- Velopack-based auto-update

## Requirements

- Windows 10 1809+ or Windows Server 2019+ (required for the WAM broker)
- An Entra tenant with PIM eligibilities assigned to the signed-in user
- A configured Entra App Registration (see [Configure](#configure))

## Install

Download the latest installer from the [Releases](../../releases) page and run it. The installer is per-user — no UAC prompt — and places the app under `%LocalAppData%\Entra-PIM-Manager\`.

When a new release is published, the app checks GitHub once a day, then prompts you to download and install it — you choose whether to restart now or apply on the next launch. Toggle this under **Settings → Updates**, where **Check for updates** also runs a check on the spot and tells you what it found. A release is only offered once it is 72 hours old: builds are unsigned, and security tools block a binary they have not classified yet, so a fresher one would install and then fail to start.

Uninstalling removes everything: the app, the autostart entry, and your per-user data under `%LocalAppData%\junis\Entra-PIM-Manager` — settings, signed-in accounts and the cached tokens. Updates leave all of that untouched.

## Configure

Before first use, an Entra App Registration must be created once (an admin task). Its client id is then entered into the app — no file editing required.

Setup steps: [docs/app-registration-setup.md](docs/app-registration-setup.md).

In short:

1. Create an App Registration in your Entra portal — multi-tenant if it should serve several tenants, single-tenant if it lives in exactly one.
2. Add the WAM redirect URI `ms-appx-web://microsoft.aad.brokerplugin/{client-id}` and enable public client flows.
3. Grant delegated Graph permissions: `User.Read`, `RoleEligibilitySchedule.Read.Directory`, `RoleAssignmentSchedule.ReadWrite.Directory`, `RoleManagementPolicy.Read.Directory`, `RoleManagementPolicy.Read.AzureADGroup`, `PrivilegedAccess.ReadWrite.AzureADGroup`, `Group.Read.All` — plus the delegated permission **Azure Service Management → `user_impersonation`** for Azure resource roles (PIM for Azure Resources goes through Azure Resource Manager, not Graph).
4. Grant admin consent in every tenant where Entra PIM Manager will be used.
5. Launch the app, open **Settings → TENANTS**, and add one entry per tenant: tenant id, client id, cloud, optional label. A multi-tenant registration is listed once per tenant with the same client id; only listed tenants can be signed in to. Entries are saved to your per-user config at `%LocalAppData%\junis\Entra-PIM-Manager\appsettings.local.json` and applied on the next restart — the shipped `appsettings.json` only carries the scopes.

> **Entra China (21Vianet)?** National clouds are physically isolated instances of Entra, so a Global App Registration does not exist there — a Global client id sent to `login.partner.microsoftonline.cn` fails with `AADSTS700016`. Repeat steps 1–4 in [portal.azure.cn](https://portal.azure.cn) and add that tenant's entry with cloud **Entra China**. Both clouds then work side by side; "Add account…" lists every entry under "Sign in with".
>
> **Upgrading from 0.6.x?** The per-cloud client ids are folded into per-tenant entries automatically at first start (one per enrolled tenant, no re-sign-in). A client id without any enrolled tenant cannot be migrated and has to be added again with its tenant id — see [docs/app-registration-setup.md §8](docs/app-registration-setup.md#8-upgrading-from-06x).
>
> Running from source instead of an installer? Copy `src/Entra-PIM-Manager.App.Avalonia/appsettings.local.json.sample` to `appsettings.local.json` and fill in `TenantAppRegistrations` — a developer convenience that avoids retyping the ids in the UI on every run.

## Enterprise deployment

Rolling out to a team? Nobody has to type a client id. Two scripts:

```powershell
# Admin, once per tenant: creates the App Registration, grants consent,
# and prints the endpoint command with the ids filled in
./scripts/create-app-registration.ps1

# On each endpoint, in the user's context: installs and configures
./scripts/install-entra-pim-manager.ps1 -SetupExe .\Entra-PIM-Manager-win-Setup.exe `
    -TenantId <guid> -ClientId <guid> -Label "Contoso" -TicketSystem "ServiceNow"
```

Installation and configuration are two separate steps — a silent install never starts the app, so the configuration is a second call that writes the entry and exits at once, which is what lets an Intune install command return. Deploy in the **user's** context; install and configuration are both per-user.

Details, arguments and exit codes: [docs/unattended-deployment.md](docs/unattended-deployment.md).

## Build from source

Requires the .NET 8 SDK on Windows.

```powershell
git clone <repo-url>
cd Entra PIM Manager
dotnet restore
dotnet build -c Release -warnaserror
dotnet test
```

To produce a Velopack installer, see [packaging/velopack/README.md](packaging/velopack/README.md).

## Architecture

```text
src/Entra-PIM-Manager.App.Avalonia  →  Avalonia views, ViewModels, tray   (UI only)
src/Entra-PIM-Manager.Core          →  Auth, Graph, models, services      (no UI deps)
src/Entra-PIM-Manager.Tests         →  xUnit, Moq                         (tests against Core only)
```

`Entra-PIM-Manager.Core` does not reference any UI toolkit — that's the layering boundary that keeps tests simple.

## License

MIT — see [LICENSE](LICENSE).

## Contributing

Contributions are welcome — see [CONTRIBUTING.md](CONTRIBUTING.md).

## Security

Found a vulnerability? Please report it privately — see [SECURITY.md](SECURITY.md).
