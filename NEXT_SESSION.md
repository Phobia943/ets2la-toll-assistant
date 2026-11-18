# Nächste Sitzung

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
