---
paths:
  - "packaging/velopack/build.ps1"
  - "Directory.Build.props"
  - "src/Entra-PIM-Manager.Core/Configuration/AppPaths.cs"
  - "src/Entra-PIM-Manager.App.Avalonia/Services/AutostartService.cs"
---

Renaming the app changes only its display name (`--packTitle`, `Product`, UI text). These stay as
they are: `--packId Entra-PIM-Manager`, the data directory `%LocalAppData%\junis\Entra-PIM-Manager`,
the `HKCU\…\Run` value name, and the exe and `.intunewin` file names. A new packId is a new app
that existing installs never update to, and customers' Intune detection rules, install commands
and ASR exclusions name the rest. A rename to "PIM Manager" was analysed and rejected for this
reason.
