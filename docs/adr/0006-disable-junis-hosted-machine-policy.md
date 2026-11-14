# Turn off junis-hosted through a machine policy, separate from the tenant lock

Some organizations require their own App Registration but still let users add tenants, for
example customer tenants (issue #19). The tenant lock (ADR 0005) rules that out. They get a second
DWORD, `DisableJunisHosted` = 1, under the same key `HKLM\SOFTWARE\Policies\junis\Entra-PIM-Manager`.
While it is set, the app starts as if the build shipped no junis client id: every place that offers
or shows junis-hosted already depends on that id, so the option, the hosting chip and the consent
link disappear without further code. Read at startup, like the lock. Decided 2026-11-14.

## Considered options

- **Widen the tenant lock instead of a second value.** Rejected: the lock also stops users from
  adding tenants, which is exactly what these organizations want to keep.
- **Keep entries that already carry the junis client id junis-hosted, and let the policy hide
  only the offer.** Rejected: the app would keep the junis id and carry an "offered" flag into
  three ViewModels, plus a rule for switching such an entry in the gear. And junis-hosted would
  keep running, chip included, where the organization turned it off.
- **Block entries that already carry the junis client id, with a "turned off by your
  organization" message.** Rejected: the app would have to keep the junis id and carry a flag into
  the sign-in paths. Not taken, because the policy is a convenience, not a security boundary: a
  user can type the junis id into the self-hosted field anyway, and only the tenant's admin consent
  decides whether the junis registration signs in.
- **Make the install script refuse to fall back to junis-hosted under the policy.** Rejected: whoever
  sets the policy also builds the rollout and passes `-ClientId`. The deployment docs say so.
- **Hide junis-hosted when the value is malformed.** Rejected: the same rule as the lock (only a
  DWORD other than 0 counts, anything else is ignored and logged), so admins learn one rule.

## Consequences

- An entry that carries the junis client id (from before the policy, from `--client-id`, from
  the install script without `-ClientId`, or typed into the client id field) counts as
  self-hosted. Accepted, because such an entry should not exist where the policy is set:
  - it signs in with the explicit scopes instead of Graph's `.default`, so a tenant's first
    consent in the sign-in window covers Graph only, not Azure;
  - every error on it uses the self-hosted wording and shows no junis consent link, including
    junis' emergency switch-off and device code, which is offered and fails with `AADSTS7000218`;
  - verification is per client id, as for any self-hosted entry: one sign-in through any of them
    marks every entry carrying the junis id as verified, consented or not. Until then the
    Settings header shows no Verified badge and the Tenants section opens on every visit;
  - after a lost or unreadable settings file, the one-time adoption of pre-verification installs
    marks the junis id verified without a sign-in.
- The policy must take effect in the same post-configure step that sets the shipped junis id.
  Post-configure steps run in registration order, so a separate earlier step would be overwritten.
- The key path and the value name are a public contract, like `TenantLockdown`.
