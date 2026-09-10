# Entra PIM Manager — user guide

> *[Diese Anleitung auf Deutsch](user-guide.de.md)*
>
> For the people who use the app. Setting it up for a tenant is an admin task and lives in
> [app-registration-setup.md](app-registration-setup.md) and
> [unattended-deployment.md](unattended-deployment.md).

## What this app is for

Your admin roles are not switched on all the time. Microsoft Entra Privileged Identity Management
(PIM) hands them out on request, for a few hours at a time, so an account that is compromised while
you are not working is not an admin account. Turning a role on is called **activating** it.

Doing that in the Azure portal means opening a browser, finding the PIM blade, and clicking through
several screens. This app does the same thing from an icon next to your clock.

**It cannot give you anything you do not already have.** It activates roles you are *eligible* for —
which someone assigned to you beforehand. If a role you expect is missing, this app is not where it
gets fixed; ask whoever administers your tenant.

## Install it

Download the installer from the project's Releases page and run it.

There is no UAC prompt and nothing to choose — it installs for your Windows account only, into
`%LocalAppData%\Entra-PIM-Manager`. It needs no admin rights, installs no service, and changes
nothing for other users of the machine. If your company rolled it out for you, it is already there.

On the very first start you are asked two things: whether the app should start when you sign in to
Windows, and whether it should keep a Start-menu entry. Both are on, and both can be changed later
under **Settings → Behavior**. The question is asked once per installation.

The app has no window of its own in the taskbar. It lives in the notification area — the icons next
to the clock. Windows likes to hide new icons there: if you cannot see it, click the **^** arrow,
and drag the icon onto the visible part of the bar to keep it there.

## Connect your account

Click the tray icon. The first time, the panel says **Welcome to Entra PIM Manager**.

What happens next depends on how you got the app:

- **Rolled out by your company** — it already knows your tenant. Click **Add account**, then
  **Sign in**.
- **Installed it yourself** — it needs the tenant's App Registration first: **Set up your first
  tenant**, then enter tenant id and client id. Your admin provides both — they identify the tenant
  and the App Registration, and there is no way to guess them. That done, add the account.

**Sign in** opens the standard Windows account picker. Pick the admin account you hold the roles
with — for many people that is a separate account from the one they log into Windows with. There is
no password prompt inside the app: Windows itself does the sign-in, and the app never sees your
password.

You can connect several accounts across several tenants. There is nothing to switch between —
everything is shown in one list, grouped by tenant.

### When the account picker keeps choosing the wrong account

Some organizations route sign-in through their own identity provider (Okta, ADFS and similar), which
sometimes signs you in as your everyday account no matter which one you pick, and you never get
offered the admin one.

For that case there is **Device code** under the sign-in options. It shows a URL and a short code
you enter on your phone or in a private browser window, where nothing is signed in yet.

Two things to know before you use it. Some companies block device-code sign-in with a Conditional
Access policy — then it fails and the normal sign-in is the only route. And roles protected by an
*authentication context* cannot be activated with a device-code session; the app tells you so on
the account, and the fix is to remove the account and add it again the normal way.

## Reading the tray icon

The icon answers "do I have privileges right now?" without opening anything. Hover it for the
detail.

| Icon | Meaning |
| --- | --- |
| **Red** | Not signed in — no account connected, or the session expired and needs a new sign-in |
| **Grey** | Signed in, nothing active. The normal resting state |
| **Green** | At least one role is active. The tooltip counts them |
| **Amber** | An active role is about to expire. The tooltip names it and counts down |

The shield is drawn to suit your taskbar, light or dark, and follows it if you change the Windows
theme. Only the small dot carries a meaning.

## The panel

Clicking the icon opens the panel. Top to bottom:

**PINNED** — roles and saved scope sets you marked with a star. Yours to arrange; it stays across
restarts.

**RECENT** — the last three roles you activated. Fills itself, no maintenance.

