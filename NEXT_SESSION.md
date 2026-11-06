# Nächste Sitzung

Stand: 6. November 2026, nach den Standprüfungen.

## Gesichert und installiert

- **Mautassistent 0.3.8** ist installiert und wurde live geladen. SHA256 der DLL: `325A0F71AF26F0E625C6FBA61FD279FC62736FFD6E157DB77B0CC057B65B253A`. Der Plugin-Build stammt aus Commit `db4b0b9`; spätere Änderungen ergänzen Messwerkzeuge und Nachweise.
- 68 vollständige lokale Prüfungen, 42 unabhängige Logikprüfungen und 21 Prüfungen der Aufnahmeauswertung und Datenformate. Diese Zahlen sind getrennte Suiten, keine addierte Fahrfreigabe.
- Der Bremsbefehl und die Abschaltung gehen der nativen Diagnose voraus. Eine langsame Diagnose hält die Lenküberwachung nicht mehr gesperrt. Ein nach manueller Übernahme eintreffendes Ergebnis wird verworfen. Der neue lokale Test scheiterte vor der Korrektur und besteht danach.
- Die globale ETS2LA-Option `EnableModernOutputForPedals` ist gespeichert eingeschaltet. Der Mautassistent schaltet sie nicht selbst um. Die vorherige Einstellung und die Rücksicherung der 0.3.7 liegen im lokalen Installationspaket.

## Im Spiel bestätigt

F8 und frische Lenksignale wurden bei Feststellbremse bestätigt. Gezieltes Ausschalten von Lane Assist löste nach rund 168 ms ohne Lenksignal Abschaltung und verriegelte Fehlerbremsung aus. Die Aufnahme lief vor dem Auslösen und bis nach der Quittierung. 631 unbeanstandete Hintergrund-Proben zeigen modernen Pedalbefehl −1, bestätigte Übernahme, SDK-Bremse 1 und Gas 0. Der getrennte physische Bremseingang war bei der Auslösung 0. Bei Spielfokus wurde ebenfalls volle SDK-Bremse gemessen. Der Fahrer bestätigte nach physischem Bremsantippen normale Controller-Lenkung. Danach waren Verriegelung und Pedalübernahme gelöst und die Assistenz blieb aus. Lane Assist wurde wieder eingeschaltet; alle vier Module waren bereit.

Das ist ein Nachweis der Ausgabe im Stand, kein Bremsweg unter Beladung und kein vollständiger Mautversuch. Die erste Aufnahme mit 0.3.7 endete vor ihrem Ausfalltest und darf dafür nicht herangezogen werden.

## Noch offen

1. **TruckersMP ist ausdrücklich Pflicht. Hintergrundfahrt ist nicht gelöst.** Trotz moderner Pedalausgabe kommen Gasbefehl und Übernahmebestätigung an, wirksames Gas und Motorreaktion fallen im Hintergrund jedoch aus. Vordergrund und erneuter Hintergrund reproduzieren den Unterschied. Details und Messfolge: [BACKGROUND_INPUT.md](BACKGROUND_INPUT.md). Der heruntergeladene Focus Keeper wurde nur als Quelltext geprüft, nicht ausgeführt oder installiert; sein Autor beschränkt ihn auf Einzelspieler. Er ist kein Ersatz für die geforderte TruckersMP-Lösung.
2. Die spontane Lenk-/ACC-Aufzeichnungslücke von knapp zwei Sekunden bei rund 28 km/h ist ungeklärt. Das native Lenkmodul lief ohne internen Fehler. Bei der anschließenden Fehleraufzeichnung kam bereits ein neues Lenksignal an. Gemeinsame Ausführungspause, Zustellverzögerung und überholtes Monitor-Snapshot sind nicht sicher unterschieden. Abschaltschwellen wurden nicht gelockert. Nächster sinnvoller Offline-Schritt: Ausführung der Host-Ticks und synchrone Diagnosearbeit auf mögliche Blockaden prüfen.
3. Ursprünglicher Kurvenunfall und weiterer nativer Routenverlust nach Spurwechsel sind nicht nachweislich an der Wurzel behoben. Die Diagnose bewahrt tatsächliche native Reglerwerte und ausgewählte Route/Spurwechselzustände für neue Vorfälle.
4. Vollständige Mautdurchfahrt mit dem aktuellen Stand: geeignete Lkw-Spur und Zahlart, stabile Schranke, frühes Abbremsen, Halt, zwei Sekunden Offenzeit, langsame Passage, stabile Ausfahrt und manuelle Übernahme. Ein Einzelspieler-Vorversuch ersetzt den geforderten TruckersMP-Nachweis nicht. Anforderungen: [VALIDATION.md](VALIDATION.md).

## Weiterarbeiten

Die Computerbedienung wurde zuletzt durch die physische Escape-Taste beendet. Ohne erneute Rückmeldung keine weitere Fensterbedienung oder Testaktivierung beginnen. Dateien, Code und gespeicherte Aufnahmen können weiter offline geprüft werden. Vor späteren Änderungen an der laufenden Assistenz aktuelle Daten, Stillstand und Feststellbremse erneut prüfen; vor einem DLL-Austausch ETS2LA beenden.

Für weitere Aufnahmen mit `Tools/Record-TollDrive.ps1 -IncludePedalOutput` einen neuen Dateinamen wählen. Vor gezieltem Auslösen anhand eines tatsächlich laufenden Aufnahmeprozesses und einer frischen vollständigen Zeile prüfen, dass das Fenster noch läuft. Eine fehlende oder abgelaufene Aufnahme nicht durch einen behaupteten Fahrnachweis ersetzen. Rohaufnahmen mit Spielpositionen bleiben außerhalb des öffentlichen Repositorys.
