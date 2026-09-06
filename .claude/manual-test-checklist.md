# Manuelle Test-Checkliste — Entra PIM Manager v1

Diese Checkliste deckt die Integrationstests ab, die **nicht** automatisiert
ausgeführt werden (echte Tenant-Calls, WAM-Broker, Velopack-Install). Sie wird
vor einem Release vollständig durchgearbeitet und abgezeichnet.

- **Tester:** Daniel Hepe
- **Datum:** 2026-09-06
- **Build / Version:** 0.9.0 (getestet als `0.9.0-local.202609061109`)
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
- [ ] App Registration hat die delegierte Berechtigung **Azure Service Management →
      user_impersonation**, Admin-Consent erteilt; Testkonto hat mindestens eine
      eligible Azure-Rolle (Low-Risk, z. B. Reader auf einer Test-Resource-Group).
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

- [ ] Nur ein Global-Eintrag konfiguriert → im „Add account"-Panel ist **kein**
      „Sign in with"-Picker sichtbar.
- [ ] China-Eintrag unter **Settings → TENANTS** hinzufügen (Tenant-ID des
      China-Tenants, China-ClientId, Cloud „Entra China (21Vianet)") → Restart-Banner
      erscheint; nach Neustart zeigt „Add account" den Picker mit beiden Einträgen.
- [ ] China-Eintrag im Picker wählen → der Account landet mit `"cloud": 1` in
      `accounts.json`.
- [ ] Log prüfen: die MSAL-Authority beim China-Sign-in ist
      `login.partner.microsoftonline.cn`, nicht `login.microsoftonline.com`.
- [ ] **Eligibilities des China-Tenants werden gelistet** (Directory-Rollen und/oder
      PIM-for-Groups). ⇦ Machbarkeitsnachweis
- [ ] Aktivierung **und** Deaktivierung einer China-Rolle erfolgreich.
- [ ] Global- und China-Account gleichzeitig enrolled: beide Tenant-Gruppen
      erscheinen, Wechsel zwischen ihnen ohne erneute Anmeldung.
- [ ] Getrennte Cache-Dateien vorhanden: `msal-{global-client-id}.cache` **und**
      `msal-{china-client-id}.cache`.
- [ ] China-Account entfernen lässt den Global-Account und dessen Cache unberührt.
- [ ] Device-Code-Pfad für China (Advanced) funktioniert — Fallback, falls WAM
      gegen 21Vianet nicht greift.
- [ ] Falsche ClientId für eine Cloud eingetragen → verständliche Meldung
      („unknown in the selected cloud…"), **kein** roher `AADSTS700016`.
- [ ] Settings zeigt das grüne **Verified**-Badge erst, wenn **beide** konfigurierten
      Registrations je eine erfolgreiche Anmeldung hatten.

## 1c. App Registrations pro Tenant — ab 0.7.0

> Voraussetzung: zusätzlich zur Multi-Tenant-Registration eine **Single-Tenant**
> App Registration in einem zweiten Test-Tenant (siehe
> `docs/app-registration-setup.md` §1) — gleiche Redirect-URI, Public Client
> Flows an, gleiche sechs Scopes, Admin-Consent nur dort.

- [ ] Settings → TENANTS → „Add a tenant": „Add" erst aktiv, wenn Tenant-ID **und**
      Client-ID GUIDs sind; Label optional.
- [ ] Zwei Einträge mit **derselben** Multi-Tenant-ClientId für zwei Tenants →
      beide erscheinen im „Sign in with"-Picker; Sign-in in beide funktioniert,
      eine gemeinsame `msal-{client-id}.cache`.
- [ ] Eintrag mit der Single-Tenant-ClientId → Sign-in über diesen Eintrag:
      Eligibilities werden gelistet, eigene `msal-{client-id}.cache` entsteht, Log
      zeigt eine Request-Authority mit `/{tenant-id}` (nicht `/organizations`).
- [ ] Aktivierung **und** Deaktivierung einer Rolle über diesen Account erfolgreich.
- [ ] Im WAM-Picker ein Konto wählen, das **nicht** Mitglied/Gast des gewählten
      Tenants ist → verständliche Sign-in-Meldung, **kein** Eintrag in
      `accounts.json`.
- [ ] Account entfernen, dessen ClientId noch von einem anderen Enrollment genutzt
      wird → dessen Cache bleibt; letztes Enrollment einer ClientId entfernen →
      MSAL-Account wird gepurgt.
- [ ] Registration per ✕ auf der Tenant-Karte entfernen → Restart-Banner; nach
      Neustart zeigt die Tenant-Gruppe „Sign-in for this account is no longer
      valid…", und der Account lässt sich in Settings trotzdem entfernen.
- [ ] **Die Karte bleibt dabei stehen**, solange noch ein Konto in diesem Tenant
      angemeldet ist — mit Status „No App Registration — sign-in for this tenant
      will fail". Ihre Konten dürfen **nicht** aus der Ansicht verschwinden.
- [ ] Gleichen Tenant erneut hinzufügen (anderer Label) → ein Eintrag, nicht zwei.
- [ ] Zahnrad auf einer Tenant-Karte → Konfiguration klappt auf, Client-ID, Label
      und Ticketsystem sind vorbelegt, die **Tenant-ID steht nur als Text da**
      (nicht editierbar). Label ändern → Save → Karte aktualisiert, Restart-Banner.
- [ ] Nur das **Ticketsystem** ändern → Save → **kein** Restart-Banner, und die
      Änderung wirkt sofort in der nächsten Aktivierung.
- [ ] Zwei Tenant-Karten gleichzeitig aufklappen → beide zeigen **ihre eigenen**
      Werte; Tippen in der einen verändert die andere nicht.
- [ ] Konfiguration ohne Save wieder zuklappen und erneut öffnen → der verworfene
      Text ist weg, die gespeicherten Werte stehen wieder da.
- [ ] **Verified**-Badge erst, wenn **jede Karte mit Registration** eine
      erfolgreiche Anmeldung hatte. Eine Karte ohne Registration darf das Badge
      **nicht** unterdrücken.
- [ ] **Upgrade von 0.6.x** (Global-ClientId + enrollte Accounts): nach dem ersten
      Start je Tenant ein Eintrag mit der alten ClientId, Log meldet „Migrated
      legacy client id…", `msal.cache` wurde zu `msal-{client-id}.cache`, **kein**
      erneuter Sign-in nötig; `AppRegistrations`/`AllowedTenants` sind aus
      `appsettings.local.json` verschwunden.
- [ ] **Upgrade von 0.6.x ohne Accounts** (nur ClientId): Log-Warnung „…had no
      enrolled or whitelisted tenant and was removed…", First-Run-CTA erscheint.

### Tenant-Baum — ab 0.9.0

- [ ] Zwei Konten im **selben** Tenant → **eine** Karte mit beiden Zeilen darunter,
      nicht zwei Karten.
- [ ] „+ Add account…" auf einer Karte → das Anmelde-Panel öffnet **ohne**
      „Sign in with"-Auswahl; angemeldet wird genau dieser Tenant.
- [ ] Frisch hinzugefügten Tenant vor dem Neustart: „+ Add account…" ist
      **deaktiviert**, der Tooltip nennt den Grund („Restart the app to sign in to
      this tenant"). Nach dem Neustart ist der Button aktiv.
- [ ] Tenant-Karte am Griff (⋮⋮) verschieben → die Reihenfolge der Gruppen im
      Popup folgt; Konten innerhalb des Tenants behalten ihre Reihenfolge; nach
      Neustart ist die Reihenfolge noch da.
- [ ] Auf eine Karte **ohne** Konten ziehen → wird abgelehnt (kein Move-Cursor).
      Eine Karte ohne Konten hat selbst keinen Griff.
- [ ] **Alias umbenennen und dabei den 60-Sekunden-Refresh abwarten**, ohne Enter
      zu drücken: Der Cursor bleibt im Feld, der getippte Text bleibt stehen.
      (Ein Neuaufbau der Liste statt eines Abgleichs würde hier den Fokus
      wegnehmen — und das nächste Escape das ganze Popup schließen.)
- [ ] Ohne jede Konfiguration starten → Settings zeigt die TENANTS-Karte
      aufgeklappt mit dem „Add a tenant"-Formular, nicht eine leere Liste.

## 1d. Sektionen in der Eligibility-Liste — ab 0.9.0

> Hintergrund: Bei mehreren hundert Eligibilities war die Liste beim Öffnen
> unendlich lang, und ein Tenant weiter unten nur nach langem Scrollen erreichbar.

- [ ] Popup öffnen → **alle Tenant-Gruppen sind zugeklappt**; sichtbar sind nur
      PINNED, RECENT und je eine Zeile pro Tenant. Ein Tenant weiter unten ist
      ohne Scrollen erreichbar.
- [ ] Tenant aufklappen → **eine Zeile je Kind** mit Zähler: „DIRECTORY ROLES",
      „ADMINISTRATIVE UNITS", „GROUPS", „AZURE ROLES". Kinder, die es in
      diesem Tenant nicht gibt, erscheinen **nicht**.
- [ ] Sektion aufklappen → die Zeilen erscheinen; **bei „AZURE ROLES" liegen
      die Rollenknoten („Owner · 12 scopes") darin**, nicht daneben.
      > Für 0.9.0 nur mit wenigen Azure-Eligibilities geprüft. Der Fall, für den die
      > Faltung gebaut wurde — mehrere hundert Zuweisungen —, ist bewusst auf nach dem
      > Release verschoben (siehe `docs/engineering-backlog.md`).
- [ ] Tenant mit **wenigen** Eligibilities (≤ 12) aufklappen → seine Sektionen
      sind bereits offen; man klickt nicht zweimal für fünf Rollen.
- [ ] **Aktive Rolle in einer zugeklappten Sektion:** die Sektionszeile zeigt das
      grüne „n active"-Abzeichen. Zuklappen darf nicht verbergen, dass gerade
      etwas Rechte gewährt.
- [ ] Sektion aufklappen, Popup schließen und wieder öffnen bzw. den
      60-Sekunden-Refresh abwarten → die Sektion ist noch offen.
- [ ] **Suche** nach einem Rollennamen → passende Tenants **und** Sektionen
      öffnen sich selbst, die Zähler stimmen; Sektionen ohne Treffer verschwinden.
      Suchfeld leeren → alles klappt wieder zu wie vorher.
- [ ] **AU-beschränkte Directory-Rolle** (Voraussetzung: eine Rolle auf eine
      Administrative Unit zugewiesen): erscheint **nicht** unter „DIRECTORY
      ROLES", sondern unter „ADMINISTRATIVE UNITS", und die Zeile nennt
      „Administrative unit: {Objekt-ID}". Aktivierung darüber funktioniert.
- [ ] Eine Rolle, die es **tenantweit und AU-beschränkt** gibt, erscheint in
      beiden Sektionen — und die Aktivierung trifft jeweils den richtigen Scope
      (im Portal gegenprüfen).
- [ ] **Gruppen:** Mitgliedschaft und Besitz stehen in **einer** Sektion, die
      Zeile unterscheidet sie („Group membership" / „Group ownership").
- [ ] Directory-Rollen zeigen **keine** „Directory role"-Zeile mehr — die
      Sektionsüberschrift sagt es bereits.
- [ ] **Tenant mit Ladefehler** (z. B. China ohne ARM-Consent): eine Sektion in
      einem anderen Tenant aufklappen, den Refresh abwarten → sie bleibt offen.
- [ ] Sektion aufklappen, **dann** suchen, dann Suchfeld leeren → die Sektion ist
      wieder so, wie sie vor der Suche war (nicht pauschal zugeklappt).

### Optik der drei Ebenen

> Hintergrund: Tenant und Rollenzeile sahen typografisch identisch aus, die
> Hierarchie war nur an 14 px Einzug erkennbar.

- [ ] Tenant aufklappen → die drei Ebenen sind **auf einen Blick** zu trennen:
      Tenant groß und fett, Sektion klein in Großbuchstaben, Rollenzeile normal.
      Keine zwei Ebenen sehen gleich aus.
- [ ] Links am aufgeklappten Tenant läuft eine **senkrechte Haarlinie**, an der
      die Sektionen hängen — und sie ist **in Light *und* Dark** sichtbar. (Nur
      hier reicht ein Blick nicht: der Light-Wert ist absichtlich zart.)
- [ ] Der Abstand **zwischen** zwei Tenants ist deutlich größer als der Abstand
      innerhalb eines Tenants.
- [ ] **Zugeklappter Tenant mit aktiver Rolle:** die Kopfzeile zeigt das grüne
      „n active"-Abzeichen — dasselbe, das Sektionen und Rollenknoten schon
      tragen. Zähler und Abzeichen stehen nebeneinander.
- [ ] Die Sektionsüberschrift ist trotz 10 px **noch als klickbar erkennbar**:
      Hover färbt die ganze Zeile, das Chevron bleibt sichtbar.

## 2. Read-Pfade (Eligibilities & Active Assignments) — Phase 2

- [ ] „Eligible Roles…" öffnet die Liste; Directory-Rollen werden angezeigt.
- [ ] PIM-for-Groups-Einträge erscheinen in derselben Liste.
- [ ] Group-Einträge tragen das Warn-Badge (Mitgliedschaft ≠ Rollenaktivierung).
- [ ] **Role-assignable Group aktivieren:** das Aktivierungsformular zeigt den
      Banner „⚠ This group can carry directory roles…" — neben den Bannern für
      Approval und Auth-Context. In der **Liste** steht dazu nichts mehr; die
      Warnung gehört auf den Bestätigungsschirm, nicht ins Durchblättern.
- [ ] Eine Gruppe **ohne** `isAssignableToRole` zeigt den Banner **nicht**.
- [ ] Anzeigenamen der Gruppen sind aufgelöst (keine rohen GUIDs).
- [ ] Azure-Rollen erscheinen in derselben Liste mit Label „Azure resource role" und
      Scope-Zeile („Subscription: …", „Resource group: …", „Management group: …") —
      Management-Group- und Subscription-Scope in einem Rutsch; eine über eine Gruppe
      geerbte Azure-Rolle erscheint ebenfalls.
      **Nicht mit einer frischen Gruppenmitgliedschaft testen:** Azure löst die Gruppen
      des Aufrufers aus einem eigenen Cache auf, eine gerade geänderte Mitgliedschaft
      ist minutenlang unsichtbar (2026-09-04 beobachtet, kein App-Fehler).
- [ ] Mehrere Scopes derselben Azure-Rolle: die Rolle erscheint als **ein** zugeklappter
      Knoten („Owner · 12 scopes"), nicht als zwölf Zeilen. Aufklappen zeigt die Scopes,
      Rolle auf nur einem Scope bleibt eine normale Zeile.
- [ ] Ist ein Scope hinter einem **zugeklappten** Knoten aktiv, trägt der Knoten das
      Badge „1 active".
- [ ] Suche klappt passende Knoten auf und zeigt nur die passenden Scopes; Zähler am
      Knoten stimmt. Filter leeren → Knoten sind wieder zu.
- [ ] Pin-Symbol an einer Zeile: sie erscheint oben unter PINNED, mit Tenant- und
      Scope-Zeile. Erneutes Klicken entfernt sie. Pinnen funktioniert **auch** bei
      einer aktiven (ausgegrauten) Zeile.
- [ ] Nach einer Aktivierung steht die Rolle unter RECENT (max. 3, neueste oben);
      etwas Gepinntes taucht dort **nicht** zusätzlich auf.
- [ ] Während einer Suche sind PINNED und RECENT ausgeblendet.
- [ ] Neustart: Pins sind noch da. Eine gepinnte Eligibility entziehen → die Zeile
      verschwindet oben, keine Karteileiche.
- [ ] Settings → Diagnostics: Zeile „Azure Resource Manager" ist grün.
- [ ] Filter-Textfeld grenzt die Liste korrekt ein.
- [ ] „Active Assignments…" zeigt nur aktivierte Zuweisungen (keine permanenten).

## 3. Aktivierung & Deaktivierung (Write-Pfade) — Phase 3

- [ ] Aktivierung einer Low-Risk-Directory-Rolle ohne Pflichtfelder: erfolgreich.
- [ ] Dauer-Slider ist auf die `MaximumDuration` der Policy gedeckelt.
- [ ] Policy verlangt Begründung → Feld ist sichtbar und Pflicht; leeres Feld
      wird inline abgelehnt.
- [ ] Policy verlangt Ticket → die **Ticket-Nummer** ist Pflicht, das **Ticketsystem
      nicht** (Microsoft verlangt nur die Nummer). Leere Nummer wird inline abgelehnt,
      leeres System nicht.
- [ ] Ticketsystem in Settings → Tenants für den Tenant gesetzt: erscheint
      **ohne Neustart** vorbelegt im Aktivierungspanel und bleibt überschreibbar.
      Anderer Tenant ohne Eintrag → Feld startet leer. Feld leeren und speichern →
      Vorbelegung ist weg.
- [ ] Aktivierung mit Ticketnummer, aber leerem Ticketsystem: wird von Graph bzw. ARM
      angenommen (das Feld wird weggelassen, nicht leer gesendet).
- [ ] Aktivierung einer PIM-for-Groups-Mitgliedschaft: erfolgreich.
- [ ] Bei Group-Aktivierung mit Ticket: Ticket landet in der Begründung
      (Group-Surface hat kein `ticketInfo`-Feld).
- [ ] Rolle mit Genehmigungspflicht: Status „Genehmigung angefordert"
      (PendingApproval), Toast entsprechend.
- [ ] Erfolgs-Toast erscheint; Liste/Countdown aktualisieren sich.
- [ ] Live-Countdown der aktiven Zuweisung zählt herunter.
- [ ] Deaktivierung einer aktiven Rolle: erfolgreich, verschwindet aus der Liste.
- [ ] Deaktivierung einer aktiven Group-Mitgliedschaft: erfolgreich.
- [ ] Aktivierung einer Azure-Rolle: Dauer-Slider entspricht den PIM-Einstellungen
      der Rolle im Azure-Portal; Toast, Pending-Zeile, echte Zeile nach dem Refresh;
      Portal → PIM → My roles → Azure resources → Active bestätigt sie.
- [ ] Aktivierungspanel einer Azure-Rolle zeigt **keinen** „Validate"-Button.
- [ ] Deaktivierung der Azure-Rolle nach ≥ 5 min erfolgreich; davor ist der
      Stop-Button gesperrt.

## 4. Tray-App & UI — Phase 4

- [ ] App startet ohne sichtbares Hauptfenster, nur Tray-Icon.
- [ ] Kontextmenü: Eligible Roles…, Active Assignments…, Refresh, Sign out, Quit.
- [ ] Tray-Icon-Variante bei ≥ 1 aktiver Rolle ist sichtbar anders.
- [ ] „Refresh" aktualisiert beide Listen.
- [ ] Hintergrund-Refresh (~60 s) aktualisiert die Daten ohne Nutzeraktion.
- [ ] „Expiry soon"-Toast erscheint < 5 min vor Ablauf einer aktiven Zuweisung.
- [ ] Ablauf-Fenster einer Azure-Rolle nennt den **Scope** unter der Tenant-/Konto-Zeile
      („Management group: …"). Zwei gleichnamige Rollen auf verschiedenen Scopes:
      „+1 more expiring soon" stimmt, und „Dismiss" auf der einen lässt die andere stehen.
- [ ] „Quit" beendet den Prozess vollständig (Tray-Icon verschwindet).
- [ ] In der UI erscheinen ausschließlich gemappte, freundliche Meldungen —
      keine Stacktraces, keine rohen Graph-Fehlertexte.

## 4b. Netzwerk-Diagnose (Settings → DIAGNOSTICS)

> Hintergrund: In abgeschotteten Kundennetzen öffnet sich das WAM-Fenster,
> bleibt aber weiß — die Login-Seite bzw. deren CDN-Assets sind blockiert,
> während Device-Code (nur Token-Endpoint) funktioniert. Der Netzwerk-Check
> macht das per Klick nachweisbar.

- [ ] **Offenes Netz:** „Run network check" → alle Zeilen grün, pro konfigurierter
      Cloud eine Gruppe plus „Update feed (optional)"; Proxy-Angabe plausibel.
- [ ] „Copy report" legt den Klartext-Report in die Zwischenablage; er enthält
      Hostnamen, Status, Latenzen, App-/OS-Version — **keine** Tokens, UPNs oder
      Proxy-Credentials.
- [ ] **Simulierter Block:** `0.0.0.0 aadcdn.msauth.cn` und `aadcdn.msftauth.cn`
      in die hosts-Datei → beide CDN-Zeilen rot (DNS/Blocked), die
      Authority-Zeilen bleiben grün — das Muster des Kundenfalls. Danach
      hosts-Einträge wieder entfernen.
- [ ] Während des Laufs dreht der Spinner, der Button ist gesperrt; die App
      bleibt bedienbar.

## 4c. Konto-Alias, Suche & Scope-Anzeige — ab 0.9.0

> Hintergrund: Im 380 px breiten Popup ist eine lange UPN nicht lesbar, und wer
> PIM for Azure Resources auf vielen Subscriptions nutzt, findet in einer nach
> Rollennamen gefilterten Liste nichts wieder.

- [ ] Settings → TENANTS: Stift-Icon in einer Kontozeile → die Namenszeile wird
      zum Eingabefeld, der Cursor steht bereits darin, Text ist markiert.
- [ ] „EADM" + Enter → die Zeile zeigt „EADM"; UPN- und Tenant-Zeile darunter
      bleiben **unverändert sichtbar**. Avatar-Initialen folgen dem Alias.
- [ ] **Esc im Alias-Feld bricht nur die Umbenennung ab und schließt das Popup nicht.**
- [ ] Alias leeren + Enter → Anzeigename bzw. UPN kehrt zurück; in
      `%LocalAppData%\Entra-PIM-Manager\settings.json` ist der Eintrag unter
      `AccountAliases` verschwunden.
- [ ] ACTIVE-Karte und ELIGIBILITIES-Gruppenkopf zeigen den Alias **sofort**,
      ohne auf den 60-s-Refresh zu warten.
- [ ] Ohne Alias: UPN wird mittig gekürzt (`external.daniel…onmicrosoft.com`),
      Tooltip zeigt die vollständige UPN — in beiden Ansichten.
- [ ] Alias überlebt einen Neustart **und** „Konto entfernen + neu anmelden".
- [ ] Zwei Enrollments derselben Identität (zwei Tenants oder zwei Clouds):
      nur das umbenannte Konto ändert sich.
- [ ] Suche: Subskriptionsname → nur die passenden Azure-Zeilen; „azure" → alle
      Azure-Rollen; Tenant-Name → die gesamte Tenant-Gruppe. Trefferzähler `(n)`
      im Gruppenkopf stimmt, und das Leeren des Filters stellt das vorherige
      Expand/Collapse-Layout wieder her.
- [ ] ACTIVE-Karte einer Azure-Rolle: Meta-Zeile „Tenant · Subscription: …"
      **ohne** „Azure resource role"; Tooltip zeigt Tenant-GUID und vollständige
      ARM-Scope-ID. Directory-Rolle/Gruppe: Meta-Zeile unverändert, Tooltip = Tenant-GUID.
- [ ] **Sicherheit:** Das Aktivierungspanel zeigt weiterhin die echte Identität
      (Entra-Anzeigename/UPN), nicht den Alias.
- [ ] Der Alias-Text taucht in den Logs **nirgends** auf (auch nicht in DEBUG).

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
      **Hinweis Alters-Gate:** Releases jünger als 72 h werden absichtlich
      nicht angeboten (Reputationsfenster unsignierter Builds; Log-Zeile
      „deferred until 72h"). Für diesen Testpunkt ein Release ≥ 72 h nutzen
      oder das Gate im Log als Deferral verifizieren.
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
- [ ] Settings → TENANTS: die ClientId der Vorversion erscheint als ein
      Eintrag pro enrolltem Tenant (Titel = Tenant-ID · Cloud) mit Verified-Status
      (siehe auch §1c, Upgrade-Punkte).
- [ ] Handgepflegte `AllowedTenants` der Vorversion sind zu Einträgen geworden
      (ein Eintrag je Tenant mit der Global-ClientId) und der Key ist aus der
      Datei verschwunden (§1c, Upgrade-Punkte).

## 5c. „What's new"-Fenster — ab 0.9.0

- [ ] Nach einem Update auf eine neue Version erscheint beim Start **einmal** das
      Fenster mit den Release Notes der neuen Version — Text ist lesbar aufbereitet
      (Überschriften, Aufzählungspunkte, kein rohes Markdown mit `**`).
- [ ] **Kurzfassung, nicht der ganze Text:** unter „New" und „Fixed" steht je Punkt
      **eine Zeile** — der Leitsatz. Die lange Erklärung dahinter steht **nicht** im
      Fenster, sondern nur auf GitHub. Ohne Scrollen erfassbar.
- [ ] Der **Pflicht-Hinweis oben** (Admin-Consent für Azure Service Management) steht
      dagegen **vollständig** da. Er ist der Grund, warum das Fenster bei diesem
      Release überhaupt existiert — er darf nicht gekürzt sein.
- [ ] Die Punkte unter „Notes" bleiben ebenfalls vollständig (es sind Einschränkungen,
      keine Feature-Beschreibungen).
- [ ] „OK" schließt es; nach einem Neustart kommt es **nicht** wieder.
- [ ] Der Link unten öffnet die Releases-Seite des Repos im Browser.
- [ ] Bei gleicher Version (normaler Neustart) erscheint es gar nicht.
- [ ] **Setup.exe derselben Version noch einmal drüberinstallieren** → das Fenster
      kommt **trotzdem**. Es hängt nicht allein an der Versionsnummer, sondern auch
      am First-Run-Marker, den Velopack auch bei einem Update über eine bestehende
      Installation setzt.
- [ ] **Ältere Version installieren, dann wieder die neue** (z. B. 0.8.0 über 0.9.0,
      danach 0.9.0) → das Fenster kommt beim 0.9.0-Start. Die Einstellungen liegen
      unter `%LocalAppData%` und überleben beides, die gemerkte Version ist also
      noch die alte — genau der Fall, der es früher verschluckt hat.
- [ ] **Automatisches Update** (ohne Setup.exe, per Auto-Updater) → das Fenster kommt
      beim Neustart ebenfalls. Hier trägt **allein die Versionsnummer**: Velopack feuert
      dort `OnRestarted`, nicht `OnFirstRun`, es gibt also keinen Marker.
      ⚠ **Nicht mit zwei `0.9.0-local.*`-Builds testbar** — die tragen alle dieselbe
      `AssemblyVersion 0.9.0.0`, das `-local.<Zeitstempel>` erreicht sie nicht. Für
      diesen Punkt einen Build mit anderer Basis packen (z. B. `0.9.1-local.*`).
- [ ] Fehlt die Notes-Datei für die laufende Version im Build, startet die App
      normal und zeigt schlicht kein Fenster (Log: „No release notes shipped…").
- [ ] `LastSeenVersion` steht danach in `%LocalAppData%\…\settings.json`.

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
- [ ] **Tenant ohne ARM-Consent:** Tenant-Gruppe zeigt „Azure resource roles
      unavailable: …", Directory-Rollen und Gruppen laden weiter; nach erteiltem
      Consent erscheinen die Azure-Zeilen (spätestens nach Neustart).
      **Die Meldung muss vom fehlenden Consent sprechen — nicht „the request
      timed out".** Genau das stand am 2026-09-04 im China-Tenant, während die
      Ursache der fehlende Admin-Consent war.
- [ ] **Und dabei erscheint kein einziger WAM-Dialog** — weder beim Start noch bei
      einem der 60-s-Refreshes, auch nicht mit mehreren Konten. Der Hintergrund-Read
      holt das ARM-Token ausschließlich silent; ein Dialog hier würde die
      Token-Erneuerung aller anderen Konten blockieren, bis deren Timeout greift.
      Der Step-up-Prompt aus dem Claims-Challenge-Punkt oben bleibt davon unberührt.
- [ ] **Mehrere Konten, ein Tenant ohne Consent:** die Liste lädt trotzdem
      vollständig und innerhalb der üblichen Zeit — kein Konto wartet auf ein anderes.
- [ ] Nach jedem Fehlerfall ist die App weiter bedienbar (kein eingefrorenes UI).

## 7. Sicherheit & Logs

Logdateien: `%LocalAppData%\Entra-PIM-Manager\logs\pim-manager-*.log`

- [ ] Logs enthalten **keine** Access-/ID-/Refresh-Tokens (auch nicht in DEBUG).
- [ ] Logs enthalten **keine** Begründungstexte.
- [ ] Ticket-Nummern dürfen vorkommen — sind nicht sensibel.
- [ ] Nutzer erscheinen nur als `oid` (Object-ID), nie als UPN/Mail im Klartext.
- [ ] `appsettings.local.json` mit echten IDs ist **nicht** eingecheckt.
- [ ] App-Manifest: `requestedExecutionLevel level="asInvoker"`.
- [ ] **Logmenge:** App einige Stunden mit laufendem 60-Sekunden-Refresh stehen
      lassen → die Tagesdatei bleibt im **einstelligen MB-Bereich**. Die
      MSAL-Warnungen („Initializing authority from URI…", „MsaDeviceOperationProvider
      is not available") stehen **je einmal** darin, nicht einmal pro Refresh.
      Referenz: vor dem Fix waren es 6,5 MB in 12,5 Stunden, davon 99,6 % Wiederholungen.
- [ ] Ein MSAL-**Fehler** (z. B. Sign-in mit falscher ClientId) erscheint bei jedem
      Auftreten im Log, nicht nur beim ersten — Fehler werden nicht dedupliziert.

---

## Abzeichnung

| Abschnitt           | Ergebnis (OK / Fehler) | Bemerkung |
| ------------------- | ---------------------- | --------- |
| 1 Auth              | OK                     |  |
| 1b Sovereign Cloud  | OK                     |  |
| 1c Tenants & App-Reg| OK                     |  |
| 1d Sektionen        | OK                     | Azure-Faltung nur mit wenigen Eligibilities; 400er-Fall verschoben (Backlog) |
| 2 Read-Pfade        | OK                     |  |
| 3 Aktivierung       | OK                     |  |
| 4 Tray & UI         | OK                     |  |
| 4b Netzwerk-Check   | OK                     |  |
| 4c Alias & Suche    | OK                     |  |
| 5 Packaging         | OK                     |  |
| 5b In-Place-Upgrade | OK                     |  |
| 5c What's new       | OK                     |  |
| 6 Fehlerpfade       | OK                     |  |
| 7 Sicherheit & Logs | OK                     | Logmenge nach dem MSAL-Fix bestätigt |

**Freigabe für Release:** ☒ ja  ☐ nein — Unterschrift: ______________________
