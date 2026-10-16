# Offer a junis-hosted multi-tenant App Registration next to the customer's own

Before the first sign-in, someone has to create an App Registration, set its redirect URI and
permissions, and consent to it. That is the biggest hurdle for a new tenant. junis therefore
publishes one multi-tenant registration, registered in its production tenant, that a customer
admin only consents to. Customers' own registrations stay fully supported, and existing entries
are not touched. The junis
registration has "Allow public client flows" switched off, so device-code sign-in works only
with a customer's own registration.

## Considered options

- **The customer's own registration only (status quo).** Every tenant needs an admin who may
  create apps. The setup script helps, but it needs the Graph PowerShell module and the rights
  to create apps.
- **The junis registration only.** Rejected:
  - Security teams want privileged tools under a registration they control.
  - Entra China cannot use a Global registration at all.
  - junis would become a single point of failure for every customer.
- **The junis registration with public client flows on, so device code works for everyone.**
  Rejected. A client id that is public and consented in many tenants with role-activation
  scopes is the kind device-code phishing campaigns use ([Storm-2372](https://www.microsoft.com/en-us/security/blog/2025/02/13/storm-2372-conducts-device-code-phishing-campaign)).
  WAM identifies itself as a public client through its redirect URI and works without the flag;
  device code does not (`AADSTS7000218`). With the flag off, device-code phishing with the junis
  client id fails at the token endpoint in every tenant. This holds only while the app has no
  credential of any kind: no secret, no certificate and no federated credential.
- **Discovering the tenant from the sign-in instead of asking for its id.** Rejected. A
  sign-in to `/organizations` finds only the account's home tenant, so tenants where the user
  holds roles as a guest would be missed. The list of tenants is also the allowlist a rollout
  pins.
- **A dedicated tenant that hosts only this registration.** It limits the blast radius on paper,
  so it will be suggested again. Rejected:
  - It needs admins of its own, and they can be phished just the same.
  - It would be a second tenant to secure and monitor, next to the production tenant junis already
    runs with PIM and phishing-resistant MFA.

  This choice is hard to undo. A registration cannot move between tenants, so a change means a new
  client id and a new consent in every customer tenant.

## Consequences

- It is a new registration, separate from the one junis uses for its own work. A public app for
  role activation should not carry earlier secrets, redirect URIs or owners, and junis keeps its
  own device-code use. The new registration has no owners, because an owner can change redirect
  URIs and credentials without activating a role. Changes go through a Cloud Application
  Administrator role activated with PIM.
- Global cloud only. Entra China stays with the customer's own registration: 21Vianet would need
  its own tenant and contract, and publisher verification is not available in national clouds.
- The same permissions as a customer's own registration: seven Microsoft Graph permissions plus
  Azure Service Management `user_impersonation`. Adding a scope means a new consent in every tenant
  that uses the junis registration. It belongs in the release notes, never a silent change. A
  token request with one unconsented scope fails as a whole, so a new scope gets a token request of
  its own. Until an admin consents, its feature stays visible but disabled, with a hint that names
  the missing consent. Sign-in keeps working for every tenant.
- Device code, the way out for accounts a federated identity provider signs in as the wrong user,
  needs a customer's own registration. For junis-hosted tenants the app has to say so.
- The tenant id stays mandatory, and the list of registrations stays the allowlist.
- An entry stores the junis client id explicitly, like any multi-tenant registration. Older app
  versions read it unchanged, which also covers a downgrade, and the Core logic stays as it is. If
  junis ever had to replace the registration, a release would have to rewrite the old id in every
  entry.
- A customer user cannot consent alone. The sign-in requests the Graph scopes, and all of them
  except `User.Read` need admin consent. Azure Service Management `user_impersonation` would not
  need it on its own. An admin consents once per tenant.
- Switching a tenant to self-hosted changes only the client side. The tenant-wide consent to the
  junis app stays until the tenant disables sign-in for it under Enterprise applications.
