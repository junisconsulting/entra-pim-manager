# Manuelle Test-Checkliste — Entra PIM Manager v1

Diese Checkliste deckt die Integrationstests ab, die **nicht** automatisiert
ausgeführt werden (echte Tenant-Calls, WAM-Broker, Velopack-Install). Sie wird
vor einem Release vollständig durchgearbeitet und abgezeichnet.

- **Tester:** ______________________
- **Datum:** ______________________
- **Build / Version:** ______________________
- **Tenant:** ______________________ (Test-Tenant, nicht Produktiv)

> Automatisiert getestet (CI, nicht hier): Unit-Tests `Entra-PIM-Manager.Core`,
> Coverage-Gate ≥ 70 %, Build mit `-warnaserror`.

---

## 0. Voraussetzungen

- [ ] Entra App Registration existiert (siehe `docs/app-registration-setup.md`).
- [ ] Delegated Permissions gesetzt **und** Admin-Consent erteilt.
- [ ] `TenantId` / `ClientId` liegen vor.
- [ ] Testkonto hat mindestens eine eligible Directory-Rolle **und** eine
      eligible PIM-for-Groups-Mitgliedschaft (Low-Risk, für Aktivierungstests).
- [ ] Testgerät: Windows 10 1809+ / Windows 11, kein lokales Admin-Recht nötig.

---

## 1. Auth-Layer (MSAL + WAM) — Phase 1

- [ ] Erster Start ohne Cache: WAM-Prompt erscheint (System-Dialog, **kein**
      eingebetteter Browser).
- [ ] Anmeldung erfolgreich; Tray-Tooltip/Status zeigt den angemeldeten Nutzer.
- [ ] App beenden und neu starten: Anmeldung erfolgt **silent** (kein Prompt).
- [ ] Log prüfen: `Token acquired silently via Cache` o. ä. — `TokenSource`
      ist nach dem 2. Start `Cache`.
- [ ] „Sign out" im Tray-Menü: Konten entfernt, `msal.cache` gelöscht,
      Status auf „Nicht angemeldet".
- [ ] Nach Sign-out erneuter Sign-in: WAM-Prompt erscheint wieder.

## 1b. Sovereign Cloud (Entra China / 21Vianet) — ab 0.4.2

> Voraussetzung: eine **zweite** App Registration, angelegt unter `portal.azure.cn`
> in einem China-Tenant (siehe `docs/app-registration-setup.md` §7). Die
> Global-Registration funktioniert dort nicht — National Clouds sind isolierte
> Instanzen.
>
> **Punkt 5 zuerst ausführen.** Er ist der eigentliche Machbarkeitsnachweis:
> Microsoft garantiert keine Feature-Parität zwischen den Clouds, und ob die
> PIM-Endpoints auf `microsoftgraph.chinacloudapi.cn` existieren, ist nicht
> dokumentiert. Schlägt er fehl, ist der Rest hinfällig.

- [ ] Nur Global konfiguriert → im „Add account"-Panel ist **keine** Cloud-Auswahl
      sichtbar.
- [ ] China-ClientId unter **Settings → APP REGISTRATION → Entra China (21Vianet)**
      speichern → Restart-Banner erscheint; nach Neustart zeigt „Add account" die
      Cloud-Auswahl mit beiden Einträgen.
- [ ] Cloud „Entra China" + **leeres** Tenant-Feld → der Account landet mit
      `"cloud": 1` in `accounts.json` (Regression: bis 0.4.1 wurde still Global
      enrolled).
- [ ] Log prüfen: die MSAL-Authority beim China-Sign-in ist
      `login.partner.microsoftonline.cn`, nicht `login.microsoftonline.com`.
- [ ] **Eligibilities des China-Tenants werden gelistet** (Directory-Rollen und/oder
      PIM-for-Groups). ⇦ Machbarkeitsnachweis
- [ ] Aktivierung **und** Deaktivierung einer China-Rolle erfolgreich.
- [ ] Global- und China-Account gleichzeitig enrolled: beide Tenant-Gruppen
      erscheinen, Wechsel zwischen ihnen ohne erneute Anmeldung.
- [ ] Getrennte Cache-Dateien vorhanden: `msal.cache` **und** `msal-china.cache`.
- [ ] China-Account entfernen lässt den Global-Account und dessen Cache unberührt.
- [ ] Device-Code-Pfad für China (Advanced) funktioniert — Fallback, falls WAM
      gegen 21Vianet nicht greift.
