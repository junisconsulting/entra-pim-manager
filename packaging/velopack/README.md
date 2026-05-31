# Velopack-Packaging

Dieses Verzeichnis enthält das Build-Skript für die ausgelieferten Entra PIM Manager-Pakete.

## Paket bauen

```powershell
pwsh ./packaging/velopack/build.ps1 -Version 0.1.0
```

Das Skript:

1. installiert bei Bedarf die Velopack-CLI (`vpk`),
2. publiziert die App **self-contained** für `win-x64` (das Endgerät braucht keine
   installierte .NET-Runtime — ein Per-User-Install kann ohne Admin keine Runtime
   nachinstallieren),
3. packt das Ergebnis mit `vpk pack` nach `packaging/velopack/releases/`.

`publish/` und `releases/` sind gitignored.

## Installationsmodell

Das erzeugte Paket installiert **per-user** nach `%LocalAppData%\Entra-PIM-Manager` —
ohne UAC, ohne Schreibzugriff auf `HKLM` oder `Program Files`, ohne Windows-Dienst
und ohne Scheduled Task. Autostart wird beim ersten Start über den
`HKCU`-Run-Key gesetzt (Velopack-`OnFirstRun`-Hook) und beim Deinstallieren wieder
entfernt (`OnBeforeUninstallFastCallback`).

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

Die App prüft beim Start gegen einen GitHub-Releases-Feed. Die Feed-URL kommt aus
`EntraPimManager:UpdateUrl` in der Konfiguration; ist sie leer, wird die Update-Prüfung
übersprungen. Updates werden heruntergeladen und beim nächsten Start angewendet,
damit die laufende Sitzung nicht unterbrochen wird.
