# Entra App Registration — Setup for Entra PIM Manager

> This guide describes the one-time setup of an Entra App Registration
> that Entra PIM Manager needs in order to authenticate against Microsoft Graph.
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
> **Using Entra China (21Vianet) too?** National clouds are physically isolated
> instances of Entra, so a Global App Registration does not exist there. Work
> through this guide once per cloud and see [§7](#7-sovereign-clouds-entra-china-21vianet)
> for what differs. **Upgrading from 0.6.x?** See [§8](#8-upgrading-from-06x).

## 1. Create the App Registration

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
| `PrivilegedAccess.ReadWrite.AzureADGroup` | Activate/deactivate PIM for Groups |
| `Group.Read.All` | Resolve group names |

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

## 5. Enter the registration in the app

The normal path requires no file editing: start the app, open **Settings → APP
REGISTRATION**, and add an entry: the **tenant id**, the **client id** from
step 1, the **cloud**, and an optional **label** (e.g. the customer's name — it
names the entry in the sign-in picker) → **Add**. The app saves it to your
per-user config at `%LocalAppData%\junis\Entra-PIM-Manager\appsettings.local.json`
and applies it on the next restart (**Restart now** in the banner). Repeat for
every tenant you sign in to — with the same client id for every tenant a
multi-tenant registration is consented in.

Click an entry to edit it (the form switches to **Save**; changing the tenant id
or cloud moves the entry); ✕ removes it. An account that was enrolled through a
removed entry keeps its place in the list but can no longer sign in — its group
says so; remove the account or add the entry again.

The green **Verified** badge only appears once an account has actually signed in
with that entry; it is per registration, because a sign-in with one client id
proves nothing about another.

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

`Cloud` defaults to `Global`; `Label` is optional. Keep the list in **one** file:
.NET's configuration overlays arrays index by index, so the same key in two
files merges entry-wise. The shipped `appsettings.json` deliberately carries
only the scopes.

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
| Entry's `Cloud` | `Global` | `China` |

### Procedure

1. Sign in to [portal.azure.cn](https://portal.azure.cn) with an admin of your
   China tenant and repeat **steps 1–4** of this guide there. Nothing changes in
   substance: same name, same redirect-URI pattern (with the **China** client
   id), "Allow public client flows" on, the same six delegated Graph scopes,
   admin consent per China tenant via the `login.partner…` URL in step 4.
2. In the app: **Settings → APP REGISTRATION** → add an entry with the China
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
  id and cloud, and that admin consent was granted in that tenant (step 4).
- **"The sign-in ended up in a different tenant than the selected entry"**: the
  token Entra issued names another tenant than the entry. Check the entry's
  tenant id.
- **"Sign-in for this account is no longer valid (its App Registration may have
  changed)"** on a tenant group: the entry the account was enrolled through was
  removed or its client id changed. Remove the account in Settings and add it
  again through the current entry.
