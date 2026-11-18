# Nächste Sitzung

## Aktuell: 0.4.10 geladen, Parkneutralisierung und Anfahren aufgezeichnet

0.4.10 wurde um 14:22 MEZ nach bestätigtem Halt und beendeter App installiert. Sicherung, fünf Live-Dateien und unveränderte Spiel-/Workerdateien sind unabhängig geprüft. Keine Anwendung wurde gestartet. Es beseitigt die belegte Wiederholung vollständiger Modell-/Hookup-Katalogladungen nach leerer Queue und sichert Quellenwechsel sowie unvollständige Definitionen ab. Kandidat, Installer und Nachweise stehen in [REVIEW-0.4.10.md](REVIEW-0.4.10.md). Kein Ersatz der Fahrregelung und keine verlängerten Ausgabegrenzen.

Die 0.4.9-Fahrt zeigte um 13:51 und 13:56 MEZ kurze technische Ausgabeausfälle. Beim ersten fehlte vor allem der nächste Tickstart, beim zweiten alterten beide Ausgaben. Gültige Route und Pedalempfang unterscheiden dies vom Kreuzungsfehler. Die spätere Laufzeitaufnahme belegt starke Kataloglast, enthält aber keinen der ursprünglichen Ausfälle. Kausalität bleibt offen; nicht als behoben melden.

Der Nutzer startete ETS2LA um 14:25 MEZ selbst. Die zunächst ausgeschalteten Module wurden im bestätigten Stand einzeln aktiviert: Mautassistent, Pathfinding und Lane Assist; nativer ACC bleibt aus. Frische 0.4.10 ist ab 14:28:16 in der Aufnahme enthalten; die Startbereitschaft wurde separat im direkt gelesenen Status um 14:28:56 bestätigt. Nach ausdrücklich bestätigtem F8 folgt von 14:29:23 bis 14:29:49 ein 26,35-sekündiger Standabschnitt mit Feststellbremse und Spielpedalen 0/0. Anschließend fährt der Lkw mit aktiver Assistenz; bis zum Aufnahmeende 14:30:52 sind Anfahren bis rund 50 km/h, empfangenes Gas und kein neuer Fehlerhalt aufgezeichnet. Eine getrennte dreiminütige Fahrtaufnahme bis 14:36:45 enthält ebenfalls aktive Assistenz ohne aufgezeichneten Fehlerhalt; kein lückenloser Nachweis über die Zeit zwischen beiden Aufnahmen. Die erneute Controllerübernahme unter 0.4.10 ist noch offen. Während der Fahrt nur lesen; kein Fensterwechsel und keine Änderungen. Details und Grenzen stehen im Review. Nicht den abgeschlossenen Standabschnitt als weiterhin bestehenden Stillstand behandeln.

## Vorheriger Stand: 0.4.9 installiert und gefahren

0.4.9 wurde um 13:45 MEZ nach beendeter App samt Worker installiert. Plugin-SHA 00F6280BF75BB93F05A6D805E4A4BA5DBAA30F822F89FA8AE0840BD02BC2DEB0. Alle fünf Live- und Sicherungsdateien stimmen; Spiel und Workerdateien wurden nicht verändert, keine Anwendung wurde gestartet. Die folgenden Fahrbefunde stammen noch aus 0.4.8. Der Nutzer hat den Test inzwischen gestartet und die weitere Fahrtbeobachtung beauftragt. Parkneutralisierung über 40 Sekunden und tatsächliche Gas-/Bremswirkung in der Vordergrundfahrt sind belegt. Die unmittelbar danach berichtete Controllerübernahme ist in der betreffenden Aufnahme nicht eindeutig enthalten; nicht als unabhängig bestätigten Testabschluss zählen. Moderne Pedalausgabe bleibt false, Hintergrundvergleich nicht durchgeführt.

