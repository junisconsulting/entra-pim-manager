# Entra PIM Manager — Anleitung

> *[This guide in English](user-guide.md)*
>
> Für alle, die die App benutzen. Das Einrichten für einen Tenant ist Admin-Arbeit und steht in
> [app-registration-setup.md](app-registration-setup.md) und
> [unattended-deployment.md](unattended-deployment.md).
>
> Die Oberfläche der App ist englisch. Bezeichnungen wie **Activate on** oder **Justification**
> stehen deshalb auch hier auf Englisch — so wie du sie im Fenster siehst.

## Wozu die App da ist

Deine Admin-Rollen sind nicht dauerhaft eingeschaltet. Microsoft Entra Privileged Identity
Management (PIM) gibt sie auf Anforderung heraus, für ein paar Stunden, damit ein Konto, das
kompromittiert wird während du nicht arbeitest, kein Admin-Konto ist. Das Einschalten heißt
**aktivieren**.

Im Azure-Portal bedeutet das: Browser öffnen, PIM-Blade suchen, sich durch mehrere Seiten klicken.
Diese App macht dasselbe über ein Symbol neben der Uhr.

**Sie kann dir nichts geben, was du nicht schon hast.** Sie aktiviert Rollen, für die du *eligible*
bist — die dir also vorher jemand zugewiesen hat. Fehlt eine Rolle, die du erwartest, ist das nicht
hier zu beheben: frag die Administration deines Tenants.

## Installieren

Installer von der Releases-Seite des Projekts laden und ausführen.

Es kommt keine UAC-Abfrage und es gibt nichts auszuwählen — die Installation gilt nur für dein
Windows-Konto und landet in `%LocalAppData%\Entra-PIM-Manager`. Keine Adminrechte, kein Dienst,
keine Auswirkung auf andere Benutzer des Rechners. Wenn deine IT die App verteilt hat, ist sie schon
da.

Beim allerersten Start wirst du zwei Dinge gefragt: ob die App starten soll, wenn du dich an Windows
anmeldest, und ob sie einen Startmenü-Eintrag behalten soll. Beides ist an, beides lässt sich später
unter **Settings → Behavior** ändern. Die Frage kommt einmal pro Installation.

Die App hat kein eigenes Fenster in der Taskleiste. Sie lebt im Infobereich — den Symbolen neben der
Uhr. Windows versteckt neue Symbole dort gern: Wenn du es nicht siehst, klick auf den Pfeil **^**
und zieh das Symbol auf den sichtbaren Teil der Leiste, damit es dort bleibt.

## Konto verbinden

Klick auf das Symbol. Beim ersten Mal steht dort **Welcome to Entra PIM Manager**.

Wie es weitergeht, hängt davon ab, woher du die App hast:

- **Von der IT verteilt** — der Tenant ist schon hinterlegt. **Add account**, dann **Sign in**.
- **Selbst installiert** — es fehlt noch die App Registration des Tenants: **Set up your first
  tenant**, dann Tenant-ID und Client-ID eintragen. Beides bekommst du von deinem Admin — die zwei
  IDs benennen den Tenant und die App Registration, raten kann man sie nicht. Danach das Konto
  hinzufügen.

**Sign in** öffnet die normale Windows-Kontoauswahl. Nimm das Admin-Konto, mit dem du die Rollen
hältst — bei vielen ist das ein anderes als das, mit dem sie sich an Windows anmelden. In der App
gibt es keine Passwortabfrage: Windows erledigt die Anmeldung, die App sieht dein Passwort nie.

Du kannst mehrere Konten in mehreren Tenants verbinden. Umschalten musst du zwischen ihnen nicht —
alles steht in einer Liste, nach Tenant gruppiert.

### Wenn immer das falsche Konto angemeldet wird

Manche Organisationen leiten die Anmeldung über einen eigenen Identity Provider (Okta, ADFS und
ähnliche). Der meldet dich mitunter mit deinem Alltagskonto an, egal was du auswählst, und das
Admin-Konto wird dir nie angeboten.

Dafür gibt es **Device code** bei den Anmeldeoptionen. Es zeigt eine URL und einen kurzen Code, den
du auf dem Handy oder in einem privaten Browserfenster eingibst — dort, wo noch nichts angemeldet
ist.

Zwei Dinge vorher. Manche Unternehmen sperren die Device-Code-Anmeldung per Conditional Access; dann
scheitert sie und der normale Weg ist der einzige. Und Rollen, die durch einen *Authentication
Context* geschützt sind, lassen sich mit einer Device-Code-Sitzung nicht aktivieren. Die App weist
am Konto darauf hin; die Lösung ist, das Konto zu entfernen und normal neu anzumelden.

