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

## Acknowledgements

Reporters who follow responsible disclosure are credited in the release notes of the fixing version, unless they prefer to remain anonymous.
