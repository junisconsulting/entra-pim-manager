---
paths:
  - "src/Entra-PIM-Manager.App.Avalonia/ViewModels/**/*.cs"
---

An exception that escapes an async `[RelayCommand]` is rethrown on the UI thread
(CommunityToolkit.Mvvm 8.2.2). `App`'s `Dispatcher.UIThread.UnhandledException` handler logs it and
shows a generic "Unexpected error" toast (at most one a minute), so the tray keeps running — but
that is only the net: it cannot say what failed or undo a half-finished change, and it does not
cover a discarded task (`_ = SaveAsync()`, only logged when collected), the synchronous startup in
`OnFrameworkInitializationCompleted`, or the shutdown. Whether exceptions from the native tray
callbacks reach it is unverified. Every async command and every discarded task still catches and
logs, and leaves the failure visible: a message in the command's own slot
(`SaveScopeFavoritesAsync` sets `ScopeNotice`), or a control that visibly did not change (no new
chip, no "Copied"). Never borrow another feature's message slot: `ValidationMessage` carries the
activation's own errors. This includes file writes (favourites, settings) and the clipboard, not
only Graph and ARM calls.
