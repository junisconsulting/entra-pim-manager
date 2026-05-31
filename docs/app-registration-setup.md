# Entra App Registration — Setup für Entra PIM Manager

> Diese Anleitung beschreibt das einmalige Setup einer Entra App Registration,
> die Entra PIM Manager benötigt, um sich gegen Microsoft Graph zu authentifizieren.
> Benötigt einen Entra-Administrator für den Admin-Consent **im Home-Tenant**
> und in jedem zusätzlichen Tenant, in dem Entra PIM Manager genutzt werden soll.
>
> Entra PIM Manager ist multi-tenant: **eine** App Registration deckt beliebig viele
> Tenants ab. Account-Wahl passiert interaktiv im WAM-Picker. Eine optionale
> `AllowedTenants`-Whitelist in der lokalen Konfiguration sperrt die App auf
> eine bekannte Menge von Tenant-GUIDs (z. B. Konzern-Tochterunternehmen);
> ohne Whitelist sind alle Tenants erlaubt, in denen Admin-Consent erteilt wurde.

## 1. App Registration im Home-Tenant anlegen

1. [Entra-Portal](https://entra.microsoft.com) → **Identity → Applications →
   App registrations → New registration**.
2. **Name**: `Entra PIM Manager`.
3. **Supported account types**: **Accounts in any organizational directory
   (Any Microsoft Entra ID tenant — Multitenant)**.
   - Wichtig: nicht „Single tenant", nicht „... and personal Microsoft accounts".
4. **Redirect URI**: zunächst leer — wird in Schritt 2 als Plattform gesetzt.
5. **Register**.

Aus der Übersicht notieren:

- **Application (client) ID** → `ClientId`

(Eine `TenantId` wird nicht mehr in die App-Konfiguration eingetragen — der Tenant
jedes enrolled Accounts wird beim Sign-in aus dem WAM-Result ermittelt.)

## 2. Plattform & Redirect-URI (WAM-Broker)

1. App Registration → **Authentication → Add a platform → Mobile and desktop
   applications**.
2. Custom Redirect URI hinzufügen:

   ```
   ms-appx-web://microsoft.aad.brokerplugin/{client-id}
   ```

   `{client-id}` durch die echte Application (client) ID ersetzen.
3. **Allow public client flows**: auf **Yes** stellen (Desktop-/Broker-Flow).

## 3. API-Berechtigungen (Delegated)

**API permissions → Add a permission → Microsoft Graph → Delegated permissions** —
folgende Scopes hinzufügen:

| Scope | Zweck |
|---|---|
| `User.Read` | Profil des angemeldeten Users |
| `RoleEligibilitySchedule.Read.Directory` | Eligible Directory-Roles lesen |
| `RoleAssignmentSchedule.ReadWrite.Directory` | Directory-Roles aktivieren/deaktivieren |
| `RoleManagementPolicy.Read.Directory` | PIM-Policies für Directory-Roles lesen |
| `PrivilegedAccess.ReadWrite.AzureADGroup` | PIM-for-Groups aktivieren/deaktivieren |
| `Group.Read.All` | Gruppennamen auflösen |

## 4. Admin-Consent — pro Tenant

**API permissions → Grant admin consent for \<Home-Tenant\>**.

Für **jeden weiteren Tenant**, in dem Entra PIM Manager genutzt werden soll, muss ein
dortiger Admin separat Consent erteilen:

```
https://login.microsoftonline.com/{external-tenant-id}/adminconsent
    ?client_id={pim-manager-client-id}
    &redirect_uri=ms-appx-web://microsoft.aad.brokerplugin/{pim-manager-client-id}
```

`{external-tenant-id}` und `{pim-manager-client-id}` ersetzen. Der dortige Admin
folgt dem Link, meldet sich an und bestätigt die Berechtigungen einmalig.

Ohne Admin-Consent im jeweiligen Tenant schlägt der erste Graph-Call beim
Hinzufügen dieses Accounts fehl.

## 5. Konfiguration eintragen

`ClientId` (und optional `AllowedTenants`) in eine lokale Konfigurationsdatei
eintragen — **nie committen**:

1. `src/Entra-PIM-Manager.App.Avalonia/appsettings.local.json.sample` nach
   `src/Entra-PIM-Manager.App.Avalonia/appsettings.local.json` kopieren.
2. `ClientId` mit dem echten Wert aus Schritt 1 füllen.
3. **Optional** in `AllowedTenants` die GUIDs der erlaubten Tenants eintragen:

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

   Leeres Array oder Eintrag weglassen = unbeschränkt (jeder Tenant mit
   Admin-Consent darf enrollt werden).

`appsettings.local.json` ist in `.gitignore`.

## 6. Verifikation

1. **App starten** → WAM-Picker erscheint, da kein Account enrollt ist.
2. **Anmelden mit Konto in Home-Tenant** → Account erscheint in
   `%LocalAppData%\Entra-PIM-Manager\accounts.json` und ist in der UI sichtbar.
3. **Zweites Konto in anderem Tenant hinzufügen** → setzt Admin-Consent im
   zweiten Tenant voraus (Schritt 4 dieser Anleitung).

Für jeden enrolled Account wird ein eigener `GraphServiceClient` instanziiert
(siehe [IGraphClientFactory.CreateFor(account)](src/Entra-PIM-Manager.Core/Graph/IGraphClientFactory.cs)),
damit Token-Acquisition, Retry und Claims-Challenges sauber pro Tenant laufen.