## Das Tray-Symbol lesen

Das Symbol beantwortet „habe ich gerade Rechte?", ohne dass du etwas öffnest. Mauszeiger drauf für
die Details.

| Symbol | Bedeutung |
| --- | --- |
| **Rot** | Nicht angemeldet — kein Konto verbunden, oder die Sitzung ist abgelaufen |
| **Grau** | Angemeldet, nichts aktiv. Der Normalzustand |
| **Grün** | Mindestens eine Rolle ist aktiv. Der Tooltip zählt sie |
| **Gelb** | Eine aktive Rolle läuft gleich ab. Der Tooltip nennt sie und zählt herunter |

Das Schild ist für helle wie dunkle Taskleisten gezeichnet und folgt ihr, wenn du das
Windows-Design umstellst. Bedeutung trägt allein der kleine Punkt.

## Das Fenster

Ein Klick auf das Symbol öffnet das Fenster. Von oben nach unten:

**PINNED** — Rollen und gespeicherte Scope-Sets, die du mit einem Stern markiert hast. Deine
Sortierung, bleibt über Neustarts erhalten.

**RECENT** — die letzten drei aktivierten Rollen. Füllt sich von selbst, nichts zu pflegen.

**ELIGIBILITIES** — alles, was du aktivieren darfst, nach Tenant gruppiert. Gruppen lassen sich
zuklappen, und die App merkt sich, welche du zugelassen hast.

**ACTIVE** — was gerade eingeschaltet ist, jeweils mit Countdown-Balken. Wegen dieses Abschnitts
lohnt der Blick in die App auch dann, wenn du nichts aktivierst.

Das **Suchfeld** durchsucht Rollenname, Rollentyp, Tenant *und* Azure-Scope gleichzeitig — der Name
einer Subscription findet also die Rollen, die für sie gelten. **⟳** erzwingt eine Aktualisierung,
nötig ist das aber selten: Die Liste hält sich selbst aktuell, auch bei Aktivierungen, Abläufen und
Rollen, die anderswo beendet wurden. **⚙** öffnet die Einstellungen.

Ganz unten steht die Version, die du benutzt. Sie ist ein Link: ein Klick öffnet genau dieses
Release auf GitHub — „was hat sich eigentlich geändert" ist damit einen Klick entfernt statt eine
Suche.

## Eine Rolle aktivieren

Klick auf eine Rolle. Das Aktivierungsformular schiebt sich ein.

**Duration** — ein Schieberegler in Halbstundenschritten. Sein Maximum entscheidet nicht die App,
sondern die Richtlinie, die dein Admin für diese Rolle gesetzt hat; verschiedene Rollen haben
verschiedene Maxima. Den Startwert legst du unter **Settings → Behavior → Default activation
duration** fest.

**Justification** — warum du sie brauchst. Das landet im Audit-Log deines Tenants und wird von
echten Menschen gelesen; „Arbeit" hilft später niemandem. Wenn du immer denselben Satz tippst,
speichere ihn mit **+ Save current** unter **FAVOURITES**: Er kommt für diese Rolle als
Ein-Klick-Favorit zurück.

**Ticket number / Ticket system** — wird nur gefragt, wenn die Richtlinie der Rolle es verlangt. Das
Ticketsystem ist pro Tenant vorbelegt, falls dein Admin eines hinterlegt hat; die Nummer tippst du.

Dann **Activate**. Die Rolle wandert innerhalb weniger Sekunden nach ACTIVE.

### Zwei Hinweise, die man lesen sollte

**ℹ This activation requires approval** — du bekommst die Rolle jetzt nicht. Die Anfrage geht an
einen Genehmiger und wartet. Die Zeile bleibt als ausstehend sichtbar, und die Rolle wird aktiv,
sobald zugestimmt wurde. Von deiner Seite ist nichts weiter zu tun — dränge den Genehmiger, nicht
die App.

**ℹ This activation may require additional verification (MFA)** — Windows verlangt während der
Aktivierung eine Bestätigung.

**⚠ This group can carry directory roles** — bei einem PIM-for-Groups-Eintrag. Eine
Gruppenmitgliedschaft kann Admin-Rollen mitbringen; die Aktivierung gibt dir also womöglich mehr,
als der Gruppenname vermuten lässt. Gut zu wissen, was man da einschaltet.

## Azure-Rollen: wo die Rolle gelten soll

