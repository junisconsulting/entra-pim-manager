# Unattended deployment

> How to roll Entra PIM Manager out to a team without anyone having to type a client id.
> An admin consents once to the junis-hosted registration, or runs one script once per tenant for
> a self-hosted one; every endpoint then gets two commands.

The interactive path — install, then **Settings → TENANTS** — asks each user for a tenant id and,
for self-hosted, a client id. Nobody outside the identity team can act on those. This page replaces
that step with a command line.

## 1. Create the App Registration (admin, once per tenant)

**junis-hosted (Global cloud only): skip this step.** An admin of the tenant consents once through
the link in [app-registration-setup.md](app-registration-setup.md#junis-hosted-consent-only), and
the endpoint command in step 2 leaves out `-ClientId`. The rest of this step is self-hosted.

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
# junis-hosted (Global cloud only)
./install-entra-pim-manager.ps1 -SetupExe .\Entra-PIM-Manager-win-Setup.exe -TenantId <guid>

# self-hosted
./install-entra-pim-manager.ps1 -SetupExe .\Entra-PIM-Manager-win-Setup.exe `
    -TenantId <guid> -ClientId <guid>
```

Without `-ClientId` the tenant is junis-hosted: the script reads `JunisHostedClientId` from the
installed `current\appsettings.json` and passes it on as the client id. `-Cloud China` without
`-ClientId` is refused before anything is installed. An installed version that ships no junis
client id stops the script with "This installation does not offer junis-hosted yet; pass -ClientId
or install a newer version."
Where the organization turns off junis-hosted, always pass `-ClientId`
([Turn off junis-hosted](#turn-off-junis-hosted)).

`scripts/install-entra-pim-manager.ps1` wraps both steps, validates the ids before touching
anything, maps the exit code to a readable error, and — the part worth having — verifies that the
entry actually reached `appsettings.local.json` instead of trusting the exit code.

If your deployment tool cannot run a script, the two commands it wraps are:

```text
Entra-PIM-Manager-win-Setup.exe --silent
%LocalAppData%\Entra-PIM-Manager\current\Entra-PIM-Manager.exe --tenant-id <guid> --client-id {junis-client-id}
```

The app's own `--client-id` stays required, also for junis-hosted: pass the junis client id
published in [SECURITY.md](../SECURITY.md#the-junis-hosted-app-registration). For self-hosted, pass
your own client id instead.

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
| `--client-id <guid>` | `-ClientId` | app: yes · script: no | Application (client) id. The script without `-ClientId` passes the junis client id (junis-hosted, Global cloud only; [unless turned off](#turn-off-junis-hosted)) |
| `--cloud <Global\|China>` | `-Cloud` | no | Defaults to `Global` |
| `--label <text>` | `-Label` | no | Tenant alias in the sign-in picker and tenant list |
| `--ticket-system <text>` | `-TicketSystem` | no | e.g. `ServiceNow`, prefilled into the activation form |
| — | `-LogoPath <file.png>` | no | Script only, see [Your organization's logo](#your-organizations-logo) |

Run it once per tenant to configure several. An entry for a tenant that is already configured is
replaced, not duplicated.

That is also how a rollout switches a tenant: re-running the script without `-ClientId` makes it
junis-hosted (unless [junis-hosted is turned off](#turn-off-junis-hosted)), re-running it with
`-ClientId` makes it self-hosted again. The script cannot remove accounts, so have users remove the
tenant's accounts in the app first. Otherwise those accounts show "The saved sign-in for this
account is missing", and the old registration's tokens stay with Windows. Users who already see the
message sign in again with **+ Add account** on the tenant's card, through the new registration;
they do not have to remove the account first.

The first four go into `appsettings.local.json` and take effect at the next app start. The ticket
system goes into `settings.json` instead — it is workflow rather than auth configuration — and also
takes effect at the next start. A running instance keeps the settings it loaded and drops the entry
the next time it saves its own, which is one reason the script restarts a running instance; with
`-NoStart` it does not, so configure the ticket system while the app is not running.

### Exit codes

| Code | Meaning |
| --- | --- |
| `0` | Registration written |
| `1` | Arguments missing or not valid GUIDs |
| `2` | Configuration file could not be read or written |

The app is a GUI executable and has no console, so the exit code is the only feedback — nothing is
printed even when run from a terminal. Both ids are validated before anything is written: a typo
fails with code `1` and leaves the configuration untouched.

### Your organization's logo

`-LogoPath <file.png>` puts your logo at the top right of the panel. The script
copies it to `%LocalAppData%\junis\Entra-PIM-Manager\logo.png`, and the app reads it at startup.
The path is read on the device, in the user's context: a file inside your own Intune package
(`.\logo.png`, see [step 3](#3-intune-the-ready-made-win32-app)), or a share the user can read
(`\\server\branding\logo.png`), which works with the release package as it is — not yet tested in
Intune.

- **PNG only**, ideally with a transparent background. The panel has a light and a dark theme, so
  pick a version that reads on both — a dark wordmark disappears on the dark one.
- It is drawn 32 px tall and at most 140 px wide, so a logo wider than about 4.4:1 comes out
  smaller. 96 px of height keeps it sharp up to 300 % display scaling. Crop it tightly: a
  transparent margin counts toward both limits, so the logo comes out smaller and no longer lines
  up with the edge.
- A run without the parameter leaves an existing logo in place. Users can also choose or remove
  one themselves under **Settings → APPEARANCE → Logo**, which writes the same file.
- A file the app cannot decode shows no logo, and the reason is in the log.

### Lock the tenant list

On managed devices you can hide tenant management from users. Set this machine policy value, and
the app no longer offers **Add a tenant**, the switch between junis-hosted and self-hosted, or
**Remove tenant**. Users keep their accounts, labels and ticket systems, and see
"Tenants are managed by your organization." in Settings
([ADR 0005](adr/0005-tenant-lockdown-machine-policy.md)).

| Key | Value | Type | Data |
| --- | --- | --- | --- |
| `HKLM\SOFTWARE\Policies\junis\Entra-PIM-Manager` | `TenantLockdown` | `REG_DWORD` | `1` locks, `0` or absent does not |

Any other type, a `REG_SZ` `"1"` for example, is ignored and logged as a warning. The app reads the
value at startup, so a change takes effect at the next app start. The app only reads this key and
never writes it; users cannot change it without admin rights.

The tenant itself still comes from the install script in the user's context. The lock is a separate
**device** setting and needs admin rights, so it cannot go into the Win32 app from
[step 3](#3-intune-the-ready-made-win32-app), which runs as the user. In Intune, add it as a
**platform script** (Devices → Scripts and remediations → Platform scripts) with **Run this script
using the logged on credentials: No**:

```powershell
$key = 'HKLM:\SOFTWARE\Policies\junis\Entra-PIM-Manager'
if (-not (Test-Path $key)) { New-Item -Path $key -Force | Out-Null }
New-ItemProperty -Path $key -Name TenantLockdown -PropertyType DWord -Value 1 -Force | Out-Null
```

It works from the 32-bit PowerShell Intune starts by default: Windows does not redirect
`SOFTWARE\Policies` for 32-bit processes. A platform script runs once and does not undo itself when
you unassign it; to lift the lock, assign a script that sets `0`. With Group Policy, a registry
preference item (Computer Configuration → Preferences → Windows Settings → Registry) sets the same
value.

> **The lock is not a security boundary.** It keeps the app's own settings tidy. It does not stop
> anyone from signing in to another tenant, in a browser or with another tool. To keep users on
> managed devices out of foreign tenants, use
> [tenant restrictions v2](https://learn.microsoft.com/entra/external-id/tenant-restrictions-v2)
> in your cross-tenant access settings.

### Turn off junis-hosted

If your organization requires its own App Registration but users may still add tenants
themselves, turn off junis-hosted instead of locking the tenant list. Set this machine policy
value, and the add form and the gear on a tenant's card ask only for a client id, with no choice
between junis-hosted and self-hosted, and the cards show no hosting chip
([ADR 0006](adr/0006-disable-junis-hosted-machine-policy.md)).

| Key | Value | Type | Data |
| --- | --- | --- | --- |
| `HKLM\SOFTWARE\Policies\junis\Entra-PIM-Manager` | `DisableJunisHosted` | `REG_DWORD` | `1` turns it off, `0` or absent does not |

It follows the same rules as `TenantLockdown`: any other type is ignored and logged as a warning,
the app reads the value at startup, and both values can be set together. Deploy it the same way,
as an Intune platform script in system context:

```powershell
$key = 'HKLM:\SOFTWARE\Policies\junis\Entra-PIM-Manager'
if (-not (Test-Path $key)) { New-Item -Path $key -Force | Out-Null }
New-ItemProperty -Path $key -Name DisableJunisHosted -PropertyType DWord -Value 1 -Force | Out-Null
```

With Group Policy, a registry preference item sets the same value.

Pass `-ClientId` in every install command: without it, the script still writes the junis client
id. The app then treats such an entry as self-hosted: it keeps signing in if your tenant has
consented to the junis app, but its errors and its "Verified" status describe a self-hosted
registration. The same goes for entries that were junis-hosted before you set the policy; switch
them to your own client id under the gear.

> **Turning off junis-hosted is not a security boundary either.** It only stops the app from
> offering it. Whether the junis registration can sign in to your tenant is decided by your
> tenant's consent; see
> [Leaving junis-hosted](app-registration-setup.md#leaving-junis-hosted).

## 3. Intune: the ready-made Win32 app

Every release also carries `Entra-PIM-Manager-win-Setup.intunewin`: the same Setup.exe, packed for
Intune together with `install-entra-pim-manager.ps1` and `uninstall-entra-pim-manager.cmd`. It holds
no tenant id and no self-hosted client id — those go into the install command — so one package
serves every tenant. Add it in Intune as a **Windows app (Win32)** and enter:

| Field | Value |
| --- | --- |
| Install command, junis-hosted | `powershell.exe -NoProfile -ExecutionPolicy Bypass -File install-entra-pim-manager.ps1 -SetupExe .\Entra-PIM-Manager-win-Setup.exe -TenantId <guid> -NoStart` |
| Install command, self-hosted | `powershell.exe -NoProfile -ExecutionPolicy Bypass -File install-entra-pim-manager.ps1 -SetupExe .\Entra-PIM-Manager-win-Setup.exe -TenantId <guid> -ClientId <guid> -NoStart` |
| Uninstall command | `uninstall-entra-pim-manager.cmd` |
| Install behavior | **User** |
| Return codes | the defaults — installer and uninstaller exit `0` on success and `1` on failure |
| Requirements | Operating system architecture **x64** only · Minimum operating system **Windows 10 1809** (the WAM broker needs it). ARM64 is left out on purpose: it would run the x64 build under emulation, which nobody has tested |
| Detection rule | **Registry** · key `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Uninstall\Entra-PIM-Manager` · value `DisplayVersion` · **Version comparison** · **Greater than or equal to** the release you upload, e.g. `0.12.0` |
| Logo | [`app-registration-logo.png`](../src/Entra-PIM-Manager.App.Avalonia/Assets/app-registration-logo.png) from this repository |

`-Cloud`, `-Label` and `-TicketSystem` from the [arguments table](#arguments) work in the install
command too. To install without configuring a tenant, make the install command
`Entra-PIM-Manager-win-Setup.exe --silent`; users then add their tenant under **Settings →
TENANTS**.

The release package carries no logo. For [yours](#your-organizations-logo), build a package of your
own with `packaging/intune/build.ps1 -LogoPath <file.png>` and add `-LogoPath .\logo.png` to the
install command. That package does not need rebuilding for later releases: Intune only does the
first install, and updates come from the app itself. For the same reason it never reaches devices
that already run the app — Intune counts them as detected, the limit described below for a second
tenant. There, run the install script with `-LogoPath` once, copy `logo.png` into
`%LocalAppData%\junis\Entra-PIM-Manager` with a script of your own — without the read-only
attribute, or neither **Remove** nor the uninstall can delete it — or let users choose it in
Settings.

Why these values, and not the obvious alternatives:

- **`-NoStart`.** Whether Intune waits for a tray app the script started is unverified, and a wait
  would run into the install timeout. The app comes up at the next logon through autostart, or
  right away from the Start menu.
- **Greater than or equal, never equals.** The app updates itself, and every update rewrites
  `DisplayVersion`. With "equals" the first update makes the app look missing, and Intune's
  reinstall silently *downgrades* it. Intune does the first install; updates stay with the app.
- **Version comparison, not string or integer.** `DisplayVersion` is a string with dots
  (`0.12.0`). String comparison only offers equals and not equal, and integer comparison needs a
  DWORD. Version comparison reads the string as a version and offers greater than or equal.
- **Registry, not a file rule.** A file rule on `%LOCALAPPDATA%` is not reliably resolved to the
  signed-in user.
- **The uninstall is a script** because Intune does not expand environment variables in the
  uninstall command, and `Update.exe` lives under `%LOCALAPPDATA%`. It removes the app **and** its
  data: accounts, token cache, configuration.

Two consequences to plan for:

- **Detection proves "installed", not "configured".** Where the app is already installed at the
  uploaded version or newer — by hand, or through another Intune app — Intune counts it as detected
  and never runs the install command, so the tenant is not configured there. (An *older* install is
  not detected: Intune installs over it, which configures the tenant but restarts the app, see the
  next point.) One Intune app configures one tenant on fresh
  installs; a second tenant on the same devices needs the script run separately, or **Settings →
  TENANTS**.
- **An Intune reinstall or upgrade ends the running app.** `Setup.exe --silent` installs over an
  existing install without asking — a downgrade included — and stops a running tray app first.
  With `-NoStart` it comes back at the next logon.

The app is signed, the scripts are not. Where a GPO enforces the `AllSigned` execution policy, it overrides
`-ExecutionPolicy Bypass` and the install fails. Use the two raw commands from step 2 instead, in one
install command — not yet tested in Intune:

```text
cmd.exe /c "start "" /wait Entra-PIM-Manager-win-Setup.exe --silent && start "" /wait "%LOCALAPPDATA%\Entra-PIM-Manager\current\Entra-PIM-Manager.exe" --tenant-id <guid> --client-id {junis-client-id}"
```

As in step 2, `--client-id` is the junis client id for junis-hosted and your own for self-hosted.
`cmd` expands `%LOCALAPPDATA%` itself. `start /wait` is not decoration: both executables are GUI
programs, `cmd /c` does not reliably wait for those, and without it the second command would run
before the install has finished. What this line loses is the script's check that the entry actually
landed in `appsettings.local.json`: a `0` here means only that the app accepted the arguments. Check
that file on the first device by hand. It also sets no logo — the app itself takes no logo argument —
so place `logo.png` in `%LocalAppData%\junis\Entra-PIM-Manager` another way, or let users choose it
in Settings.

To build the package from a local Setup.exe, run `packaging/intune/build.ps1` on Windows — it is the
same script the release uses.

## Two things that will bite you

**Deploy in the user's context, not as SYSTEM.** The install and the configuration are both
per-user (`%LocalAppData%\junis\Entra-PIM-Manager`). A deployment running as SYSTEM writes into the
SYSTEM profile and the user sees an unconfigured app. In Intune this is a Win32 app with install
behaviour **User**.

**Keep the tenant list in one file.** .NET configuration overlays arrays index by index. If a
machine also has an `appsettings.local.json` next to the executable — a developer convenience when
running from source — its entries merge field by field into the per-user list and produce
registrations nobody configured.

## Pooled hosts with FSLogix

On pooled session hosts (Azure Virtual Desktop, Remote Desktop Services) with FSLogix profile
containers, the app and its accounts follow the user from host to host. The Windows sign-in
broker's state does not: FSLogix deliberately leaves the broker's data out of the container
([nonroamable identity data](https://learn.microsoft.com/fslogix/concepts-container-types#profile-container)),
and broker tokens are bound to the device. An admin account that is not the Windows sign-in account
therefore needs **one sign-in per logon**. The app keeps the account, the broker no longer knows it,
and the first time the user opens the panel after a logon, the Windows sign-in window appears. Seen
in a field test on hybrid-joined pooled hosts on 2026-11-12.

The app cannot avoid this, and FSLogix's `RoamIdentity` is no way around it: Microsoft recommends
leaving it off and rules it out for Microsoft Entra joined and Intune-managed devices
([RoamIdentity](https://learn.microsoft.com/fslogix/reference-configuration-settings#roamidentity)).
The app does not open the sign-in window on its own after a logon, because an unprompted sign-in
window in a privileged-access tool looks like phishing. Until the user opens the panel once, the
tray icon is red with "sign-in needed"; if other accounts still work, it keeps their state and its
tooltip says how many accounts need a sign-in.

**Microsoft Entra joined hosts need `LoadCredKeyFromProfile`.** Without it, DPAPI cannot decrypt
data from an earlier logon in an FSLogix profile, even on the same host, so the app's token cache
comes up empty and every account needs **+ Add account** after each logon. Set
`LoadCredKeyFromProfile` = 1 (DWORD under `HKLM\Software\Policies\Microsoft\AzureADAccount`) or,
through Intune, `RemoteDesktop/LoadAadCredKeyFromProfile` = 1
([Policy CSP](https://learn.microsoft.com/windows/client-management/mdm/policy-csp-remotedesktop#loadaadcredkeyfromprofile),
[FSLogix with Microsoft Entra ID](https://learn.microsoft.com/fslogix/how-to-configure-profile-container-entra-id-hybrid#configure-your-local-windows-device)).
Data encrypted before the policy stays unreadable, so each user re-adds their account once.
Confirmed on a Microsoft Entra joined pooled host on 2026-11-13 (issue #13).

For an admin account that *is* the Windows sign-in account, on Microsoft Entra joined hosts, the
app recovers a lost sign-in by itself. It asks Windows silently for the session's own account, but
only after checking that the session user is that very account: on these hosts the user's SID
carries the Entra object id. It never opens a window for it, and an attempt that fails is not
repeated until the app restarts. On hybrid-joined hosts with an Active Directory sign-in the app
cannot check this locally and does not try. Tested on a Microsoft Entra joined notebook; whether
Windows still issues the token on a pooled host whose DPAPI keys are lost at every logon is not
yet known.

## Before you roll this out

From 1.0.0 on, releases are signed by junis GmbH ([ADR 0003](adr/0003-code-signing.md)). Microsoft
documents that the Defender ASR rule "Use advanced protection against ransomware" does not block
validly signed files. The rule "Block executable files from running unless they meet a prevalence,
age, or trusted list criterion" has no such exemption, so a freshly published release can still be
blocked there until it builds reputation. The signing certificate changes every few days, so a
Defender certificate indicator does not last either: use the path exclusions below. For App
Control, test a publisher rule against a signed release (`New-CIPolicy -Level Publisher`) before
relying on it.

**How a block shows up in Intune.** The install fails with exit code 1 (`0x80070001`), and no
`Entra-PIM-Manager` key appears under `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall`.
Intune discards the script's output, so the reason is not in its logs. It is in the event log
**Microsoft-Windows-PowerShell/Operational**, event 4100: `Start-Process` failed with "Access is
denied". Defender records the block itself as event 1121 in
**Microsoft-Windows-Windows Defender/Operational**, naming the rule and the file. Seen in the field
with the ASR rule "Block executable files from running unless they meet a prevalence, age, or
trusted list criterion" (`01443614-cd74-433a-b99e-2ecdc07bfc25`) in block mode.

**What to exclude.** Add these as per-rule exclusions to the rule that blocked. A hash rule would
have to be renewed with every release, and the Velopack stub and `Update.exe` are no exception:
vpk patches both per app, so their hashes differ from the stock Velopack binaries. Exclusions do
not take user environment variables, hence `C:\Users\*`.

| Path | Started by |
| --- | --- |
| `C:\Windows\IMECache\*\Entra-PIM-Manager-win-Setup.exe` | the Intune install |
| `C:\Users\*\AppData\Local\Entra-PIM-Manager\*\Entra-PIM-Manager.exe` | the install script and the install hook (`current\`) |
| `C:\Users\*\AppData\Local\Entra-PIM-Manager\Entra-PIM-Manager.exe` | autostart and the Start menu (the stub) |
| `C:\Users\*\AppData\Local\Entra-PIM-Manager\Update.exe` | self-update and uninstall |

The first two are field-tested: with exactly those, the install went through. The other two follow
from the install layout and are listed separately because it is unconfirmed whether `*\` also
matches no folder at all.