- [ ] Falsche ClientId für eine Cloud eingetragen → verständliche Meldung
      („unknown in the selected cloud…"), **kein** roher `AADSTS700016`.
- [ ] Settings zeigt das grüne **Verified**-Badge erst, wenn **beide** konfigurierten
      Registrations je eine erfolgreiche Anmeldung hatten.

## 2. Read-Pfade (Eligibilities & Active Assignments) — Phase 2

- [ ] „Eligible Roles…" öffnet die Liste; Directory-Rollen werden angezeigt.
- [ ] PIM-for-Groups-Einträge erscheinen in derselben Liste.
- [ ] Group-Einträge tragen das Warn-Badge (Mitgliedschaft ≠ Rollenaktivierung).
- [ ] Role-assignable Groups sind als solche erkennbar markiert.
- [ ] Anzeigenamen der Gruppen sind aufgelöst (keine rohen GUIDs).
- [ ] Filter-Textfeld grenzt die Liste korrekt ein.
- [ ] „Active Assignments…" zeigt nur aktivierte Zuweisungen (keine permanenten).

## 3. Aktivierung & Deaktivierung (Write-Pfade) — Phase 3

- [ ] Aktivierung einer Low-Risk-Directory-Rolle ohne Pflichtfelder: erfolgreich.
- [ ] Dauer-Slider ist auf die `MaximumDuration` der Policy gedeckelt.
- [ ] Policy verlangt Begründung → Feld ist sichtbar und Pflicht; leeres Feld
      wird inline abgelehnt.
- [ ] Policy verlangt Ticket → Ticket-Nummer und -System sind Pflichtfelder.
- [ ] Aktivierung einer PIM-for-Groups-Mitgliedschaft: erfolgreich.
- [ ] Bei Group-Aktivierung mit Ticket: Ticket landet in der Begründung
      (Group-Surface hat kein `ticketInfo`-Feld).
- [ ] Rolle mit Genehmigungspflicht: Status „Genehmigung angefordert"
      (PendingApproval), Toast entsprechend.
- [ ] Erfolgs-Toast erscheint; Liste/Countdown aktualisieren sich.
- [ ] Live-Countdown der aktiven Zuweisung zählt herunter.
- [ ] Deaktivierung einer aktiven Rolle: erfolgreich, verschwindet aus der Liste.
- [ ] Deaktivierung einer aktiven Group-Mitgliedschaft: erfolgreich.

## 4. Tray-App & UI — Phase 4

- [ ] App startet ohne sichtbares Hauptfenster, nur Tray-Icon.
- [ ] Kontextmenü: Eligible Roles…, Active Assignments…, Refresh, Sign out, Quit.
- [ ] Tray-Icon-Variante bei ≥ 1 aktiver Rolle ist sichtbar anders.
- [ ] „Refresh" aktualisiert beide Listen.
- [ ] Hintergrund-Refresh (~60 s) aktualisiert die Daten ohne Nutzeraktion.
- [ ] „Expiry soon"-Toast erscheint < 5 min vor Ablauf einer aktiven Zuweisung.
- [ ] „Quit" beendet den Prozess vollständig (Tray-Icon verschwindet).
- [ ] In der UI erscheinen ausschließlich gemappte, freundliche Meldungen —
      keine Stacktraces, keine rohen Graph-Fehlertexte.

## 5. Packaging, Install & Auto-Update — Phase 5

- [ ] `pwsh ./packaging/velopack/build.ps1 -Version 0.1.0` erzeugt ein Paket.
- [ ] Installation als **Standard-Nutzer ohne UAC-Prompt** möglich.
- [ ] Installationsziel ist `%LocalAppData%` — **kein** `Program Files`, **kein** `HKLM`.
- [ ] Kein Windows-Dienst und kein als SYSTEM laufender Scheduled Task angelegt.
- [ ] Autostart-Eintrag unter `HKCU\…\Run` ist nach Installation gesetzt.
- [ ] Erststart ohne Konfiguration: `ConfigurationWindow` fragt TenantId/ClientId ab.
- [ ] Eingegebene Werte landen in `%LocalAppData%\Entra-PIM-Manager\appsettings.local.json`.
- [ ] Höhere Version paketieren → App erkennt das Update und wendet es beim
      nächsten Start an (laufende Sitzung wird nicht unterbrochen).
- [ ] Deinstallation entfernt den `HKCU\…\Run`-Autostart-Eintrag.

### 5b. In-Place-Upgrade von der Vorversion

> Diese Punkte prüfen die **wirksame Konfiguration nach dem Update**, nicht die
> Existenz der Konfigurationsdatei. Der frühere Wortlaut („Konfiguration überlebt
> das Update — liegt außerhalb des Install-Verzeichnisses") wurde in 0.4.2 mit
> gutem Gewissen abgehakt, während die App tatsächlich in den Einrichtungs-CTA
> startete: die Datei hatte überlebt, aber das neu ausgelieferte `appsettings.json`
> überstimmte sie. Velopack ersetzt das Installationsverzeichnis — jeder **neue**
> Schlüssel darin kann einen Nutzerwert aus einer unteren Config-Ebene verdecken,
> weil `IConfiguration` pro Schlüssel merged. Deshalb wird hier ab jetzt das
> beobachtbare Ergebnis geprüft.

Ausgangslage: Vorversion **installiert und vollständig eingerichtet** (ClientId
gesetzt, mindestens ein Konto enrolled), dann die neue Version darüber installieren.

- [ ] Nach dem Update erscheint **kein** Einrichtungs-CTA — die App startet direkt
      in die normale Ansicht.
- [ ] Alle zuvor enrollten Konten sind noch da, in unveränderter Reihenfolge.
- [ ] Eligibilities und aktive Zuweisungen laden ohne erneute Anmeldung.
- [ ] Settings → APP REGISTRATION: die ClientId der Vorversion ist vorbefüllt,
      **nicht** der Platzhalter `YOUR-CLIENT-ID-HERE`.
- [ ] Ein Wert, der nur in der Vorversion existierte (z. B. handgepflegte
      `AllowedTenants`), ist noch wirksam.

## 6. Fehlerpfade & Hardening — Phase 6

- [ ] **Offline:** Netzwerk trennen, „Refresh" auslösen → Statuszeile meldet
      „Keine Verbindung zu Microsoft Entra…", kein Absturz.
- [ ] **Offline während Aktivierung:** Aktivierungs-Dialog bleibt offen mit
      Verbindungs-Hinweis, Eingaben bleiben erhalten.
- [ ] **Timeout:** Ein Graph-Call > 30 s wird abgebrochen; Meldung
      „Die Anfrage hat zu lange gedauert…".
- [ ] **Throttling (429):** Bei gehäuften Anfragen erscheint eine
      „Zu viele Anfragen…"-Meldung (kein harter Fehler).
- [ ] **Abgelaufene Eligibility:** Aktivierung einer zwischenzeitlich entfernten
      Berechtigung → Hinweis „Liste aktualisieren".
- [ ] **Claims-Challenge / Conditional Access:** Aktivierung einer Rolle, die
      eine MFA-/Auth-Context-Step-up erfordert → WAM-Re-Auth-Prompt erscheint,
      Aktivierung danach erfolgreich.
- [ ] Nach jedem Fehlerfall ist die App weiter bedienbar (kein eingefrorenes UI).

## 7. Sicherheit & Logs

Logdateien: `%LocalAppData%\Entra-PIM-Manager\logs\pim-manager-*.log`

- [ ] Logs enthalten **keine** Access-/ID-/Refresh-Tokens (auch nicht in DEBUG).
- [ ] Logs enthalten **keine** Begründungstexte.
- [ ] Ticket-Nummern dürfen vorkommen — sind nicht sensibel.
- [ ] Nutzer erscheinen nur als `oid` (Object-ID), nie als UPN/Mail im Klartext.
- [ ] `appsettings.local.json` mit echten IDs ist **nicht** eingecheckt.
- [ ] App-Manifest: `requestedExecutionLevel level="asInvoker"`.

---

## Abzeichnung

| Abschnitt           | Ergebnis (OK / Fehler) | Bemerkung |
| ------------------- | ---------------------- | --------- |
| 1 Auth              |                        |           |
| 1b Sovereign Cloud  |                        |           |
| 2 Read-Pfade        |                        |           |
| 3 Aktivierung       |                        |           |
| 4 Tray & UI         |                        |           |
| 5 Packaging         |                        |           |
| 5b In-Place-Upgrade |                        |           |
| 6 Fehlerpfade       |                        |           |
| 7 Sicherheit & Logs |                        |           |

**Freigabe für Release:** ☐ ja  ☐ nein — Unterschrift: ______________________