Um 13:17:24 löste ein geometrisch rückwärts springender neuer Kreuzungspfad den Gegenrichtungshalt aus. Native Graphsuche und konkreter Fehlerpfad sind mit der echten Karte reproduziert. 0.4.9 korrigiert die Erzeugung aller Alternativpfade an dieser Quelle, ohne Interpolator oder Schutzgrenzen zu ändern. 255 Pluginprüfungen sowie der isolierte tatsächliche Host-/Pedallauf bestehen; unabhängiger Gegenreview und reale Kartenprobe bestätigen den begrenzten Fix. Details und Paketidentität: [REVIEW-0.4.9.md](REVIEW-0.4.9.md).

Installation und tatsächliches Laden von 0.4.9 sind bestätigt. Die neue Stand-/Fahrabnahme ist offen. Beim nächsten sicheren Halt Feststellbremse und F8 zunächst aus; anschließend Parkneutralisierung und Controllerübernahme bestätigen und die Kreuzung erneut prüfen. Vor jeder Aktion aktuellen Prozess-/Fahrzustand feststellen, keine Fahrtfreigabe aus älteren Bestätigungen ableiten. Der vor dem Dateiaustausch gesicherte neue Routenfehler vom 18.11. um 13:44:31 trat im Umfeld von Pause/Schließen auf; seine genaue Ursache ist noch nicht untersucht.

Zusätzlich berichtete der Nutzer einen drohenden Pfostenkontakt innen beim Abbiegen. Die genaue Phase liegt in einer 38-sekündigen Aufnahmelücke; zeitgleiche Trailer-/Pfostenpose fehlt. Der normale Routenkorridor enthält keine innere Nachlaufkurve des Aufliegers. Das ist weiterhin offene Produktarbeit, keine durch 0.4.9 behobene Eigenschaft. Nächste Analyse benötigt zusammenhängende Route, Zugmaschinen-/Aufliegerpose und das tatsächliche Hindernis. Nicht pauschal Korridor verbreitern, Geschwindigkeit verringern oder Schutzprüfungen abschalten.

Gesamtauftrag bleibt offen: Aufliegerführung, Hintergrundpedale, weitere native Ausgabefehler und vollständige Maut-/Zahlungs-/Warnphasenabnahme. [Arbeitsliste](WORKLIST.md), [Hintergrundmessplan](BACKGROUND_INPUT.md).

## Vorheriger Stand: 0.4.7

Der Gesamtauftrag „alle offenen Themen umsetzen“ ist weiter offen. 0.4.7 ist gebaut und lokal geprüft (233 Integration, 100 Kernlogik, inzwischen 316 echte Hostprüfungen). Der unabhängige Endreview erteilte die begrenzte Freigabe für Einbau und beaufsichtigte Prüfung. Die Plugin-Datei wurde um 11:04 MEZ mit Sicherung installiert; Version und Hash wurden unabhängig bestätigt. Keine Anwendung wurde gestartet. Der separate InputBridge-Adapter besteht 47 geschützte native Prüfungen und den Diagnosevertrag; die gezielte Negativvariante reproduziert den Writerfehler. Nach eigenem Endreview wurde er um 11:17 MEZ bei weiterhin geschlossenen Anwendungen mit geprüfter Originalsicherung installiert. Die letzte Benutzerfrage bittet um Start von TruckersMP/ETS2LA im Stand, F8 aus, um die echte Übertragung zu messen. Keine Stand-/Fahrabnahme erfinden. Details und konkrete Beweisgrenzen: [REVIEW-0.4.7.md](REVIEW-0.4.7.md), Reihenfolge: [WORKLIST.md](WORKLIST.md).

0.4.6 wurde bereits am 18. November um 10:08 MEZ mit Sicherung und Hashprüfung installiert. Die folgenden früheren Sitzungsstände sind historische Einträge und dürfen diesen Stand nicht überschreiben. Ob eine Anwendung jetzt läuft, muss vor jedem Eingriff neu geprüft werden.

## Historische Sitzungsnotizen

## Neuer Kandidat: 0.4.6 – Bahnübergang

