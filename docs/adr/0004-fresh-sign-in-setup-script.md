# Set up a fresh sign-in per activation with a signed script, not a one-click service

PIM roles in most tenants activate without a fresh sign-in, because that needs a Conditional Access
authentication context with sign-in frequency "Every time", and few tenants configure one. To make
that setup as easy as possible, Entra PIM Manager ships a PowerShell script signed by junis GmbH
that configures it, and the app starts it from a button with the tenant id filled in. The script
signs in through Microsoft Graph PowerShell, so neither the app nor a junis service gains a
permission. Decided 2026-10-22. Neither the script nor the button exists yet: the hint in the
activation panel ships first and links to a manual guide; the script's exact scope is designed
separately.

## Considered options

- **A web page that writes the configuration through Graph in one click.** It needs an App
  Registration that junis operates, with write permissions on Conditional Access and PIM role
  settings, consented in every customer tenant. Whoever compromises that page or registration can
  rewrite the Conditional Access policies of every customer: the attack surface ADR 0002 keeps the
  junis-hosted registration away from.
- **A button in the app that configures the tenant itself.** The app would hold the same write
  permissions for as long as it is installed, for a setup that runs once.
- **A local Windows Hello confirmation before activation, when no authentication context is set.**
  Rejected. It stops only a passer-by at an unlocked PC, who can switch it off in Settings or in
  the user-writable `settings.json`, and it does nothing against malware or stolen tokens. Entra
  never sees it, so admins could take it for the real control and skip the authentication context.
- **Falling back to the Windows password when Windows Hello is missing.** Rejected with it: the
  app would handle the user's Windows password, and every typo counts toward the domain's account
  lockout.

## Consequences

- The activation panel says, for every role whose settings the app can read and that has no
  authentication context, that a fresh sign-in is strongly recommended, and links to
  `docs/fresh-sign-in.md`. It cannot be dismissed. Where the app cannot read the role's settings,
  as for most Azure resource roles, it says nothing rather than guess.
- The script is signed in the release workflow like the binaries. Under `AllSigned` it still asks
  once per release whether to trust the publisher, because the 72-hour certificate changes daily
  (ADR 0003).
- The admin who runs it needs Conditional Access Administrator and Privileged Role Administrator,
  which they can activate with the app itself.
