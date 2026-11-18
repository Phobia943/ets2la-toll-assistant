# 0.4.10 – wiederholte Katalogarbeit entfernen

Stand: 18. November 2026. Nach bestätigtem sicheren Halt und beendeter Assistenz um 14:22 MEZ mit geprüfter Sicherung installiert. Alle fünf Live- und Sicherungsdateien stimmen. Nach eigenem Nutzerstart sind tatsächliches Laden, Parkneutralisierung und Anfahren aufgezeichnet; erneute manuelle Übernahme und vollständige Fahrabnahme stehen aus. Dies ist eine belegte Leistungsverbesserung, kein Nachweis, dass sämtliche Ausfälle behoben sind.

## Neue Ausfälle und Beweisgrenzen

0.4.9 löste um 13:51:15 MEZ wegen 163,82 ms alter Pedalbefehle einen Fehlerhalt aus; um 13:56:14,99 folgte eine 164,45 ms alte Lenkausgabe. Die bestätigten Überwachungszeitpunkte liegen 299,9986 Sekunden auseinander. Beim ersten Fall lag der größte Zeitanteil zwischen beendetem und neu begonnenem Regelungsdurchlauf. Die gespeicherte Phase `Preparing` war erst etwa 1,3 ms alt und belegt keine dort blockierte Berechnung.

Beim zweiten Fehler liefen Spielsimulation und der unabhängige Recorder weiter; auch der vorherige Pedalbefehl war veraltet. Ein kompletter PC-/Spielstillstand ist damit nicht belegt. Die nachträglich erfasste native Meldung über ausgeschaltete Assistenz erklärt nicht die ursprüngliche Lücke. Beide Vorfälle sind vom zuvor korrigierten Gegenrichtungsfehler an der Kreuzung zu unterscheiden.

Die anschließenden Laufzeitaufnahmen enthalten nicht die beiden historischen Ausfälle. Das nächste Fünf-Minuten-Fenster wurde mit aktiver Assistenz ohne neuen Fehler durchfahren; dort ist eine kurze Speicherbereinigung von 1,480 ms positiv dokumentiert. Die Decoder-Verlustzähler sind unplausibel und erlauben keinen Vollständigkeits- oder Abwesenheitsbeweis. Die Ursache der früheren langen Lücken bleibt offen; Überwachungsfristen und Notbremsstrategie werden deshalb nicht spekulativ verändert.

## Belegte unnötige Arbeit

Viele erfasste Speicherbereinigungs-Triggerstapel führen durch `TollModelEvidence.ReadModels` und `ReadHookupDefinitions`. Der Kontrollfluss bestätigt: Nach jeder geleerten Warteschlange wurde der Hintergrundauftrag beendet, mitsamt seinen nur lokal gehaltenen Definitionskatalogen. Der nächste neue Modellauftrag las diese Kataloge erneut vollständig.

Eine getrennte Messung mit den 113 archivierten, in Größe und Änderungszeit unverändert vorhandenen Quelldateien ergab in drei Läufen:

- 4.252 Modelleinträge, 463 von 463 Hookup-Dateien lesbar, 3.349 eindeutige Hookup-Einträge und 726 als nicht fest klassifizierte Einträge; keine verschluckten SII-Lesefehler in dieser Archivmenge.
- Modellkatalog etwa 2,80–3,24 Sekunden und 7,53 GB kumulierte Speicherzuweisungen; Hookup-Katalog etwa 5,24–5,85 Sekunden und 10,28 GB. Das ist kurzlebige Allokationsmenge, kein entsprechend großer dauerhafter Speicherbedarf.
- Eigene Archivleser benötigen weiterhin etwa 299 MB und 289–392 ms je Neuöffnung. Die erste Katalogladung, die Archivöffnung und die eigentliche Modell-/Geometrieprüfung bleiben Arbeit im Fahrhost.

Diese Messung belegt den vermeidbaren Aufwand. Sie stellt nicht rückwirkend dessen Kausalität für die beiden Fehlerhalte fest und behauptet keine vollständig erfasste aktuelle Live-Dateisysteminstanz.

Eine zweite isolierte Probe ruft die tatsächliche `DefinitionGeneration.Read` der eingefrorenen 0.4.10-DLL dreimal auf, jeweils mit frisch geöffneten und anschließend geschlossenen eigenen Archivlesern. Erstladen: 10,10 Sekunden / 17,80 GB Allokationen; Wiederverwendung: 0,350 ms / 1.760 Bytes und 0,005 ms / 0 Bytes. Katalogreferenzen und vollständige Inhaltsfingerprints bleiben auch nach Schließen der Leser identisch. Diese Zeiten betreffen nur den Katalogread, nicht den ganzen Worker oder eine Fahrt. Die separate Archivöffnung bleibt mit etwa 299 MB / 367–482 ms pro Batch bestehen.