0.4.5 wurde am 18. November geladen und während einer dreiminütigen Fahrt beobachtet. Kein neuer Fehlerhalt, keine Pedalempfangsstörung oder Kabinen-/Chassisschadenzunahme in dieser Aufnahme. Ein kurzer Bremsimpuls ging auf 301 ms alte Routendaten zurück; die Regelung verlangt weniger als 200 ms. Diese Verzögerungsursache und der frühere Lenkausfall bleiben offen.

Der anschließende Halt am offenen Bahnübergang ist belegt: zwei offene SDK-Schranken mit ID 0 stehen an PPD-Locators mit ID 1. Das Kartenprofil ordnet beide über `id_map` dem zentralen Controller 0 zu. Bisher suchte die Ampellogik direkt am Controllerpunkt und fand dort keinen Actor. 0.4.6 ergänzt diese Gruppenbindung. Noch nicht installiert oder gefahren; erst nach geschlossenem ETS2LA mit Sicherung austauschen. Danach reale offene/geschlossene Durchfahrt und manuelle Übernahme prüfen. Keine Warnlichtfreiheit aus fehlenden Live-Ampelmeldungen behaupten. [Review](REVIEW-0.4.6.md).

## Maßgeblicher aktueller Stand: 0.4.5 installiert, Liveprüfung offen

0.4.4 wurde am 18. November gefahren. Zwei Pedalempfangsausfälle nach längerem Heraustabben lösten dessen technische Fehlerbremsung aus. 0.4.5 trennt diese Störung von einer echten Gefahrenanforderung, erhält gesunde Lenkung und aktuelle Bremsgrenzen und entfernt alte gespeicherte Bremsanforderungen. Gas bleibt bis zu manueller Übernahme und bewusstem F8 gesperrt. Nach geprüftem Prozessende wurde die eine Plugin-Datei mit Sicherung ersetzt; Installation und unveränderte Hostdateien sind per SHA256 bestätigt. Keine Anwendung gestartet. [Review, Paketidentität und Prüfungen](REVIEW-0.4.5.md).

Nächster Schritt: Im Stand mit Feststellbremse ETS2LA starten, F8 zunächst auslassen, geladene 0.4.5 und Startbereitschaft prüfen. Dann Aktivierung und Controllerbremse/F9 prüfen. Eine Empfangsstörung wird nicht durch Pause, Parken oder Fokuswiederkehr quittiert; nach manuellem Übernehmen bewusst neu starten. Keine erneute blinde Hintergrundfahrt zum Nachweis der noch ungelösten tatsächlichen Übertragung.

Separater Vorfall 09:19:37: etwa 158 ms ohne Lenkbefehl bei frischer Telemetrie. Die erst nach der Abschaltung erfasste Native-Meldung zur App-Pause ist kein Beleg einer vorherigen Pause. Ursache der Ausgabelücke offen, Watchdog unverändert. Automatisches Bezahlen und vollständige Mautdurchfahrt mit 0.4.4/0.4.5 weiterhin nicht nachgewiesen.

## Vorheriger Installationsstand: 0.4.4

0.4.4 wurde am 17. November mit Sicherung aller fünf ersetzten Dateien installiert und unabhängig gegen das versiegelte Paket geprüft. Spiel und ETS2LA waren geschlossen und wurden nicht automatisch gestartet. Die Fassung ergänzt gebundene einmalige Zahlungsinteraktion, einen unabhängigen dauerhaften Zahlungsschreiber und genaue Fokus-/Pedaldiagnose. 217 Integrationsgruppen, 99 Logikgruppen, 299 Host-/Produktprüfungen, 70 Aufnahmeprüfungen und drei Inventurtests bestehen. Kritischer Gegenreview abgeschlossen. [Paketidentität und Grenzen](REVIEW-0.4.4.md). Nicht aus einem älteren Versionsabschnitt auf den laufenden Stand schließen: Prozess und Datei jeweils neu prüfen.

