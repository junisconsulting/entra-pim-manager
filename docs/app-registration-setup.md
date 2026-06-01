# Entra App Registration — Setup for Entra PIM Manager

> This guide describes the one-time setup of an Entra App Registration
> that Entra PIM Manager needs in order to authenticate against Microsoft Graph.
> Requires an Entra administrator for the admin consent **in the home tenant**
> and in every additional tenant in which Entra PIM Manager will be used.
>
> Entra PIM Manager is multi-tenant: **one** App Registration covers any number of
> tenants. Account selection happens interactively in the WAM picker. An optional
> `AllowedTenants` whitelist in the local configuration locks the app down to
> a known set of tenant GUIDs (e.g. group subsidiaries); without a whitelist,
> all tenants in which admin consent was granted are allowed.

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

- **Application (client) ID** → `ClientId`

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
3. **Allow public client flows**: set to **Yes** (desktop/broker flow).

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

Without admin consent in the respective tenant, the first Graph call fails when
that account is added.

## 5. Enter the ClientId

The normal path requires no file editing: start the app, open **Settings**, and
paste the `ClientId` from step 1 into the App Registration field. The app saves
it to your per-user config at
`%LocalAppData%\Entra-PIM-Manager\appsettings.local.json` and applies it on the
next restart. The shipped `appsettings.json` carries only a placeholder.

### Optional: restrict the allowed tenants

`AllowedTenants` is not exposed in the UI — to lock the app down to a known set
of tenant GUIDs, edit the per-user config file directly and add the array
alongside the `ClientId` the UI already wrote:

```json
{
  "EntraPimManager": {
    "ClientId": "00000000-0000-0000-0000-000000000000",
    "AllowedTenants": [
      "11111111-1111-1111-1111-111111111111",
      "22222222-2222-2222-2222-222222222222"
    ]
  }
}
```

Empty array or omitted entry = unrestricted (any tenant with admin consent may
be enrolled).

### Running from source

When launching from a source build instead of an installer, you can skip the UI
and provide the value directly: copy
`src/Entra-PIM-Manager.App.Avalonia/appsettings.local.json.sample` to
`src/Entra-PIM-Manager.App.Avalonia/appsettings.local.json` and fill in
`ClientId`. Both this file and the per-user one are in `.gitignore` — **never
commit either**.

## 6. Verification

1. **Start the app and open Settings → ACCOUNTS → "Add account…".** A slide-in
   opens with an optional tenant field and a primary **Sign in** button.
2. **Leave the tenant field blank and click Sign in** → the WAM picker appears;
   pick your admin account in the home tenant. It then appears in
   `%LocalAppData%\Entra-PIM-Manager\accounts.json` and in the UI.
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
