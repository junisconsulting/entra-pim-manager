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