Als Nächstes über die üblichen Verknüpfungen starten und zunächst mit Feststellbremse stehen bleiben, F8 noch auslassen. Geladene Version und Bereitschaft prüfen; dann Start/Parken sowie F9 und Controllerbremse im Stand. Erst anschließend eigene Geschwindigkeitsregelung, manuelle Zahlzone, echte Schrankenöffnung und Anschlussstraße prüfen. Keine Fahrmodule während einer Fahrt neu laden. Ein ausgegebener Interaktionsimpuls oder Speicher-ACK ist keine Zahlung und kein Nachweis einer autonomen Durchfahrt. Bei nicht belegter oder unerreichbarer Zahlzone bleibt manuelle Übernahme erforderlich; bestehende Abstände nicht für den Test verkleinern.

TruckersMP-Hintergrundpedale bleiben offen. Die offizielle Input-API bietet keinen erzwungenen Background-Schalter; 0.4.4 erfasst verifizierten Fensterkontext und trennt logischen Empfang von effektiver Pedalwirkung. Kein Einzelspieler-FocusKeeper und keine blinde Rückkehr zur luftverbrauchenden modernen Pedalausgabe. Die bisherige Standardeinstellung bleibt erhalten.

## Historischer Ausgangsstand 0.4.1 – durch obigen Stand ersetzt

**0.4.1 ist installiert und unabhängig geprüft. Die erneute Stand-/Fahrprüfung steht aus.** Die erste Fahrt mit 0.4.0 zeigte einen Fehlerhalt durch Verwechslung des Gaseingangs mit seiner effektiven Wirkung. 0.4.1 korrigiert die Überwachung und sperrt bei echtem Empfangsausfall nur die Beschleunigung, ohne daraus allein eine Vollbremsung zu erzeugen. Der Austausch der Plugin-Datei erfolgte am 17. November um 17:18 Uhr MEZ nach erneut bestätigtem Stillstand und geschlossenem ETS2LA; vorherige Plugin-Datei gesichert, Hostbibliotheken unverändert. [Befund und Nachweise](REVIEW-0.4.1.md).

## Abschluss und nächste reale Prüfung

1. Offline-Prüfstand: 200 Integration, 93 Kernlogik, 223 isolierte Host-/Plugin-Pipeline, 64 Aufnahme und 3 historische Wiedergabeprüfungen grün. Zusätzlich 255 teilweise überlappende Routingassertions, keine Summierung. Geprüftes Vier-Datei-Paket und Endreview sind vom tatsächlichen Einbau zu trennen; kein Build oder Replay ist ein Fahrnachweis.
2. Installation am 17. November 2026 um 16:46 Uhr MEZ nach bestätigtem Halt, manueller Übernahme und geschlossenem ETS2LA abgeschlossen. Plugin, Game, State und LaneAssist sowie vier Sicherungsdateien unabhängig geprüft; ACC und Spiel-DLLs unverändert. Keine Apps oder Assistenz automatisch gestartet. Nicht erneut installieren, sondern mit der Standprüfung fortfahren.
3. Nach freiwilligem Nutzerstart die genaue Version, drei benötigten Module und frische SDK-/Routenquellen im Stand prüfen. Spiel im Vordergrund lassen, normaler Luftdruck, Feststellbremse angezogen. Eingebauten Spieltempomat selbst ausschalten; die eigene Regelung übernimmt bei aktivem Spieltempomat nicht und toggelt ihn nicht blind.
4. F8 im Stand aktivieren; mit Controllerbremse und F9 getrennt prüfen, dass Assistenz ausgeht und Lenkung/Pedale manuell reagieren. Loslassen der Bremse darf nicht erneut einschalten.
5. Auf freier Strecke eigene Regelung beobachten: Anfahren, bis 100 km/h wenn passend, Rollen bei kleiner weicher Tempodifferenz, Kurvenbegrenzung und tatsächliche Spielpedale. Unwirksame Spielausgabe darf nicht als gelöst gelten. Fehlendes Straßenlimit 0 bedeutet höchstens 30 km/h vorsichtige Ersatzgrenze, kein Solltempo 0; engere lokale Grenzen dominieren. Den unabhängigen Pedalwächter bei weiterlaufender Lenkung zuerst isoliert prüfen, keinen realen Ausfall während der Fahrt provozieren.
6. Klassische Mautprüfung aufnehmen: frühe Automatikspurwahl, bei fehlender Freigabe normaler Vorhalteanker mit Wechselreserve, geschlossene/offene Schranke, verzögerte Freigabe, vollständige Passage und weitere Anschlussstraße. Keine manuelle Gasunterstützung als autonome Passage zählen.
7. Neu beobachtete Halte nach begrenzendem Grund unterscheiden: gewünschter Normalhalt, Ampel/Verkehr, Mautbeleg, Routenlücke oder tatsächlicher Ausgabefehler. Nicht pauschal Bremsfristen erhöhen oder Quellen ignorieren.

