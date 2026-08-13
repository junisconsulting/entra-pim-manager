# Entra PIM Manager

A Windows tray application for activating Microsoft Entra Privileged Identity Management (PIM) eligibilities — Directory Roles and Group Memberships — from one place, across multiple tenants, without UAC, admin rights, or service installation.

<p>
  <img src="docs/screenshot1.png" alt="Entra PIM Manager screenshot 1" width="32%" />
  <img src="docs/screenshot2.png" alt="Entra PIM Manager screenshot 2" width="32%" />
  <img src="docs/screenshot3.png" alt="Entra PIM Manager screenshot 3" width="32%" />
</p>

## Features

- One-click activation of Entra PIM eligibilities from the system tray
- Multi-tenant: sign in with multiple admin accounts; eligibilities and active assignments are grouped per tenant
- Multi-cloud: Global and Entra China (21Vianet) side by side, each with its own App Registration
- WAM-broker authentication (no embedded WebView, no in-app password prompts), with a device-code fallback for tenants whose federated IdP forces seamless SSO onto the wrong account
- Activation form with justification, ticket reference, and a duration slider in 0.5 h steps (bounded by the per-role policy maximum)
- Live watchdog — the list refreshes automatically when assignments are activated, deactivated, or expire
- Favorites for recurring justifications
- Drag-and-drop reordering of accounts in Settings
- Per-user install to `%LocalAppData%\Programs\Entra-PIM-Manager\` — no UAC, no HKLM, no Windows service
- Optional Windows autostart (enabled by default on first install, toggleable in Settings)
- Velopack-based auto-update

## Requirements

- Windows 10 1809+ or Windows Server 2019+ (required for the WAM broker)
- An Entra tenant with PIM eligibilities assigned to the signed-in user
- A configured Entra App Registration (see [Configure](#configure))

## Install

Download the latest installer from the [Releases](../../releases) page and run it. The installer is per-user — no UAC prompt — and places the app under `%LocalAppData%\Programs\Entra-PIM-Manager\`.

When a new release is published, the app checks GitHub once a day, then prompts you to download and install it — you choose whether to restart now or apply on the next launch. Toggle this under **Settings → Updates**.

## Configure

Before first use, an Entra App Registration must be created once (an admin task). Its client id is then entered into the app — no file editing required.

Setup steps: [docs/app-registration-setup.md](docs/app-registration-setup.md).

In short:

1. Create a multi-tenant App Registration in your Entra portal.
2. Add the WAM redirect URI `ms-appx-web://microsoft.aad.brokerplugin/{client-id}` and enable public client flows.
3. Grant delegated Graph permissions: `User.Read`, `RoleEligibilitySchedule.Read.Directory`, `RoleAssignmentSchedule.ReadWrite.Directory`, `RoleManagementPolicy.Read.Directory`, `PrivilegedAccess.ReadWrite.AzureADGroup`, `Group.Read.All`.
4. Grant admin consent in every tenant where Entra PIM Manager will be used.
5. Launch the app, open **Settings → APP REGISTRATION**, and paste the client id into the row for its cloud. It is saved to your per-user config at `%LocalAppData%\junis\Entra-PIM-Manager\appsettings.local.json` and applied on the next restart — the shipped `appsettings.json` only carries a placeholder.

> **Entra China (21Vianet)?** National clouds are physically isolated instances of Entra, so a Global App Registration does not exist there — a Global client id sent to `login.partner.microsoftonline.cn` fails with `AADSTS700016`. Repeat steps 1–4 in [portal.azure.cn](https://portal.azure.cn) and enter that client id in the **Entra China** row. Both clouds then work side by side; the cloud picker appears in "Add account…" as soon as more than one is configured.
>
> Running from source instead of an installer? Copy `src/Entra-PIM-Manager.App.Avalonia/appsettings.local.json.sample` to `appsettings.local.json` and fill in `AppRegistrations` — a developer convenience that avoids retyping the ids in the UI on every run.

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
