---
paths:
  - "src/Entra-PIM-Manager.App.Avalonia/ViewModels/**/*.cs"
---

An exception that escapes an async `[RelayCommand]` kills the tray app: CommunityToolkit.Mvvm 8.2.2
rethrows it on the UI thread, and the app registers no global unhandled-exception handler. Every
async command catches and logs, and leaves the failure visible: a message in the command's own slot
(`SaveScopeFavoritesAsync` sets `ScopeNotice`), or a control that visibly did not change (no new
chip, no "Copied"). Never borrow another feature's message slot: `ValidationMessage` carries the
activation's own errors. This includes file writes (favourites, settings) and the clipboard, not
only Graph and ARM calls.