## Abgegrenzter Ausgangsbefund

Der Halt vor der spanischen Zufahrt hatte tatsächlich nur 15,31 m Wechselrest; die bestehende Anforderung beträgt 45 m. Frühere kontrafaktische Positionen auf derselben realen RoadList mit 160/220 m Rest bei 20/77 km/h erreichen die automatische Spur. Warum die tatsächliche frühere Auswahl damals fehlte, ist nicht belegt. Der neue separate 60 m-Anker erhält eine Wechselreserve auf der nachgewiesenen Zufahrt; er ist kein rückwirkender Nachweis dieser Ursache. Er bleibt bei vorläufiger Manualwahl aktiv, wenn eine aktuell physisch freie automatische Alternative nur wegen Tempo noch nicht erreichbar und eine sichere Vorbereitung auf derselben Zufahrt nachgewiesen ist. Eine geschlossene synthetische Plantprobe erreicht aus 130 m/77 km/h den zulässigen tatsächlichen Wechselbeginn bei etwa 62 m/19,5 km/h; eine manuell vorgegebene Geschwindigkeitsfolge ist dafür nicht nötig. Der tatsächliche Plugin-/OnPath-/Watchdog-Übergang wurde zusätzlich isoliert durch beide Pedalsenken geprüft; die alte Zwischenfassung reproduzierte den unerwünschten Fehlerhalt. Die reale Fahrt bleibt gesondert zu prüfen.

Die Folgeaufnahme 14:35–14:45 Uhr enthält eine Passage mit manuellem Gas und anschließender Trackingverlustmeldung. Die reale Kartenrekonstruktion findet die aufgezeichnete 4,499 m-Lücke 357,613 m voraus: falsche Folgekurve [0], echte passende Alternative [1], gleicher Ein-/Ausfahrtsknoten, kein Spurwechsel nötig. Vollständige historische Nachfolge-UIDs fehlen. Die alte Aufnahme zählt nicht als autonome erfolgreiche Durchfahrt.

Ein separater geometrischer Gegenfall zum nativen GetClosestGate zeigt ein gegenläufiges Tor knapp unter 4 m von derselben Route. Die neue eigene Regelung verwendet nicht diesen nativen ACC-Schrankenregler. Damit wird die alte historische Ursache nicht nachträglich bewiesen.

## Verbleibende Grenzen

Alt+Tab-Pedale in TruckersMP bleiben offen. Begrenzte SDK-Verkehrs-/Parkslots, unbekannte Objekte und künftige Spawns sind keine vollständige Wahrnehmung. Standardprofil 4,5×3,4 m ist eine Annahme, keine gemessene Ladungsfreigabe. Sonderladung, Mehrfachauflieger, lenkbare Auflieger, Baustellen und sämtliche Verkehrsschilder sind nicht allgemein abgedeckt. Die neue Regelung muss neu im Spiel geprüft werden.

Während einer Fahrt nur lesen, keinen Fensterfokus wechseln und keine Produktdateien austauschen. Aktueller Prozess-/Fahrzustand ist vor jeder Aktion neu festzustellen. Ältere Versions- und Installationsnachweise stehen im [Änderungsprotokoll](CHANGELOG.md) und in den jeweiligen Reviews.
