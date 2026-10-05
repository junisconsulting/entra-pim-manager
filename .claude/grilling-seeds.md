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
