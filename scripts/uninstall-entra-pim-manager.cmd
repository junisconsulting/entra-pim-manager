@echo off
rem Silent uninstall of Entra PIM Manager for Intune and other deployment tools.
rem Their uninstall command does not expand environment variables, so the path to the
rem per-user Update.exe is resolved here. Removes the app AND its data folder (accounts,
rem token cache, configuration). A batch file waits for the GUI-subsystem Update.exe,
rem so its exit code (0 success, 1 failure) is what the caller sees.
"%LOCALAPPDATA%\Entra-PIM-Manager\Update.exe" --uninstall --silent
exit /b %ERRORLEVEL%
