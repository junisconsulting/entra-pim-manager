---
paths:
  - "src/Entra-PIM-Manager.App.Avalonia/ViewModels/**/*.cs"
---

An exception that escapes an async `[RelayCommand]` kills the tray app: CommunityToolkit.Mvvm 8.2.2
rethrows it on the UI thread, and the app registers no global unhandled-exception handler. Every
async command catches, logs and tells the user, like `SaveScopeFavoritesAsync` in
`ActivationPanelViewModel` does. That includes file writes (favourites, settings) and the clipboard,
not only Graph and ARM calls.