**ELIGIBILITIES** — everything you may activate, grouped per tenant. Groups fold, and the app
remembers which ones you left closed.

**ACTIVE** — what is switched on right now, each with a countdown bar. This section is the reason to
look at the app when you are not activating anything.

The **search box** searches role name, role type, tenant *and* Azure scope in one go — typing a
subscription name finds the roles that apply to it. The **⟳** button forces a refresh, though the
list keeps itself current on its own: activations, expiries and roles ended elsewhere show up
without you asking. **⚙** opens Settings.

At the very bottom sits the version you are running. It is a link: clicking it opens that release on
GitHub, so "what actually changed" is one click from the app rather than a search.

## Activate a role

Click a role. The activation form slides in.

**Duration** — a slider in half-hour steps. Its maximum is not the app's decision: it is the policy
your admin set for that role, so different roles offer different maximums. The starting value is
yours to set under **Settings → Behavior → Default activation duration**.

**Justification** — why you need it. This is written into your tenant's audit log and read by real
people, so "work" helps nobody later. If you keep typing the same sentence, save it with **+ Save
current** under **FAVOURITES**: it comes back for that role as a one-click favourite.

**Ticket number / Ticket system** — asked only when the role's policy requires it. The ticket system
is prefilled per tenant if your admin configured one; the number is yours to type.

Then **Activate**. The role moves to ACTIVE within a few seconds.

### Two notices worth reading

**ℹ This activation requires approval** — you are not getting the role now. The request goes to an
approver and waits. The row stays visible as pending, and the role activates when they say yes.
Nothing more to do on your side; chase the approver, not the app.

**ℹ This activation may require additional verification (MFA)** — Windows asks you to confirm during
the activation.

**⚠ This group can carry directory roles** — on a PIM-for-Groups entry. Group membership can carry
admin roles with it, so activating this may grant more than the group's name suggests. Worth knowing
what you are switching on.

## Azure roles: choosing where the role applies

An Azure role you hold on a management group applies to everything beneath it — which can be every
subscription your company owns. Activating all of that to work on one is unnecessary exposure, so
the app asks where.

Those roles get an **Activate on** button. It opens a list with a search box at the top, then
**subscriptions**, then **management groups**, and last **Entire scope — everything listed above**.
That order is deliberate: smallest blast radius first.

**Nothing is preselected — not even the scope your eligibility sits on.** Activate stays unavailable
until you answer. Taking everything is available at the bottom of the list, but it is a decision you
make rather than what happens if you leave the form alone.

Every ticked scope becomes its own activation. Tick three subscriptions and ACTIVE shows three rows,
each with its own countdown, each stoppable on its own.

Resource groups are not offered. Nor are individual resources — those exist in the portal, but the
list would be endless and the app does not try.

### Save a set of scopes

Working in the same two subscriptions for weeks is normal, and re-ticking them daily is not.

Tick what you need, give it a name ("Project Contoso" — optional), and save it. It appears under
**FAVOURITE SCOPES** in that role's panel. One click ticks exactly those scopes; clicking a
different set **replaces** the selection rather than adding to it, so sets cannot quietly pile up.

Star a set and it also appears under **PINNED** on the main page, next to your pinned roles — one
click from the front page to a filled-in form. Pinning a role and pinning a scope set are the same
gesture on purpose.

Saved sets are stored on your machine (`scope-favorites.json`), never in the tenant. Nobody else
sees them.

## End a role early

Each ACTIVE row has a **stop** button. Use it when you are done — the whole point of PIM is that
privileges are not lying around.

**For the first five minutes the button is grey and does nothing.** That is not a bug and not the
app being careful: Microsoft refuses to deactivate a role that has been active for less than five
minutes. Hover the button and it tells you how long is left. After that it turns red and works.

Not ending a role costs nothing — it expires by itself.

## Before a role expires

Shortly before an active role runs out, a notification tells you, and the tray icon turns amber. The
countdown bar on the row shifts colour as it gets close.

