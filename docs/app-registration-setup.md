# Entra App Registration — Setup for Entra PIM Manager

> This guide describes the one-time setup of an Entra App Registration
> that Entra PIM Manager needs in order to authenticate against Microsoft Graph
> and Azure Resource Manager.
> Requires an Entra administrator for the admin consent in every tenant in which
> Entra PIM Manager will be used.
>
> The app is configured **per tenant**: every tenant you sign in to gets one
> entry — the tenant's id, the client id of the App Registration to use there,
> and the cloud. One **multi-tenant** registration used in several tenants is
> simply listed once per tenant with the same client id; a customer who insists
> on their own **single-tenant** registration is an entry with their client id.
> The list is also the whitelist: a tenant without an entry cannot be signed in to.
>
> **Two ways per tenant.** **junis-hosted**: an admin consents once to the
> registration junis publishes — see [below](#junis-hosted-consent-only).
> **self-hosted**: the tenant's own registration — the numbered sections §1–§8
> describe this way.
>
> **Using Entra China (21Vianet) too?** National clouds are physically isolated
> instances of Entra, so a Global App Registration does not exist there. Work
> through this guide once per cloud and see [§7](#7-sovereign-clouds-entra-china--21vianet)
> for what differs. **Upgrading from 0.6.x?** See [§8](#8-upgrading-from-06x).

## junis-hosted: consent only

Global cloud only — Entra China (21Vianet) needs a self-hosted registration
([§7](#7-sovereign-clouds-entra-china--21vianet)).

Instead of creating an App Registration, a tenant can use the one junis
publishes: a verified multi-tenant registration with the same permissions as a
self-hosted one ([§3](#3-api-permissions-delegated)). You create nothing; the
consent adds the app under Enterprise applications in your tenant. An admin
consents once; users then add the tenant in the app with its tenant id only and
choose **junis-hosted**. In the configuration file it is an ordinary entry that
carries the junis client id. How junis operates and protects the registration:
[SECURITY.md](../SECURITY.md#the-junis-hosted-app-registration).

### The consent link

An admin of the tenant with **Cloud Application Administrator** or higher
(Application Administrator, Privileged Role Administrator, Global
Administrator) opens this link once, signs in and accepts:

```text
https://login.microsoftonline.com/{tenant-id}/adminconsent?client_id={junis-client-id}&redirect_uri=https%3A%2F%2Fjunisconsulting.github.io%2Fentra-pim-manager%2Fconsent%2F
```

`{tenant-id}` is your tenant's id. `{junis-client-id}` is the junis client id,
published in [SECURITY.md](../SECURITY.md#the-junis-hosted-app-registration).
When a sign-in fails for missing consent, the
app's add-account panel shows the same link with both filled in, under "Send
this link to an admin of this tenant:".

After **Accept**, the browser lands on junis' consent page, which says that the
consent was granted and for which tenant. After **Cancel** it says that it was
not. An account without the role mostly ends on Entra's own error page instead.

An admin with Privileged Role Administrator or Global Administrator can also
consent in the app's own sign-in window, the first time the tenant meets the
app: the window lists all eight permissions. Tick **Consent on behalf of your
organization** before **Accept**; without it the consent covers only the admin's
own account, and nobody else in the tenant can sign in. Cloud Application and
Application Administrators do not get that box; they use the link. If the
tenant had already consented to the Microsoft Graph permissions alone, the window
does not ask again and Azure resource roles stay unavailable. Then an admin opens
Enterprise applications → Entra PIM Manager → **Permissions** → **Grant admin
consent for** your tenant, which grants all eight permissions for every user.

### Verify the link before you consent

The consent lets the app activate PIM roles as any user of your tenant who
signs in to it, so check what you consent to:

- the `client_id` in the link and the **App ID** on the consent screen both
  equal the junis client id published in SECURITY.md;
- the consent screen shows the publisher as verified: **junis GmbH**.

Never rely on the display name, the logo or the domain — anyone can copy them.

### Recommended hardening and detection

For both ways; the app enforces none of these. They are set on the app under
**Enterprise applications** — for junis-hosted "Entra PIM Manager" by junis
GmbH, for self-hosted your own registration.

- **Assignment required.** Properties → **Assignment required?** = **Yes**, then
  assign a group of the eligible admins under **Users and groups**. Everyone
  else gets "Your account is not assigned to Entra PIM Manager in this tenant."
  Global Administrators are exempt, and assigning a group needs Entra ID P1.
- **Block device code flow** with a Conditional Access policy (Entra ID P1). A
  self-hosted tenant loses the app's device-code fallback with it
  ([§6](#6-verification), step 4).
- **An authentication context on activation**, so every activation asks for a
  fresh sign-in — see the
  [README](../README.md#recommended-a-fresh-sign-in-for-every-activation).
- **The admin consent workflow** (Enterprise applications → Consent and
  permissions → Admin consent settings), so users request approval from the
  Windows sign-in window instead of forwarding links.
- **Monitor** the audit events "Consent to application", "Add delegated
  permission grant", "Update delegated permission grant" and "Add service
  principal" for the app id, and compare the granted scopes with the list in
  [§3](#3-api-permissions-delegated). Also watch the app's sign-ins that do not
  come from a Windows desktop client.

### Leaving junis-hosted

Switching a tenant to self-hosted changes only the app; the tenant-wide consent
stays active. Leaving for good takes two steps:

1. Switch every entry for the tenant to self-hosted, in every installation —
   the gear on the tenant's card, or the install script with `-ClientId`
   ([unattended-deployment.md](unattended-deployment.md)).
2. Switch the junis app off in your tenant, as described in
   [SECURITY.md](../SECURITY.md#switch-it-off-in-your-tenant).

## 1. Create the App Registration

> **In a hurry?** `scripts/create-app-registration.ps1` automates §1–§4 of this guide
> and prints the command lines an unattended rollout needs — see
> [unattended-deployment.md](unattended-deployment.md). The steps below are the manual
> equivalent, and remain the reference for what the script does.

1. [Entra portal](https://entra.microsoft.com) → **Identity → Applications →
   App registrations → New registration**.
2. **Name**: `Entra PIM Manager`.
3. **Supported account types**:
   - **Accounts in any organizational directory (Multitenant)** when the same
     registration should serve several tenants (the usual case for an MSP or a
     group: register once in the home tenant, consent per tenant — step 4).
   - **Accounts in this organizational directory only (Single tenant)** for a
     registration that lives and is used in exactly one tenant — e.g. a customer
     who will not consent to a foreign app and registers their own.
   - Never "... and personal Microsoft accounts".
4. **Redirect URI**: leave empty for now — it is set as a platform in step 2.
5. **Register**.

Note from the overview:

- **Application (client) ID** → the client id
- **Directory (tenant) ID** → the tenant id of the tenant you registered in

## 2. Platform & redirect URI (WAM broker)

1. App Registration → **Authentication → Add a platform → Mobile and desktop
   applications**.
2. Add a custom redirect URI:

   ```text
   ms-appx-web://microsoft.aad.brokerplugin/{client-id}
   ```

   Replace `{client-id}` with the real Application (client) ID.
3. **Allow public client flows**: set to **Yes**.

   > **Why and when:** Entra decides whether an app is a public or confidential
   > client differently per flow. The **WAM broker** sign-in identifies itself as
   > a public client via the redirect URI above, so it works **even without** this
   > setting. The **device-code** flow (Advanced → "Sign in with device code")
   > uses **no** redirect URI, so Entra can only recognise it as a public client
   > through this flag — without it the token endpoint demands a client secret and
   > device-code sign-in fails with `AADSTS7000218`. Leave it on so both flows
   > work; for a desktop app with no secret this is the correct, intended state.

## 3. API permissions (delegated)

**API permissions → Add a permission → Microsoft Graph → Delegated permissions** —
add the following scopes:

| Scope | Purpose |
|---|---|
| `User.Read` | Profile of the signed-in user |
| `RoleEligibilitySchedule.Read.Directory` | Read eligible directory roles |
| `RoleAssignmentSchedule.ReadWrite.Directory` | Activate/deactivate directory roles |
| `RoleManagementPolicy.Read.Directory` | Read PIM policies for directory roles |
| `RoleManagementPolicy.Read.AzureADGroup` | Read PIM policies for groups (member and owner) |
| `PrivilegedAccess.ReadWrite.AzureADGroup` | Activate/deactivate PIM for Groups |
| `Group.Read.All` | Resolve group names |

PIM for Azure Resources goes through Azure Resource Manager rather than Graph and
needs one more delegated permission:

**API permissions → Add a permission → Azure Service Management → Delegated
permissions** — add `user_impersonation`.

| Permission | Purpose |
|---|---|
| `user_impersonation` (Azure Service Management) | List, activate and deactivate Azure resource roles (PIM for Azure Resources) as the signed-in user |

The app derives the matching scope (`https://management.azure.com/user_impersonation`,
or the China host's) from the entry's cloud — it is not configured anywhere. A
tenant that never grants this permission still works for directory roles and
groups; its tenant group shows "Azure resource roles unavailable" instead of the
Azure rows.

## 4. Admin consent — per tenant

**API permissions → Grant admin consent for \<home tenant\>**.

For a multi-tenant registration, **every additional tenant** in which Entra PIM
Manager will be used needs its own consent by an admin of that tenant:

```text
https://login.microsoftonline.com/{external-tenant-id}/adminconsent
    ?client_id={pim-manager-client-id}
    &redirect_uri=ms-appx-web://microsoft.aad.brokerplugin/{pim-manager-client-id}
```

Replace `{external-tenant-id}` and `{pim-manager-client-id}`. The admin in that
tenant follows the link, signs in, and confirms the permissions once. A
single-tenant registration needs consent in its own tenant only — it cannot be
used anywhere else.

After **Accept** the browser keeps loading: it cannot open the `ms-appx-web://`
redirect URI. The consent is recorded all the same (field test 2026-10-17);
close the tab and check Enterprise applications → the app → **Permissions** →
**Admin consent**.

The host above is the **Global** authority. Consent for a tenant in another
cloud runs against that cloud's own authority and its own client id — for
Entra China:

```text
https://login.partner.microsoftonline.cn/{external-tenant-id}/adminconsent
    ?client_id={china-client-id}
    &redirect_uri=ms-appx-web://microsoft.aad.brokerplugin/{china-client-id}
```

Without admin consent in the respective tenant, the first Graph call fails when
that account is added. The consent URL covers every permission listed on the
registration — the Azure Service Management one included.

## 5. Enter the registration in the app

The normal path requires no file editing: start the app, open **Settings →
TENANTS**, and add an entry: the **tenant id**, **self-hosted** with the
**client id** from step 1, the **cloud**, and an optional **label** (e.g. the
customer's name — it names the entry in the sign-in picker and replaces the
directory's display name wherever the tenant is shown) → **Add**. The app saves
it to your per-user config at
`%LocalAppData%\junis\Entra-PIM-Manager\appsettings.local.json` and applies it
on the next restart (**Restart now** in the banner). Repeat for every tenant you
sign in to — with the same client id for every tenant a multi-tenant
registration is consented in.

The gear on a tenant's card opens its entry: the way (junis-hosted, or
self-hosted with its client id), label and ticket system → **Save**, or
**Cancel**. A changed client id on a tenant that has accounts asks for a second
click and removes those accounts first; add them again after the restart.
Tenant id and cloud are the card itself and cannot be
edited — a different tenant id or cloud is a different tenant: add it as a new
one, and remove the old card if it is no longer needed. **Remove tenant**
in the same place removes the tenant completely — its accounts and their
sign-ins, its App Registration and its ticket system — and asks for a second
click. An entry removed outside the app (a hand edit of the file) leaves its
accounts on their card, which says they can no longer sign in; remove the tenant
or add the entry again.

The green **Verified** badge only appears once an account has actually signed in
with that entry; it is per registration, because a sign-in with one client id
proves nothing about another — and for junis-hosted per tenant, because one
tenant's consent proves nothing about another's.

### Configuration shape

The UI writes this; the entries can also be hand-edited:

```json
{
  "EntraPimManager": {
    "TenantAppRegistrations": [
      {
        "TenantId": "11111111-1111-1111-1111-111111111111",
        "ClientId": "22222222-2222-2222-2222-222222222222",
        "Cloud": "Global",
        "Label": "Contoso"
      }
    ]
  }
}
```

The form has one more field, **Ticket system**, which is deliberately *not* part of
this file: it is prefilled into the activation form for that tenant whenever a role
requires a ticket, and it is stored with the other user settings, so it applies at
once instead of after the restart the registration fields need. Leave it empty and
the field simply starts blank.

`Cloud` defaults to `Global`; `Label` is optional. Keep the list in **one** file:
.NET's configuration overlays arrays index by index, so the same key in two
files merges entry-wise. The shipped `appsettings.json` deliberately carries
no entries — only the scopes and `JunisHostedClientId`, the junis client id a
junis-hosted entry stores. The app reads that one from the shipped file only; a
value in `appsettings.local.json` is ignored.

### Running from source

When launching from a source build instead of an installer, you can skip the UI
and provide the entries directly: copy
`src/Entra-PIM-Manager.App.Avalonia/appsettings.local.json.sample` to
`src/Entra-PIM-Manager.App.Avalonia/appsettings.local.json` and fill in
`TenantAppRegistrations`. Both this file and the per-user one are in `.gitignore` —
**never commit either**.

## 6. Verification

1. **Start the app and open Settings → ACCOUNTS → "Add account…".** A slide-in
   opens with a **Sign in with** picker (hidden while only one entry exists) and
   a primary **Sign in** button.
2. **Pick the entry and click Sign in** → the WAM picker appears; choose an
   account that is a member or guest of that tenant. The account then appears in
   `%LocalAppData%\junis\Entra-PIM-Manager\accounts.json` and in the UI.
3. **Add the same identity in another tenant** → open "Add account…" again, pick
   that tenant's entry, and sign in. This requires admin consent in the second
   tenant (step 4 of this guide).
4. **Federated IdP signs you in as the wrong account?** Use **Advanced → Sign in
   with device code** in the same panel and complete sign-in on another device
   (e.g. your phone). Note: device-code flow runs broker-less, so a Conditional
   Access policy requiring a managed device — or blocking device-code flow — will
   reject it.

For every enrolled account a dedicated `GraphServiceClient` is instantiated
(see [IGraphClientFactory.CreateFor(account)](../src/Entra-PIM-Manager.Core/Graph/IGraphClientFactory.cs)),
plus a dedicated Azure Resource Manager client
([IArmClientFactory.CreateFor(account)](../src/Entra-PIM-Manager.Core/Arm/IArmClientFactory.cs)),
so that token acquisition, retry, and claims challenges run cleanly per tenant.
Each App Registration gets its own MSAL public client and DPAPI-encrypted token
cache (`msal-{client-id}.cache`).

## 7. Sovereign clouds (Entra China / 21Vianet)

Microsoft's national clouds are *physically isolated instances* of Azure and
Entra — separate directories, separate authorities, separate Graph endpoints.
Two consequences drive the whole setup:

- An App Registration exists in exactly **one** cloud. "Multitenant" means every
  tenant *in that cloud*, not across clouds. A Global client id sent to the
  21Vianet authority fails with `AADSTS700016` ("application not found in the
  directory").
- Access tokens are not interchangeable between clouds.

So you need **a second App Registration, created inside a China tenant**.

### What differs

| | Global | Entra China (21Vianet) |
|---|---|---|
| Portal to register in | `portal.azure.com` | `portal.azure.cn` |
| Authority | `login.microsoftonline.com` | `login.partner.microsoftonline.cn` |
| Microsoft Graph | `graph.microsoft.com` | `microsoftgraph.chinacloudapi.cn` |
| Azure Resource Manager | `management.azure.com` | `management.chinacloudapi.cn` |
| Entry's `Cloud` | `Global` | `China` |

### Procedure

1. Sign in to [portal.azure.cn](https://portal.azure.cn) with an admin of your
   China tenant and repeat **steps 1–4** of this guide there. Nothing changes in
   substance: same name, same redirect-URI pattern (with the **China** client
   id), "Allow public client flows" on, the same delegated permissions (Graph
   and Azure Service Management), admin consent per China tenant via the
   `login.partner…` URL in step 4.
2. In the app: **Settings → TENANTS** → add an entry with the China
   tenant's id, the China client id and cloud *Entra China (21Vianet)* →
   **Add** → **Restart now**.
3. After the restart, **Settings → ACCOUNTS → "Add account…"** lists the China
   entry under **Sign in with**. Pick it and sign in.

Global and China accounts coexist: each enrollment records its cloud, and the app
routes its token acquisition, token cache file and Graph base URL accordingly.

### Caveats

- **Feature availability.** Microsoft states that services and features present
  in the global service may be missing from a national cloud. Verify that your
  eligibilities actually list before relying on the China path in production.
- **WAM broker.** The broker is used against the 21Vianet authority the same way
  as against Global. If it misbehaves in your environment, **Advanced → Sign in
  with device code** in the same panel is the fallback and is wired per cloud too.

## 8. Upgrading from 0.6.x

Versions up to 0.6.x held **one client id per cloud** (`AppRegistrations:Global`,
`AppRegistrations:China`; before 0.4.2 a bare `ClientId`) and let any tenant sign
in through it, optionally limited by an `AllowedTenants` whitelist. 0.7.0 folds
that into per-tenant entries **automatically at first start**:

- every tenant an account is enrolled in becomes an entry with that cloud's
  client id, so **no account has to sign in again**;
- every tenant in `AllowedTenants` becomes an entry too (with the Global client
  id, or the only configured cloud's);
- the token-cache files are renamed from the per-cloud names (`msal.cache`,
  `msal-china.cache`, …) to the per-client-id names;
- the legacy keys are removed from `appsettings.local.json`.

**Action required in one case:** a client id that was configured but had
neither an enrolled account nor a whitelisted tenant has no tenant to be pinned
to. It is dropped (the log says so) and Settings shows no entry — add it again
together with its tenant id. Likewise, signing in to a tenant that has never
been enrolled now needs an entry first; the free-text "tenant id or domain"
field of the add-account panel is gone.

## Troubleshooting

- **`AADSTS700016` — "application not found in the directory"**: the client id
  is unknown in the tenant it was sent to. Check the entry's tenant id, client
  id and cloud, and that admin consent was granted in that tenant (step 4). On
  a junis-hosted tenant it means nobody has consented yet: see
  [the consent link](#the-consent-link).
- **`AADSTS65001` — consent missing** (also `AADSTS65004`, `AADSTS90094`): no
  admin has consented to the app in that tenant yet. junis-hosted: an admin
  consents once through [the consent link](#the-consent-link); the add-account
  panel shows it, filled in. Self-hosted: grant admin consent (step 4).
- **`AADSTS50105` — "Your account is not assigned to Entra PIM Manager in this
  tenant"**: the app has **Assignment required** on, and the account is not
  assigned, directly or through a group. An admin assigns it under Enterprise
  applications → the app → **Users and groups**.
- **`AADSTS7000112` or `AADSTS70001` — the app is switched off**: in the tenant,
  check **Enabled for users to sign-in?** on the app under Enterprise
  applications. On a junis-hosted tenant, if no admin of yours switched it off,
  junis did, for all tenants, and announces it as a
  [security advisory](../SECURITY.md#watch-for-advisories).
- **"The sign-in ended up in a different tenant than the selected entry"**: the
  token Entra issued names another tenant than the entry. Check the entry's
  tenant id.
- **"Sign-in for this account is no longer valid (its App Registration may have
  changed)"** on a tenant group: the entry the account was enrolled through was
  removed or its client id changed. Remove the account in Settings and add it
  again through the current entry.
- **"Azure resource roles unavailable: This tenant has not consented…"** on a
  tenant group: the registration lacks the Azure Service Management →
  `user_impersonation` permission, or the tenant has not consented to it yet
  (steps 3 and 4). Directory roles and groups keep working; the Azure rows appear
  on their own once consent is granted, within the hour or after a restart.
- **"Azure resource roles unavailable: The background sign-in for Azure was
  refused…"** on a tenant group: the Azure token is requested silently, and
  Windows does not say why it was refused. Usually the tenant's consent covers
  the Microsoft Graph permissions but not Azure, for example one given in the
  sign-in window of a self-hosted registration, which asks for Graph only: grant
  admin consent ([the consent link](#the-consent-link), or step 4). Otherwise a Conditional
  Access policy on Azure management (Windows Azure Service Management API), such
  as "require MFA", wants more than the Windows sign-in session carries.
- **An Azure role assigned to a group does not appear, while other Azure roles
  do**: Azure resolves the caller's group memberships from its own cache, so a
  membership that changed minutes ago is not visible to the eligibility query
  yet — most often after an eligible PIM-for-Groups member was turned into a
  real member. The permission is not the problem: refresh again later. To
  confirm the eligibility exists at all, list it by the group instead of by
  yourself (`$filter=principalId eq '<group object id>'` at the scope).