Eine Azure-Rolle auf einer Management Group gilt für alles darunter — das können sämtliche
Subscriptions eures Unternehmens sein. Das alles zu aktivieren, um in einer zu arbeiten, ist
unnötige Angriffsfläche. Deshalb fragt die App nach.

Solche Rollen haben einen Knopf **Activate on**. Er öffnet eine Liste: oben ein Suchfeld, dann die
**Subscriptions**, dann die **Management Groups**, zuletzt **Entire scope — everything listed
above**. Die Reihenfolge ist Absicht: kleinste Wirkung zuerst.

**Nichts ist vorausgewählt — auch nicht der Scope, auf dem deine Berechtigung liegt.** Activate
bleibt gesperrt, bis du geantwortet hast. Alles zu nehmen steht am Ende der Liste zur Verfügung,
aber als Entscheidung — nicht als das, was passiert, wenn man das Formular in Ruhe lässt.

Jeder angehakte Scope wird eine eigene Aktivierung. Hakst du drei Subscriptions an, zeigt ACTIVE
drei Zeilen, jede mit eigenem Countdown, jede einzeln beendbar.

Resource Groups werden nicht angeboten, einzelne Ressourcen auch nicht. Im Portal gibt es sie, aber
die Liste wäre endlos — das versucht die App gar nicht erst.

### Scopes als Set speichern

Wochenlang in denselben zwei Subscriptions zu arbeiten ist normal. Sie täglich neu anzuhaken nicht.