Both the warning and how early it fires are under **Settings → Notifications**.

## Settings

The gear icon, top right.

**APPEARANCE** — Theme: System, Light or Dark.

**BEHAVIOR** — Start with Windows. Keep a Start-menu entry. Default activation duration (the
slider's starting value, not a limit — the limit is your admin's policy).

**NOTIFICATIONS** — Notify before expiry, and how early.

**UPDATES** — see below.

**DIAGNOSTICS** — Log detail, Open log folder, and a **Network check** that probes every endpoint
the app and the Windows sign-in need. **Copy report** puts the result on the clipboard in a form you
can paste into a ticket. Reach for this when sign-in fails in a locked-down network.

**TENANTS** — one card per tenant: its App Registration, its ticket system, and the accounts signed
into it. You can give an account a short alias ("EADM") instead of reading a long UPN everywhere,
and drag tenants into the order you want. Changing an App Registration takes effect after a restart,
and the app says so when it does.

## Updates

The app checks GitHub once a day and offers what it finds; you choose **Install** or **Later**, and
after downloading, whether to restart now or let it apply the next time you start the app. The
switch is under **Settings → Updates**.

**Check for updates** in the same place runs a check on the spot and tells you what came back. It
works whether or not automatic updates are on — that switch governs being interrupted, not being
allowed to ask.

One answer surprises people: *a newer version exists but is not offered yet.* Releases are held back
for their first 72 hours, on purpose. The builds are not code-signed, and security software blocks
binaries it has not classified yet — a fresher release would install and then refuse to start. It
becomes available on its own; there is nothing to do.

## When something does not work

**No roles listed, but you know you have some.** Check the account, top of the panel — it is easy to
be signed in with your everyday account instead of the admin one. If the account is right, the
eligibility genuinely is not there, and that is a question for whoever administers the tenant.

**Azure roles are missing while the others are there.** The panel shows a line that starts *Azure
resource roles unavailable* — what follows it is the cause. A missing consent resolves itself once an
admin grants it. A Conditional Access policy that wants a managed device is one for IT. And if you
signed in with a device code, that is the cause: that sign-in cannot prove anything about the
device, so remove the account and add it again with the normal sign-in.

**Sign-in window opens and stays blank.** Usually a network that blocks a Microsoft endpoint. Run
**Settings → Diagnostics → Network check**, hit **Copy report**, and send that to IT — it names the
endpoint that is blocked instead of leaving them guessing.

**Sign-in fails with a consent or permission message.** The App Registration has not been consented
to in that tenant. An admin does it once; see
[app-registration-setup.md](app-registration-setup.md).

**Activation fails and mentions authentication context or Conditional Access.** The role demands a
stronger sign-in than the session has. If you signed in with a device code, that is the cause —
remove the account and add it again with the normal sign-in.

**Activation says it needs approval and nothing happens.** It is waiting for a person. The app
cannot speed that up.

**The stop button is grey.** The first five minutes; see above.

**The tray icon is gone.** Windows hid it. Click the **^** arrow next to the clock and drag it back
onto the bar.

**Anything else** — **Settings → Diagnostics → Open log folder**. The log contains no passwords, no
tokens and no justification text; it is safe to attach to a ticket.

## What lands on your machine

Everything sits under `%LocalAppData%\junis\Entra-PIM-Manager`, in your own user profile:

- your settings, pinned roles, saved justifications and saved scope sets
- which accounts you connected, and your tenants' App Registration entries
- the Windows-managed token cache that keeps you signed in
- the log files

No password is ever stored — sign-in is handled by Windows. Nothing is written outside your profile:
no `Program Files`, no machine-wide registry, no service, no scheduled task. The only entry outside
this folder is the autostart value under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, if
you left autostart on.

Uninstalling removes all of it, including the tokens and the autostart entry. **Updating removes
none of it** — settings, accounts and favourites survive.
