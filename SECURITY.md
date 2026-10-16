# Security Policy

## Reporting a vulnerability

Entra PIM Manager handles Microsoft Entra privileged access — please treat security issues with appropriate care. If you discover a vulnerability, **do not open a public GitHub issue**. Report it privately through either of these channels:

- **Email**: [info@junis.de](mailto:info@junis.de)
- **GitHub Security Advisories**: use the [private vulnerability reporting](../../security/advisories/new) feature

Please include:

- A description of the vulnerability and its security impact
- Steps to reproduce, or a proof-of-concept
- The affected Entra PIM Manager version
- Any relevant logs **with tokens, justifications, and account identifiers redacted**

## Response timeline

- **Acknowledgement** of receipt: within 5 business days
- **Initial assessment** (confirmation or rejection): within 14 business days
- **Coordinated disclosure**: a public advisory is published once a fix is available, or 90 days after the initial report — whichever comes first

## Scope

In scope:

- The latest released version of Entra PIM Manager
- Authentication and authorization flaws (MSAL/WAM flows, token cache, claims-challenge handling)
- Privilege escalation paths
- Unintended leaks of tokens, justifications, or account identifiers in logs or persisted files
- Vulnerabilities in the auto-update mechanism (Velopack feed handling, update signature validation)

Out of scope:

- Vulnerabilities in upstream dependencies (Microsoft Graph SDK, MSAL, Avalonia, Velopack) — please report those directly to the upstream maintainers
- Issues that require the attacker to already hold administrative access to the user's machine
- Social-engineering attacks against end users (e.g. tricking a user into entering justification text into a fake dialog)
- SmartScreen / Defender warnings about unsigned builds — these are expected for the open-source release until a code-signing certificate is in place

## The junis-hosted App Registration

Next to a tenant's own App Registration (self-hosted), the app offers one that junis publishes and operates: **junis-hosted**, Global cloud only. A customer admin only consents to it ([how](docs/app-registration-setup.md#junis-hosted-consent-only)). Because one registration is consented in many tenants, this section says how junis runs it, how you check what you consent to, and how you switch it off. Background: [ADR 0002](docs/adr/0002-junis-hosted-app-registration.md).

| | |
| --- | --- |
| Application (client) id | published here when the registration goes live |
| Publisher | junis GmbH, verified, publisher domain `junis.de` |
| Sign-in audience | organizational accounts in any tenant (`AzureADMultipleOrgs`), no personal Microsoft accounts |
| Permissions | the seven Microsoft Graph delegated permissions plus Azure Service Management `user_impersonation` — the same [list](docs/app-registration-setup.md#3-api-permissions-delegated) as self-hosted |

### How junis creates and operates it

junis creates the registration by hand along this checklist; there is no script. The principals under "Who can change it" are reviewed before the first release that ships the client id.

#### The registration

- A new multi-tenant registration in the junis production tenant, the tenant linked to junis' Partner Center account — separate from the registration junis uses for its own work.
- "Allow public client flows" = **No**. Device-code sign-in with this client id therefore fails in every tenant (`AADSTS7000218`), which takes device-code phishing with a widely consented id off the table.
- **No client secret, certificate or federated identity credential, ever.** The point above holds only while the app has no credential of any kind.
- An app management policy assigned to this app blocks adding passwords and symmetric keys. Setting it up needs Security Administrator plus Cloud Application Administrator, and possibly a Workload ID licence, which is checked first. Adding a certificate or a federated credential stays alert-only (see [Alerts](#alerts)).
- The only redirect URI is `ms-appx-web://microsoft.aad.brokerplugin/{client-id}`: no `localhost`, Web or SPA URI.
- App instance property lock on **All properties**.
- Implicit grant off. No "Expose an API", no app roles, no optional claims.
- Sign-in audience `AzureADMultipleOrgs`.
- Publisher verification with publisher domain `junis.de`, a logo, [PRIVACY.md](PRIVACY.md) as the privacy statement and the MIT [LICENSE](LICENSE) as the terms.

#### Who can change it

- The app object has **no owners**: the creator removes themselves right after creating it. Changes go through the **Cloud Application Administrator** role, activated with PIM.
- Every principal that can modify the app holds that power only through a PIM-eligible role with phishing-resistant MFA, or not at all. That covers every built-in role with `microsoft.directory/applications/*` write actions — today Global Administrator, Privileged Role Administrator, Application Administrator, Cloud Application Administrator, Hybrid Identity Administrator, Partner Tier1 Support and Partner Tier2 Support — and every custom role with such actions.
- Standing assignments: only the **two break-glass Global Administrator accounts, signed in with FIDO2**.
- No service principal in the production tenant holds `Application.ReadWrite.All`, `AppRoleAssignment.ReadWrite.All` or `RoleManagement.ReadWrite.Directory`. Partner GDAP/DAP relationships into the tenant are reviewed.

#### Alerts

- junis security is alerted on **every** directory audit event whose target is this app's object id, its app id or its service principal's object id, whatever the activity or the changed property. That includes "Update application" with any property (redirect URIs, public client flag, federated credentials, permissions, sign-in audience, `isDisabled`), credentials, owners, and "Delete", "Hard delete" and "Restore application".
- Deleting items from the recycle bin (`microsoft.directory/deletedItems/delete`) is a protected action that requires an authentication context with phishing-resistant MFA.

### Emergency switch

- At least two named people at junis with an eligible Cloud Application Administrator assignment can switch the registration off (`isDisabled`), and a named maintainer publishes the GitHub Security Advisory.
- Switching it off blocks new tokens in every tenant at once. **Access tokens already issued stay valid until they expire, which is up to 28 hours for CAE-capable clients.**
- The registration is re-enabled only once it is clear whether a malicious redirect URI, credential or public-client switch ever existed.
- If one did, the advisory states the time window and the change. It asks customer admins to revoke the sessions of every user with a sign-in to the app in that window, and to review that window's sign-ins to the app that did not come from a Windows desktop client, its Graph and ARM activity, and its PIM activations.

### Verify a consent link before you consent

- The `client_id` in the link and the **App ID** on the consent screen equal the client id published above.
- The consent screen shows the verified publisher **junis GmbH**.
- Never rely on the display name, the logo or the domain — anyone can copy them.

### Watch for advisories

junis has no other way to reach you. If your tenant uses the junis-hosted registration, watch this repository's security advisories: **Watch → Custom → Security alerts**.

### Switch it off in your tenant

Enterprise applications → Entra PIM Manager → Properties → **Enabled for users to sign-in?** = **No**. That stops the app for every user of your tenant, whatever junis does. Deleting the enterprise application also removes the consent, but the app then shows its users a consent link, and any admin who follows it restores access — so disable it rather than delete it. See also [leaving junis-hosted for good](docs/app-registration-setup.md#leaving-junis-hosted) and the recommended [hardening and detection](docs/app-registration-setup.md#recommended-hardening-and-detection) for either way.

## Acknowledgements

Reporters who follow responsible disclosure are credited in the release notes of the fixing version, unless they prefer to remain anonymous.