Hak an, was du brauchst, gib dem Ganzen einen Namen („Projekt Contoso" — optional) und speichere es.
Es erscheint unter **FAVOURITE SCOPES** im Formular dieser Rolle. Ein Klick hakt genau diese Scopes
an; klickst du ein anderes Set an, **ersetzt** es die Auswahl, statt sie zu ergänzen — so können
sich Sets nicht unbemerkt aufsummieren.

Markierst du ein Set mit dem Stern, steht es zusätzlich unter **PINNED** auf der Hauptseite, neben
deinen angehefteten Rollen — ein Klick von der Startseite zum ausgefüllten Formular. Eine Rolle
anzuheften und ein Scope-Set anzuheften ist bewusst dieselbe Geste.

Gespeicherte Sets liegen auf deinem Rechner (`scope-favorites.json`), nie im Tenant. Niemand sonst
sieht sie.

## Eine Rolle vorzeitig beenden

Jede ACTIVE-Zeile hat einen **Stop**-Knopf. Benutz ihn, wenn du fertig bist — der Sinn von PIM ist
ja gerade, dass Rechte nicht herumliegen.

**In den ersten fünf Minuten ist der Knopf grau und tut nichts.** Das ist kein Fehler und keine
Vorsicht der App: Microsoft lehnt es ab, eine Rolle zu deaktivieren, die noch keine fünf Minuten
aktiv ist. Fahr mit der Maus drüber, dann steht dort, wie lange noch. Danach wird er rot und
funktioniert.

Eine Rolle nicht zu beenden kostet nichts — sie läuft von selbst ab.

## Kurz vor Ablauf

Kurz bevor eine aktive Rolle ausläuft, meldet sich eine Benachrichtigung, und das Tray-Symbol wird
gelb. Der Countdown-Balken der Zeile wechselt gegen Ende die Farbe.

Ob gewarnt wird und wie früh, steht unter **Settings → Notifications**.

## Einstellungen

Das Zahnrad oben rechts.

**APPEARANCE** — Theme: System, hell oder dunkel.

**BEHAVIOR** — Mit Windows starten. Startmenü-Eintrag behalten. Standard-Aktivierungsdauer (der
Startwert des Schiebereglers, keine Obergrenze — die kommt aus der Richtlinie deines Admins).

**NOTIFICATIONS** — vor Ablauf benachrichtigen, und wie früh.

**UPDATES** — siehe unten.

**DIAGNOSTICS** — Log-Detailgrad, Log-Ordner öffnen, und ein **Network check**, der jeden Endpunkt
prüft, den die App und die Windows-Anmeldung brauchen. **Copy report** legt das Ergebnis in einer
Form in die Zwischenablage, die du in ein Ticket einfügen kannst. Der richtige Griff, wenn die
Anmeldung in einem abgeschotteten Netz scheitert.

**TENANTS** — eine Karte pro Tenant: dessen App Registration, dessen Ticketsystem und die dort
angemeldeten Konten. Du kannst einem Konto ein kurzes Kürzel geben („EADM"), statt überall einen
langen UPN zu lesen, und die Tenants in deine Reihenfolge ziehen. Eine geänderte App Registration
greift erst nach einem Neustart — die App sagt es dann auch.

## Updates

Die App prüft einmal täglich auf GitHub und bietet an, was sie findet; du wählst **Install** oder
**Later**, und nach dem Herunterladen, ob jetzt neu gestartet oder beim nächsten Start angewendet
werden soll. Der Schalter dafür ist unter **Settings → Updates**.

**Check for updates** an derselben Stelle prüft sofort und sagt dir, was dabei herauskam. Das geht
auch bei ausgeschaltetem automatischem Update — der Schalter regelt, ob du ungefragt unterbrochen
wirst, nicht ob du fragen darfst.

Eine Antwort überrascht regelmäßig: *eine neuere Version existiert, wird aber noch nicht angeboten.*
Releases werden ihre ersten 72 Stunden absichtlich zurückgehalten. Die Pakete sind nicht signiert,
und Sicherheitssoftware blockiert Programme, die sie noch nicht eingestuft hat — eine frischere
Version würde sich installieren und dann nicht starten. Sie wird von allein verfügbar, du musst
nichts tun.

## Wenn etwas nicht funktioniert

**Keine Rollen in der Liste, obwohl du welche hast.** Prüf das Konto oben im Fenster — man ist
schnell mit dem Alltagskonto statt mit dem Admin-Konto angemeldet. Stimmt das Konto, ist die
Berechtigung tatsächlich nicht da, und das ist eine Frage an die Administration des Tenants.

**Die Azure-Rollen fehlen, die anderen sind da.** Im Fenster steht eine Zeile, die mit *Azure
resource roles unavailable* beginnt — was danach kommt, ist die Ursache. Ein fehlender Consent
erledigt sich von allein, sobald ein Admin ihn erteilt. Eine Conditional-Access-Richtlinie, die ein
verwaltetes Gerät verlangt, gehört zur IT. Und wenn du dich per Device Code angemeldet hast, ist das
die Ursache: diese Anmeldung kann nichts über das Gerät nachweisen — Konto entfernen und normal neu
anmelden.

**Das Anmeldefenster öffnet sich und bleibt leer.** Meist blockiert das Netzwerk einen
Microsoft-Endpunkt. **Settings → Diagnostics → Network check** ausführen, **Copy report** drücken
und das an die IT schicken — darin steht, welcher Endpunkt blockiert ist, statt dass jemand raten
muss.

**Die Anmeldung scheitert mit einer Consent- oder Berechtigungsmeldung.** Der App Registration wurde
in diesem Tenant nicht zugestimmt. Ein Admin macht das einmalig, siehe
[app-registration-setup.md](app-registration-setup.md).

**Die Aktivierung scheitert und nennt Authentication Context oder Conditional Access.** Die Rolle
verlangt eine stärkere Anmeldung, als die Sitzung hat. Wenn du dich per Device Code angemeldet hast,
ist das die Ursache — Konto entfernen und normal neu anmelden.

**Die Aktivierung braucht eine Genehmigung und es passiert nichts.** Sie wartet auf einen Menschen.
Die App kann das nicht beschleunigen.

**Der Stop-Knopf ist grau.** Die ersten fünf Minuten, siehe oben.

**Das Tray-Symbol ist weg.** Windows hat es versteckt. Auf den Pfeil **^** neben der Uhr klicken und
zurück auf die Leiste ziehen.

**Alles andere** — **Settings → Diagnostics → Open log folder**. Das Log enthält keine Passwörter,
keine Tokens und keinen Begründungstext; es kann bedenkenlos an ein Ticket angehängt werden.

## Was auf deinem Rechner landet

Alles liegt unter `%LocalAppData%\junis\Entra-PIM-Manager`, in deinem eigenen Benutzerprofil:

- deine Einstellungen, angehefteten Rollen, gespeicherten Begründungen und Scope-Sets
- welche Konten du verbunden hast und die App-Registration-Einträge deiner Tenants
- der von Windows verwaltete Token-Cache, der dich angemeldet hält
- die Logdateien

Ein Passwort wird nie gespeichert — die Anmeldung macht Windows. Außerhalb deines Profils wird
nichts geschrieben: kein `Programme`-Ordner, keine maschinenweite Registry, kein Dienst, keine
geplante Aufgabe. Der einzige Eintrag außerhalb dieses Ordners ist der Autostart-Wert unter
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, falls du den Autostart angelassen hast.

Eine Deinstallation entfernt all das, inklusive Tokens und Autostart-Eintrag. **Ein Update entfernt
nichts davon** — Einstellungen, Konten und Favoriten bleiben.
