# Ship Intune as a Win32 app (.intunewin), not as an MSI

Every release attaches an `.intunewin` alongside the Velopack Setup.exe. It wraps the same Setup.exe
together with the install and uninstall scripts, and an admin uploads it into their own tenant as a
Win32 app with install behaviour **User**. The package holds no tenant or client id; those go into
the install command in Intune, so one asset serves every customer.

## Considered options

- **Velopack MSI** (`vpk pack --msi --instLocation PerUser`). It is one flag away and Intune-native as
  a LOB app, which is why it will be suggested again. Rejected because an MSI install and a Setup.exe
  install exclude each other on the same machine: `Update.exe --uninstall` refuses an MSI-installed
  app ("use msiexec"). Two install paths with separate uninstall paths would have to be maintained
  and documented.
- **MSIX / LOB app.** It replaces Velopack's install model and with it the auto-updater.
- **Automated upload into a tenant via Graph.** It needs tenant credentials in a public CI and serves
  exactly one tenant.
- **Documentation only, no asset.** Wrapping Setup.exe yourself takes a minute, but the asset
  guarantees that installer and scripts match the release. The documented install, uninstall and
  detection values are shipped either way.

## Consequences

- The package is built with Microsoft's IntuneWinAppUtil, downloaded in CI pinned by SHA256. Its
  license permits use but not redistribution, so the tool is never committed or attached.
- Velopack auto-update stays on. Self-updates rewrite `DisplayVersion` in the HKCU uninstall key, so
  the Intune detection rule must be "greater than or equal", never "equals"; with "equals" the
  first self-update makes the app look missing, and the silent reinstall downgrades it.
