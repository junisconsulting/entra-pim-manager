# Lock the tenant list through a machine policy in HKLM, read-only

Managed rollouts preconfigure the tenant (issue #14), and on shared admin workstations the "Add a
tenant" form and the switch to junis-hosted only confuse users. The lock is the DWORD
`TenantLockdown` = 1 under `HKLM\SOFTWARE\Policies\junis\Entra-PIM-Manager`, set by the
organization through Intune or Group Policy. The app only reads it; it never writes HKLM. While it
is set, the tenant list is read-only: no adding, no change of hosting or client id, no removing.
Label, ticket system and the accounts stay editable. Decided 2026-11-11.

The per-user rule ("no HKLM") is about the install footprint: the app writes nothing outside the
user's profile. Reading an admin-set policy key does not change that footprint.

## Considered options

- **A flag in `appsettings.local.json`, set by the install script or `--tenant-lockdown`** (the
  original PR #16). Rejected: the file lives in `%LocalAppData%`, and any user can edit it or run
  the app with `--tenant-lockdown false`. It would look like a control without being one.
- **Both sources, the JSON flag and the policy.** Rejected: a second source needs a precedence rule
  and adds nothing the policy does not already do.
- **`HKCU\Software\Policies`.** Not taken: a lock on a shared workstation belongs to the device, not
  to whoever signs in to it.

## Consequences

- The lock is a convenience for managed rollouts, not a security boundary: anyone can still sign in
  to another tenant in a browser. Keeping users out of foreign tenants is the job of tenant
  restrictions v2, which the deployment docs point to.
- Only the lock value is out of the user's reach, not the tenant list. The list stays in the
  user-writable `appsettings.local.json`, and the deployment arguments (`--tenant-id`,
  `--client-id`) keep working under the lock: the rollout depends on them, and Intune does not
  order a device script before a user-context Win32 app.
- The key path is a public contract. Admins deploy it, so renaming it breaks their rollouts.
- Standard users cannot write `HKLM\SOFTWARE\Policies`: checked 2026-11-11 on Windows, a write
  as a standard user fails with "Requested registry access is not allowed".
