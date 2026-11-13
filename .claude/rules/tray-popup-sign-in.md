---
paths:
  - "src/Entra-PIM-Manager.App.Avalonia/Tray/TrayPopupController.cs"
  - "src/Entra-PIM-Manager.App.Avalonia/Views/TrayPopupWindow.axaml.cs"
  - "src/Entra-PIM-Manager.Core/Auth/MsalAuthService.cs"
---

`TrayPopupWindow` hides whenever it loses focus (CLAUDE.md, Code standards), and a WAM window takes
the focus. A WAM sign-in the app starts by itself therefore runs before the panel is shown, never
over it: `TrayPopupController.OpenFirstTimeAsync` waits for a refresh that is still running, lifts
the startup hold-back (`IAuthService.AllowInteractiveSignIn`) and refreshes, then `Show` shows the
panel. Found in the review of #20 (2026-11-12): showing the panel first let the WAM window close it.

`Show()` without user input (the startup open of #15, the second-instance signal) is not entitled to
the foreground under the `SetForegroundWindow` rules. If Windows refuses, the Topmost panel shows
unfocused, never deactivates, and neither Escape nor a click elsewhere hides it; only the tray icon
does. Field test at logon, typing in Notepad meanwhile (2026-11-13): Windows granted the focus. If a
report of a stuck panel comes in, this is the first suspect.
