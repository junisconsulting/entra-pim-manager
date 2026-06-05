# Velopack-Packaging

Dieses Verzeichnis enthält das Build-Skript für die ausgelieferten Entra PIM Manager-Pakete.

## Paket bauen

```powershell
pwsh ./packaging/velopack/build.ps1 -Version 0.1.0
```

Das Skript:

1. installiert die Velopack-CLI (`vpk`) bzw. richtet sie auf **dieselbe Version
   wie die `Velopack`-Library** im `.csproj` aus (die Version wird dynamisch aus dem
   Projekt gelesen) — eine Versions­diskrepanz zwischen `vpk` und der Runtime-Library
   kann ein Paket-/Installer-Format erzeugen, das die installierte App nicht versteht,
2. publiziert die App **self-contained** für `win-x64` (das Endgerät braucht keine
   installierte .NET-Runtime — ein Per-User-Install kann ohne Admin keine Runtime
   nachinstallieren),
3. packt das Ergebnis mit `vpk pack` nach `packaging/velopack/releases/`.

`publish/` und `releases/` sind gitignored.

> **Nur auf Windows baubar:** Velopack packt immer nur für das Host-Betriebssystem
> (`vpk pack` auf Linux erzeugt z. B. ein `.AppImage`). Der Windows-`Setup.exe` muss
> daher auf einer Windows-Maschine gebaut werden — ein Cross-Build des Pakets von
> Linux/macOS aus ist nicht möglich.

## Installationsmodell

Das erzeugte Paket installiert **per-user** nach `%LocalAppData%\Entra-PIM-Manager` —
ohne UAC, ohne Schreibzugriff auf `HKLM` oder `Program Files`, ohne Windows-Dienst
und ohne Scheduled Task. Autostart wird beim ersten Start über den
`HKCU`-Run-Key gesetzt (Velopack-`OnFirstRun`-Hook) und beim Deinstallieren wieder
entfernt (`OnBeforeUninstallFastCallback`).

## Erststart-Einrichtung (Autostart & Startmenü)

Velopack hat **keinen interaktiven Installer** — die `Setup.exe` läuft still. Die
Wahlmöglichkeit für Autostart und Startmenü-Eintrag wird daher über einen
**einmaligen Setup-Dialog beim ersten Start** angeboten (nicht im Installer):

1. Der `OnFirstRun`-Hook aktiviert den Autostart (sicherer Default) und legt einen
   Marker (`%LocalAppData%\Entra-PIM-Manager\.setup-pending`) ab.
2. Sobald die UI läuft, zeigt `FirstRunSetupController` den Dialog mit zwei
   Schaltern (Autostart / Startmenü-Eintrag, beide standardmäßig an).
3. Beim Bestätigen — oder beim Schließen — wird die Auswahl angewendet
   (Autostart über den `HKCU`-Run-Key, der Startmenü-Eintrag über das direkte
   Schreiben/Löschen der `.lnk`) und der Marker gelöscht. Der Dialog erscheint
   dadurch genau einmal pro Installation.

Beide Optionen lassen sich danach jederzeit unter **Settings → Behavior**
umschalten. Wie beim Autostart ist auch beim Startmenü-Eintrag das Artefakt selbst
(die `.lnk`-Datei) die Wahrheitsquelle — nichts davon liegt in `settings.json`.

> **Hinweis:** Der Startmenü-Eintrag wird als `.lnk` direkt am Installer-Pfad
> (`…\Start Menu\Programs\{ProductName}.lnk`) verwaltet — Entfernen per `File.Delete`,
> Anlegen per `WScript.Shell`. Velopacks eigene `Shortcuts`-Runtime-API wird bewusst
> **nicht** genutzt: sie ist `[Obsolete]` und liest beim Anlegen/Löschen zunächst das
> lokale `.nupkg`; schlägt das fehl, macht sie still gar nichts (so blieb der
> Settings-Toggle wirkungslos). Eine `AppUserModelId` setzt auch Velopacks Runtime-API
> nicht, und der Toast-Stack registriert sich selbst über die Registry — die direkte
> Verwaltung hat hier also keinen Nachteil.

## Code-Signing

Aktuell werden **unsignierte** Pakete gebaut. Sobald ein Code-Signing-Zertifikat
für junis GmbH verfügbar ist, wird signiert über:

```powershell
pwsh ./packaging/velopack/build.ps1 -Version 0.1.0 -SignParams "/a /f cert.pfx /p <pwd> /fd sha256 /tr <timestamp-url>"
```

`-SignParams` wird an `vpk pack --signParams` durchgereicht. **Release-Artefakte
müssen signiert sein**, bevor sie in einen Release-Branch promotet werden — die
Build-Pipeline sollte unsignierte Artefakte an dieser Stelle blockieren.

## Auto-Update

Die App prüft die **GitHub-Releases** des Projekts auf eine neuere Version —
einmal kurz nach dem Start und danach täglich. Umgesetzt über Velopacks
`UpdateManager` mit einer `GithubSource` auf das öffentliche Repo (kein Token).
Wird eine neuere Version gefunden, erscheint ein Popup; auf Wunsch wird das
Update im Hintergrund heruntergeladen und nach „Jetzt neu starten" angewendet
(oder still beim nächsten Start). Der Nutzer kann das Feature unter
**Settings → Updates** ein-/ausschalten (`AutomaticUpdatesEnabled`, Default an).

Die Prüfung funktioniert **nur in einer echten Velopack-Installation** — bei
`dotnet run` oder dem nackten `artifacts/win-x64`-Build ist `UpdateManager.IsInstalled`
false und der Updater bleibt inaktiv.

> **Wichtig beim Veröffentlichen:** `GithubSource` liest den **Velopack-Feed**, nicht
> den Installer. An jedes GitHub-Release (Tag `vX.Y.Z`) müssen die Dateien aus
> `packaging/velopack/releases/` angehängt werden — mindestens `releases.win.json`
> (und `RELEASES`), `Entra-PIM-Manager-{ver}-full.nupkg` sowie das `-delta.nupkg`,
> zusätzlich zu `…-Setup.exe`/`…-Portable.zip`. **Lädt man nur die `.exe` hoch,
> findet die In-App-Prüfung nichts.**
