# Grilling seeds

Areas where this project already paid for a question asked too late. Prompts for growing the
design tree when a change touches the area, not a questionnaire.

## Installer, deployment, unattended rollout

- **Which Velopack hooks actually run on `Setup.exe --silent`?** The install hook does;
  `OnFirstRun` does not, because a silent install never starts the app. 0.10.0 shipped unattended
  deployment claiming the installer set autostart, but it only ever happened in `OnFirstRun`. Found
  2026-10-05 while planning the Intune package, from the Velopack 1.2 source (`install.rs`).
- **In which context does the deployment run on the endpoint?** User or SYSTEM: everything is
  per-user, so SYSTEM lands in the systemprofile. And which PowerShell: Intune's `powershell.exe` is
  32-bit Windows PowerShell 5.1, and PowerShell 7 is not in-box. The install script required 7.

## Role policies (PIM settings per role)

- **Does the app actually know the role's settings?** For directory roles and PIM for Groups it
  reads them; for Azure resource roles an eligible-only user usually gets `AuthorizationFailed`,
  and `PolicyService` falls back to default limits that look like "no auth context, no approval".
  A feature that shows or decides something from the policy has to tell "not readable" from "not
  set". Found 2026-10-22 while planning the fresh-sign-in hint, after the wording was settled.

## Admin scripts against Graph and Azure

- **Can the modules share one session, and on which PowerShell?** `Microsoft.Graph.Authentication`
  and `Az.Accounts` conflict in Windows PowerShell 5.1 (Az issue #27378). The fresh-sign-in script
  was specified for 5.1 and 7 and had to become PowerShell 7 only. Found 2026-10-27.

## Hiding or locking an action

- **Which other screens send the user to it, and which other paths reach the same write?** The
  tenant lock hid the add form, but the welcome card and the add-account panel still pointed at
  it, and the gear's Save could still write a new registration for a card that had only
  accounts. Found 2026-11-11 in review and while building, after the grilling had settled scope.

## Sign-in and token timing

- **Does the change make the app ask for tokens without a click, or earlier than before?** On
  pooled AVD hosts with FSLogix the broker forgets an admin account that is not the Windows sign-in
  account at every logon (`wam_no_account_for_id`). Any interactive fallback on that path becomes
  an unprompted sign-in window, and a silent ARM failure an hour-long Azure back-off. Startup
  loading (#20) was first decided with the interactive fallback and reversed after the field test
  on 2026-11-12.
- **Which token requests can be in flight when the state changes, and in which order?** Graph and
  ARM requests race at load (`Task.WhenAll`), and the policy prefetch is fire-and-forget and still
  queued on the auth lock after **Remove**. The Windows-account safety net first let the ARM request
  use up Graph's attempt, and re-signed a just-removed account. Both found in review, 2026-11-13.