## Änderung und Gegenfälle

Vollständig geladene Definitionsdaten werden einmal pro Dateisystemgeneration gehalten. Jeder Auftrag behält seine eigene Generation; ein spät beendeter alter Auftrag kann den neuen Katalog nicht überschreiben. Archive und veränderliche Modellgeometrie bleiben getrennt pro Arbeitsbatch und werden weiterhin geschlossen. Zahlart, Sichtbarkeit, Zusatzteile, Fahrzeugprofil und Freiraum werden weiterhin pro Modellauftrag geprüft.

Ein Fehler beim Ermitteln einer neuen Archivliste ändert die Quellenfelder nicht teilweise. Ein unlesbares Hookup-Dokument verwirft die gesamte Katalogladung, denn es könnte eine widersprechende doppelte Definition enthalten. Es wird kein Teilkatalog veröffentlicht oder zur positiven Modellfreigabe benutzt. Ein neuer Modellauftrag kann die Ladung erneut versuchen; ein bereits abgelehntes Modell wird dabei nicht automatisch wieder freigegeben. Es gibt keine neue ungebremste Wiederholungsschleife.

260 vollständige Integrationsprüfungen bestehen. Die fünf neuen Gruppen prüfen den tatsächlichen Arbeitsauftrag und die SII-/Hookup-Parser: Wiederanlauf nach leerer Queue, Quellenwechsel während einer laufenden Ladung, unvollständige/fehlgeschlagene Ladung, automatische freie Spur gegenüber manueller blockierter Spur nach gescheitertem Quellenwechsel sowie eine unlesbare widersprechende Zahlungsdefinition. Nur Archivzugriff und PMD-Binärdekodierung sind in diesen synthetischen Fällen ersetzt; `InspectModel` und die Freigabeberechnung laufen tatsächlich.

Zusätzlich bestehen 307 Assertions am isolierten tatsächlichen Host-/Plugin-Ausgabepfad. Zeitabhängige Schleifen sind darin enthalten; dies sind keine 307 unabhängigen Fahrfälle. Der kritische Gegenreview hat die Generationsbindung, die Fehlerfälle, Paketidentitäten und Beweisgrenzen geprüft.

## Neuer Fahrbefund: Fehlerhalt nach Ampelfreigabe

Um 14:39:01,316 MEZ trat auch unter 0.4.10 eine technische Pedalausgabepause auf. Der Nutzer meldete eine Bremsung an einer grünen Ampel. Die gesicherte Aufnahme trennt den zuvor geplanten Signalhalt vom erneuten Fehlerhalt: Ab 14:38:56,138 meldete der zugeordnete SDK-Actor den freigegebenen Zustand 8; StopIdentity entfiel, die Bremse wurde gelöst und Gas ausgegeben. Alle 73 folgenden gespeicherten Motionbilder bis zum Fehler enthalten eine Bremsanforderung von null. Der nächste begrenzende Actor lag über 253 m voraus und führte ebenfalls nicht zu einer aktuellen Bremsanforderung. Die Aufnahme beweist die vom Code zugeordnete Signalquelle, nicht unabhängig die visuelle Ampelzuordnung.

Beim Fehler war der letzte akzeptierte Pedalbefehl 174,95 ms alt. Der Fehlerauszug nennt 9,33 km/h; die 3,5 km/h aus der späteren Statuslogzeile sind nicht das Tempo am Auslösezeitpunkt. Zwischen dem Abschluss des vorherigen Durchlaufs und dem Beginn des nächsten lagen 158,78 ms. Beim Auslösen war der neue Durchlauf erst 1,19 ms und seine Phase `Preparing` erst 0,71 ms alt. Die gültige Route, frische Telemetrie und der tatsächlich empfangene vorherige Gasbefehl widersprechen einem zu diesem Zeitpunkt fortbestehenden Ampel-Bremsbefehl. Die genaue Ursache der Ausgabepause bleibt offen.

Lokale Sicherung: `outputs/incident-0410-green-20261118-133901Z/last-fault.json`, SHA-256 `E4E775279121147FF447FFC7FB2093A94778028C84A98236942B9E4E0EBA61E2`. Der kritische Gegenreview bestätigt die getrennten Phasen. Die laufende Fahrt wurde nicht verändert. Dieser Vorfall widerlegt eine allgemeine Behebung der Ausgabeausfälle durch die Katalogoptimierung; deren unabhängig gemessener Leistungsgewinn bleibt bestehen.

