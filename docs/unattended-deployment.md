# Unattended deployment

> How to roll Entra PIM Manager out to a team without anyone having to type a client id.
> An admin runs one script once per tenant; every endpoint then gets two commands.

The interactive path — install, then **Settings → TENANTS** — asks each user for a tenant id and
a client id. Nobody outside the identity team can act on those. This page replaces that step with
a command line.

## 1. Create the App Registration (admin, once per tenant)

```powershell
pwsh ./scripts/create-app-registration.ps1
```

The script does steps 1–4 of [app-registration-setup.md](app-registration-setup.md): it creates the
registration, adds the WAM broker redirect URI, enables public client flows, requests the delegated
Graph permissions plus Azure Service Management `user_impersonation`, and grants admin consent in
the tenant you sign in to. It needs the `Microsoft.Graph.Applications` module and an admin who may
create applications and grant consent.

Useful parameters:

| Parameter | Default | Use |
| --- | --- | --- |
| `-SignInAudience` | `AzureADMyOrg` | `AzureADMultipleOrgs` for one registration serving several tenants |
| `-Cloud` | `Global` | `China` for Entra China (21Vianet) |
| `-Label` | — | Display name in the app's sign-in picker, e.g. the customer's name |

It prints the two commands below with the real ids filled in. For a multi-tenant registration it
also prints the consent URL each **further** tenant's admin has to open — that step is interactive
by nature and cannot be scripted from outside the tenant.

## 2. Deploy to the endpoints

```powershell
./install-entra-pim-manager.ps1 -SetupExe .\Entra-PIM-Manager-win-Setup.exe `
    -TenantId <guid> -ClientId <guid>
```

`scripts/install-entra-pim-manager.ps1` wraps both steps, validates the ids before touching
anything, maps the exit code to a readable error, and — the part worth having — verifies that the
entry actually reached `appsettings.local.json` instead of trusting the exit code.

If your deployment tool cannot run a script, the two commands it wraps are:

```text
Entra-PIM-Manager-win-Setup.exe --silent
%LocalAppData%\Entra-PIM-Manager\current\Entra-PIM-Manager.exe --tenant-id <guid> --client-id <guid>
```

`current\` is the executable Velopack's own install hook invokes, and Velopack replaces its
contents in place on update — so the path stays valid. The launcher stub one directory up
(`…\Entra-PIM-Manager\Entra-PIM-Manager.exe`) points at the same binary, but whether it forwards
arguments is unverified; the script falls back to it only when `current\` is missing.

The first command installs silently. The second writes the tenant entry into the per-user config
and **exits immediately** — it never opens a window. That matters: Intune waits for an install
command to return, and a tray app would never return.

> **These are two separate commands on purpose.** `Setup.exe` does not accept `--tenant-id` or
> `--client-id`; it only knows `--silent`, `--verbose`, `--log` and `--installto`. Passing them to
> the installer configures nothing and reports no error. Velopack's own `--` passthrough is no help
> either: a silent install runs the install hook and finishes without ever starting the app, so
> arguments meant for it are never used.

At the next logon the app starts through the autostart entry the installer set, already configured.
The user lands on "Add account" instead of the configuration prompt.

### Arguments

| Argument | Script parameter | Required | Notes |
| --- | --- | --- | --- |
| `--tenant-id <guid>` | `-TenantId` | yes | Directory (tenant) id |
| `--client-id <guid>` | `-ClientId` | yes | Application (client) id |
| `--cloud <Global\|China>` | `-Cloud` | no | Defaults to `Global` |
| `--label <text>` | `-Label` | no | Tenant alias in the sign-in picker and tenant list |
| `--ticket-system <text>` | `-TicketSystem` | no | e.g. `ServiceNow`, prefilled into the activation form |

Run it once per tenant to configure several. An entry for a tenant that is already configured is
replaced, not duplicated.

The first four go into `appsettings.local.json` and take effect at the next app start. The ticket
system goes into `settings.json` instead — it is workflow rather than auth configuration, and it
applies without a restart.

### Exit codes

| Code | Meaning |
| --- | --- |
| `0` | Registration written |
| `1` | Arguments missing or not valid GUIDs |
| `2` | Configuration file could not be read or written |

The app is a GUI executable and has no console, so the exit code is the only feedback — nothing is
printed even when run from a terminal. Both ids are validated before anything is written: a typo
fails with code `1` and leaves the configuration untouched.

## Two things that will bite you

**Deploy in the user's context, not as SYSTEM.** The install and the configuration are both
per-user (`%LocalAppData%\junis\Entra-PIM-Manager`). A deployment running as SYSTEM writes into the
SYSTEM profile and the user sees an unconfigured app. In Intune this is a Win32 app with install
behaviour **User**.

**Keep the tenant list in one file.** .NET configuration overlays arrays index by index. If a
machine also has an `appsettings.local.json` next to the executable — a developer convenience when
running from source — its entries merge field by field into the per-user list and produce
registrations nobody configured.

## Before you roll this out

Releases are **not code-signed** yet. In managed environments SmartScreen and Defender ASR rules
can block a freshly published unsigned installer until it builds reputation. See the entry in
[engineering-backlog.md](engineering-backlog.md) before planning a wide rollout.
