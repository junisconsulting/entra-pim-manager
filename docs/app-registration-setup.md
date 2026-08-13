# Entra App Registration — Setup for Entra PIM Manager

> This guide describes the one-time setup of an Entra App Registration
> that Entra PIM Manager needs in order to authenticate against Microsoft Graph.
> Requires an Entra administrator for the admin consent **in the home tenant**
> and in every additional tenant in which Entra PIM Manager will be used.
>
> Entra PIM Manager is multi-tenant: **one** App Registration covers any number of
> tenants **within one cloud**. Account selection happens interactively in the WAM
> picker. An optional `AllowedTenants` whitelist in the local configuration locks
> the app down to a known set of tenant GUIDs (e.g. group subsidiaries); without a
> whitelist, all tenants in which admin consent was granted are allowed.
>
> **Using Entra China (21Vianet) too?** National clouds are physically isolated
> instances of Entra, so a Global App Registration does not exist there. Work
> through this guide once per cloud and see [§7](#7-sovereign-clouds-entra-china-21vianet)
> for what differs.

## 1. Create the App Registration in the home tenant

1. [Entra portal](https://entra.microsoft.com) → **Identity → Applications →
   App registrations → New registration**.
2. **Name**: `Entra PIM Manager`.
3. **Supported account types**: **Accounts in any organizational directory
   (Any Microsoft Entra ID tenant — Multitenant)**.
   - Important: not "Single tenant", not "... and personal Microsoft accounts".
4. **Redirect URI**: leave empty for now — it is set as a platform in step 2.
5. **Register**.

Note from the overview:

- **Application (client) ID** → the client id for this cloud

(A `TenantId` is no longer entered into the app configuration — the tenant
of each enrolled account is determined from the WAM result at sign-in.)

## 2. Platform & redirect URI (WAM broker)

1. App Registration → **Authentication → Add a platform → Mobile and desktop
   applications**.
2. Add a custom redirect URI:

   ```
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
| `PrivilegedAccess.ReadWrite.AzureADGroup` | Activate/deactivate PIM for Groups |
| `Group.Read.All` | Resolve group names |

## 4. Admin consent — per tenant

**API permissions → Grant admin consent for \<home tenant\>**.

For **every additional tenant** in which Entra PIM Manager will be used, an
admin in that tenant must grant consent separately:

```
https://login.microsoftonline.com/{external-tenant-id}/adminconsent
    ?client_id={pim-manager-client-id}
    &redirect_uri=ms-appx-web://microsoft.aad.brokerplugin/{pim-manager-client-id}
```

Replace `{external-tenant-id}` and `{pim-manager-client-id}`. The admin in that
tenant follows the link, signs in, and confirms the permissions once.

The host above is the **Global** authority. Consent for a tenant in another
cloud runs against that cloud's own authority and its own client id — for
Entra China:

```text
https://login.partner.microsoftonline.cn/{external-tenant-id}/adminconsent
    ?client_id={china-client-id}
    &redirect_uri=ms-appx-web://microsoft.aad.brokerplugin/{china-client-id}
```

Without admin consent in the respective tenant, the first Graph call fails when
that account is added.

## 5. Enter the client id

The normal path requires no file editing: start the app, open **Settings → APP
REGISTRATION**, and paste the client id from step 1 into the row for its cloud.
The app saves it to your per-user config at
`%LocalAppData%\junis\Entra-PIM-Manager\appsettings.local.json` and applies it on the
next restart. The shipped `appsettings.json` carries only a placeholder.

Leave a cloud's row blank if you don't use it — that cloud is then simply absent
from the cloud picker when you add an account. At least one row must be filled in.

The green **Verified** badge only appears once an account has actually signed in
with that registration; it is per cloud, because a Global sign-in proves nothing
about the China registration.

### Optional: restrict the allowed tenants

`AllowedTenants` is not exposed in the UI — to lock the app down to a known set
of tenant GUIDs, edit the per-user config file directly and add the array
alongside the registrations the UI already wrote:

```json
{
  "EntraPimManager": {
    "AppRegistrations": {
      "Global": "00000000-0000-0000-0000-000000000000",
      "China": "00000000-0000-0000-0000-000000000000"
    },
    "AllowedTenants": [
      "11111111-1111-1111-1111-111111111111",
      "22222222-2222-2222-2222-222222222222"
    ]
  }
}
```

Empty array or omitted entry = unrestricted (any tenant with admin consent may
be enrolled). Tenant GUIDs are unique across clouds, so one flat list covers both.

A bare `"ClientId"` from a pre-0.4.2 configuration is still read, as the **Global**
registration. `AppRegistrations:Global` wins if both are present.

### Running from source

When launching from a source build instead of an installer, you can skip the UI
and provide the value directly: copy
`src/Entra-PIM-Manager.App.Avalonia/appsettings.local.json.sample` to
`src/Entra-PIM-Manager.App.Avalonia/appsettings.local.json` and fill in
`AppRegistrations`. Both this file and the per-user one are in `.gitignore` —
**never commit either**.

## 6. Verification

1. **Start the app and open Settings → ACCOUNTS → "Add account…".** A slide-in
   opens with an optional tenant field and a primary **Sign in** button.
2. **Leave the tenant field blank and click Sign in** → the WAM picker appears;
   pick your admin account in the home tenant. It then appears in
   `%LocalAppData%\junis\Entra-PIM-Manager\accounts.json` and in the UI.
3. **Add a second account in another tenant** → open "Add account…" again, enter
   that tenant's id or domain, and sign in. This requires admin consent in the
   second tenant (step 4 of this guide).
4. **Federated IdP signs you in as the wrong account?** Use **Advanced → Sign in
   with device code** in the same panel and complete sign-in on another device
   (e.g. your phone). Note: device-code flow runs broker-less, so a Conditional
   Access policy requiring a managed device — or blocking device-code flow — will
   reject it.

For every enrolled account a dedicated `GraphServiceClient` is instantiated
(see [IGraphClientFactory.CreateFor(account)](../src/Entra-PIM-Manager.Core/Graph/IGraphClientFactory.cs)),
so that token acquisition, retry, and claims challenges run cleanly per tenant.

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
| Config key | `AppRegistrations:Global` | `AppRegistrations:China` |

### Procedure

1. Sign in to [portal.azure.cn](https://portal.azure.cn) with an admin of your
   China tenant and repeat **steps 1–4** of this guide there. Nothing changes in
   substance: same name, multitenant, same redirect-URI pattern (with the
   **China** client id), "Allow public client flows" on, the same six delegated
   Graph scopes, admin consent per China tenant via the `login.partner…` URL in
   step 4.
2. In the app: **Settings → APP REGISTRATION → Entra China (21Vianet)** → paste
   the China client id → **Save** → **Restart now**.
3. After the restart, **Settings → ACCOUNTS → "Add account…"** shows a **Cloud**
   dropdown (it is hidden while only one cloud is configured). Pick
   *Entra China (21Vianet)* and sign in.

Global and China accounts coexist: each enrollment records its cloud, and the app
routes its token acquisition, token cache file and Graph base URL accordingly.

### Caveats

- **Feature availability.** Microsoft states that services and features present
  in the global service may be missing from a national cloud. Verify that your
  eligibilities actually list before relying on the China path in production.
- **WAM broker.** The broker is used against the 21Vianet authority the same way
  as against Global. If it misbehaves in your environment, **Advanced → Sign in
  with device code** in the same panel is the fallback and is wired per cloud too.