## Paketidentität

- Plugin SHA-256: `952AB9555E4FE77051FD9ED2184254C52A94BDA63FCECFAA606B2C8B56C44BB7`.
- Paketmanifest SHA-256: `CCE7448254BE3B64E94EC8313F8D6C25067C8BE014C06ECB3AE2DCBDF606ACE8`.
- Installer SHA-256: `570176F3F9EA62C1699137D27AE79B77EE6BA6ED44FC1106AC8AD39B50CDC06E`.

Das Paket enthält fünf geprüfte Appdateien; inhaltlich ändert sich nur das Plugin. Die vier Hostdateien und die eingebettete Kompatibilität bleiben exakt auf dem geprüften Stand von 0.4.9. Worker- und Spieldateien wurden nicht ersetzt. Der Installer verlangte beendetes ETS2LA samt Hilfsprozess und erstellte eine geprüfte Sicherung. Die unabhängige Endprüfung bestätigte fünf Live- und fünf Sicherungsdateien sowie alle 13 unveränderten Workerdateien, Spiel-SDK und InputBridge. Keine Anwendung wurde gestartet; Fahrtabnahme bleibt offen.

17 isolierte Installations-/Rücksetzsimulationen bestehen, einschließlich aller fünf Abbruchpositionen, währenddessen gestarteter Anwendung, Fremdbytes und umgeleiteter Pfade. Die echte Paketprüfung gegen die laufende Installation war ausschließlich lesend und bestand ebenfalls.

## Erste Liveprüfung nach Nutzerstart

ETS2LA wurde vom Nutzer um 14:25 MEZ gestartet. Zunächst waren die Module ausgeschaltet; im bestätigten Stand wurden Mautassistent, Pathfinding und Lane Assist einzeln aktiviert. Der native ACC blieb aus. Alte Statusdaten von 0.4.9 am Aufnahmebeginn gelten nicht als geladene Version; ab 14:28:16 MEZ enthält die Aufnahme frische 0.4.10-Daten, zunächst mit ausgeschalteter Assistenz. Die Startbereitschaft wurde separat im direkt gelesenen Status um 14:28:56 bestätigt; das Aufnahmeformat enthält dieses Feld nicht.

Der Nutzer bestätigte F8. Von 14:29:23,09 bis 14:29:49,44 MEZ sind 239 Messpunkte über 26,35 Sekunden mit aktiver Assistenz, Feststellbremse, Stillstand und tatsächlichen Spielpedalen 0/0 vorhanden. Die eigene Regelung meldet dabei Parkneutralisierung. Das ist kein 30-Sekunden-Test. Danach wurde die Feststellbremse gelöst und angefahren; bis 14:30:52 sind etwa 50 km/h, tatsächlich empfangenes Gas und kein neuer Fehlerhalt aufgezeichnet. Luftdruck blieb ohne Warnung. Keine eigenen Fahrzeug- oder Fenstereingriffe während der Fahrt.

Lokale Belege: `outputs/stand-0410-20261118-1427.jsonl` und `outputs/stand-0410-20261118-summary.json`. Eine zweite, getrennte Aufnahme von 14:33:45 bis 14:36:45 MEZ enthält 1.623 Messpunkte und 176 Statusbilder, alle mit aktiver 0.4.10. Tempo etwa 34–68 km/h; kein aufgezeichneter Fehlerhalt oder Pedalempfangsfehler, Lenkalter in den Statusbildern höchstens 33,21 ms und keine Luftdruckwarnung. Kabinen- und Chassisschaden blieben unverändert. Sechs Statusbilder melden eingeschränkte Gaswirkung ohne fehlenden logischen Empfang; außerdem ein vorübergehend unlesbarer Status und fünf während des Lesens weitergeschriebene Telemetriesamples. Diese Aufnahme ist daher kein lückenloser Echtzeitnachweis. Belege: `outputs/drive-0410-20261118-1434.jsonl` und zugehörige `-summary.json`; der Dateiname bezeichnet nicht den genauen Startzeitpunkt.

Die Beobachtung belegt weder eine vollständige Mautdurchfahrt noch einen Hintergrundpedalvergleich oder die Ursache früherer Ausgabelücken. Die erneute manuelle Übernahme unter 0.4.10 ist noch zu prüfen. Danach den tatsächlichen Katalog-Wiederanlauf und die Ausgabealter beobachten. Auflieger-Nachlauf, Hintergrundpedale und die übrigen offenen Punkte bleiben in [WORKLIST.md](WORKLIST.md) aufgeführt.
