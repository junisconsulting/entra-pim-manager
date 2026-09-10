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

> **Cross-Build von Linux aus ist möglich:** `vpk pack` zielt *standardmäßig* auf das
> Host-Betriebssystem — auf Linux entsteht also ein `.AppImage`. Mit vorangestellter
> OS-Direktive baut dieselbe CLI aber ein Windows-Paket:
>
> ```bash
> vpk '[win]' pack --channel win --packId Entra-PIM-Manager --packVersion X.Y.Z \
>     --packDir <publish-dir> --mainExe Entra-PIM-Manager.exe --shortcuts StartMenuRoot
> ```
>
> Die Direktive muss in der Shell gequotet werden (`'[win]'`) — ungequotet frisst die
> Shell die Klammern und `vpk` fällt auf den Host-Zielmodus zurück. Verifiziert mit
> vpk 1.2.0: *"Directive enabled for cross-compiling from Linux (current os) to Windows."*
>
> Das **Code-Signing** bleibt Windows-gebunden (`signtool`). Ein signiertes, promotable
> Release-Artefakt entsteht daher weiterhin nur auf Windows bzw. in der Release-CI;
> der Linux-Cross-Build ist für lokales Testen des Paketformats gedacht.

## Installationsmodell

Das erzeugte Paket installiert **per-user** nach `%LocalAppData%\Entra-PIM-Manager` —
ohne UAC, ohne Schreibzugriff auf `HKLM` oder `Program Files`, ohne Windows-Dienst
und ohne Scheduled Task. Autostart wird beim ersten Start über den
`HKCU`-Run-Key gesetzt (Velopack-`OnFirstRun`-Hook).

## Deinstallation

Velopack entfernt nur, was es selbst angelegt hat: das Installationsverzeichnis, die
Verknüpfungen und den Uninstall-Registry-Key. Vom Datenordner und vom Run-Key weiß es
nichts — beide räumt der `OnBeforeUninstallFastCallback`-Hook in `Program.cs` weg:

- `%LocalAppData%\junis\Entra-PIM-Manager` samt `settings.json`, `accounts.json`,
  `favorites.json`, `scope-favorites.json`, `appsettings.local.json`, den
  **MSAL-Token-Caches** und `logs\`
- der `HKCU`-Run-Key-Wert `Entra PIM Manager`
- der Hersteller-Ordner `%LocalAppData%\junis`, aber nur wenn er danach leer ist
  (nicht-rekursives `Directory.Delete`, das bei fremden Daten wirft statt sie zu löschen)

Der Hook feuert **ausschließlich** bei `--veloapp-uninstall`. Ein Update läuft über den
separaten `--veloapp-updated`-Hook (in `VelopackApp.Run()` ein eigener Dictionary-Eintrag),
Konfiguration und Anmeldungen überleben Updates also. Velopack gibt dem Hook 30 Sekunden
und wertet eine geworfene Exception als **gescheiterte Deinstallation** (`Process.Exit(-1)`) —
deshalb ist jeder Schritt best effort und schluckt seine Fehler.

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

## Unbeaufsichtigte Installation

`Setup.exe --silent` installiert ohne Dialoge — **startet die App danach aber nicht**.
Nachgewiesen am Velopack-Setup-Log (1.2.0, Windows 11, 2026-09-09): der einzige
Prozessstart im gesamten Lauf ist der Hook `--veloapp-install <version>`, der in
`Program.Main` sofort wieder aussteigt; direkt danach meldet das Log
`Installation completed successfully!`.

Konsequenz für Rollouts: Der in der `setup.exe` dokumentierte Passthrough

```text
EXE_ARGS   Arguments to pass to the started executable. Must be preceded by '--'.
```

ist im Silent-Mode **wirkungslos** — er gilt für den normalen App-Start, den Silent
überspringt. Eine unbeaufsichtigte Konfiguration läuft deshalb als zweiter Schritt über
die installierte exe selbst (`--tenant-id` / `--client-id`), siehe
`docs/unattended-deployment.md`. Nicht erneut über `Setup.exe -- …` versuchen.

Der Erststart-Dialog aus dem vorigen Abschnitt erscheint davon unberührt beim ersten
interaktiven Start: sein Auslöser ist allein der `.setup-pending`-Marker.

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
einmal kurz nach dem Start, danach täglich, und auf Wunsch sofort über
**Settings → Updates → „Check for updates"**. Umgesetzt über Velopacks
`UpdateManager` mit einer `GithubSource` auf das öffentliche Repo (kein Token).
Wird eine neuere Version gefunden, erscheint ein Popup; auf Wunsch wird das
Update im Hintergrund heruntergeladen und nach „Jetzt neu starten" angewendet
(oder still beim nächsten Start). Der Nutzer kann das Feature unter
**Settings → Updates** ein-/ausschalten (`AutomaticUpdatesEnabled`, Default an).

Der Schalter gilt nur für das **ungefragte** Prüfen; der Button prüft auch bei
ausgeschaltetem Schalter, und er ignoriert die Session-Unterdrückung eines mit
„Later" weggeklickten Updates.

**Ein Release wird erst ab 72 h Alter angeboten** (`MinimumReleaseAge` in
`UpdateService`) — unsignierte Builds werden von reputationsbasierten Kontrollen
blockiert, ein frisches Paket würde also installiert und startete dann nicht.
Deshalb unterscheidet `UpdateCheckOutcome` vier Ausgänge statt eines `null`:
`UpToDate`, `Deferred`, `Failed`, `NotSupported`. Der Button zeigt jeden davon
mit eigenem Text. Das ist kein Komfort: „aktuellste Version" zu melden, während
ein Release nur zurückgehalten wird oder GitHub gar nicht erreichbar war, ist
eine Falschaussage, auf die der Nutzer hin aufhört zu suchen — genau daran
scheiterte der Button vor 0.6.0.

Die Prüfung funktioniert **nur in einer echten Velopack-Installation** — bei
`dotnet run` oder dem nackten `artifacts/win-x64`-Build ist `UpdateManager.IsInstalled`
false und der Updater bleibt inaktiv.

> **Wichtig beim Veröffentlichen:** `GithubSource` liest den **Velopack-Feed**, nicht
> den Installer. An jedes GitHub-Release (Tag `vX.Y.Z`) müssen die Dateien aus
> `packaging/velopack/releases/` angehängt werden — mindestens `releases.win.json`
> (und `RELEASES`), `Entra-PIM-Manager-{ver}-full.nupkg` sowie das `-delta.nupkg`,
> zusätzlich zu `…-Setup.exe`/`…-Portable.zip`. **Lädt man nur die `.exe` hoch,
> findet die In-App-Prüfung nichts.**
