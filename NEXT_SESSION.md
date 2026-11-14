# Nächste Sitzung

Stand: 14. November 2026, nach erneuter Beobachtung mit SDK 1.62 und neuen Karten-DLCs. Anwendungen waren zuletzt aktiv; vor Änderungen den tatsächlichen Stillstand und Prozesszustand erneut prüfen.

## Installiert und gesichert

- **Mautassistent 0.3.11** ist installiert und per Dateiversion sowie SHA256 verifiziert: `4D96A516F16D67267251D29A16CD283EF9523439D1350D71ECB22021A7173486`. Geladene Version, aktive Assistenz und anschließende manuelle Übernahme wurden am 14. November beobachtet. Die vorherige 0.3.10 liegt im lokalen Installationspaket als Rücksicherung. Die aktuelle griechische Anlage hat noch keine bestandene automatische Durchfahrt.
- Geprüfte Kombination: ETS2LA 2026.11.5297, ETS2/SDK 1.62, Pathfinding 1.2.9, Lane Assist 1.6.2, PathLib 1.2.8 und ACC 2.1.1. Fremde DLLs und Rohaufnahmen bleiben außerhalb des Repositorys.
- Diagnose-Serialisierung und Schreibzugriffe sind vom Fahrzyklus getrennt. 0.3.11 korrigiert zusätzlich den Bezugszeitpunkt der Diagnosealter; der vor der Korrektur scheiternde Test liefert während des Fahrzyklus einen neuen Lenkbefehl. Alte Aufnahmen mit leicht negativen Altersangaben bleiben unverändert und können die strenge zusammenhängende Ausfahrtprüfung weiterhin nicht erfüllen. 73 Integrationstests, 44 unabhängige Logiktests und 43 Aufnahmeprüfungen bestehen. Die Suiten ersetzen keinen Fahrnachweis.
- In der lokalen Lane-Assist-Einstellung wurde bei beendeten Anwendungen ausschließlich **WidenTurnsWithTrailer = false** gespeichert. Die unveränderte Ausgangsdatei ist gesichert. Eine rekonstruierte vordere Kurvengeometrie überschreitet mit dem früheren Zusatzversatz die native Abstandsgrenze; ohne ihn nicht. Wirkung und Aufliegerführung sind noch im Spiel zu prüfen.
- Moderne Pedalausgabe bleibt eingeschaltet, Overlay ausgeschaltet. F8/F9/F10/F11 und die physische Bremsübernahme bleiben erhalten. Die Bremsachse ist weiterhin Axis Z / SplitPos; ohne Anlass nicht erneut anlernen.
- Die zuletzt bestätigte Fahrt läuft mit eingeschalteter moderner Pedalausgabe. Eine andere Ausgabe ist nur vorbereitet. Laufende Beobachter haben feste Laufzeiten; vor Fortsetzung ihren tatsächlichen Prozesszustand und frische Messzeilen prüfen.

## Belegt und noch offen

Eine rollende Mautpassage mit **0.3.9** wurde aufgezeichnet und vom Fahrer bestätigt: zweite Lkw-Spur von rechts, automatische Abrechnung, langsame Anfahrt und Passage, anschließend stabile Route und keine neuen Kollisionsschäden. Das Tor öffnete vor einem längeren Stillstand. Der Halt vor einer geschlossen bleibenden Schranke ist damit nicht validiert. Die separate physische Bremsübernahme mit 0.3.9 wurde bestätigt. Einzelheiten, Abtastgrenzen und die Anforderungsmatrix stehen in [VALIDATION.md](VALIDATION.md).

Nach der Reparatur trat ein weiterer nativer Kurvenabbruch auf. Der seitliche Zusatzversatz erklärt die rekonstruierte Abstandsablehnung, aber keine beliebige frühere Signallücke. Ein anderer Routenabbruch betrifft einen unvollständigen Spurwechsel auf einem kurzen Straßenabschnitt. Keine Abschaltschwelle wurde gelockert.

Die Aufnahme vom 14. November zeigt wiederholten starken Luftdruckverlust bei aktiver Assistenz, danach Warn-/Notfallflags und erneuten Druckaufbau nach manueller Übernahme. Der moderne negative Pedalbefehl und die tatsächliche Bremsmeldung passen zeitweise nicht zusammen. Der verantwortliche Ausgabeweg ist noch nicht bewiesen. Die dreiminütige hochauflösende Zusatzaufnahme enthält keinen ausreichend langen geeigneten Bremsabschnitt. Ein kontrollierter Vergleich mit Standard-Pedalausgabe benötigt ETS2LA-Neustart im sicheren Stand; bis dahin keine Umstellung während der Fahrt. Ältere Vorfälle nicht rückwirkend allein damit erklären.

Die griechische Mautanlage wird wegen aktiver Modell-Zusatzteile vollständig von der Zahlungsarten-Erkennung verworfen. Automatische Zahlungsmarker sind offline nachweisbar, aber ein aktiver Höhenbalken erfordert eine zusätzliche geometrische Freigabe. Die Sperre nicht pauschal entfernen oder eine Tor-ID ohne Höhenprüfung freigeben. Details und Grenzen: [VALIDATION.md](VALIDATION.md).

## Nächste praktische Prüfung

1. Nutzer startet TruckersMP und ETS2LA über die gewohnten Verknüpfungen und lädt den Lkw im Stand. Assistenz aus, Feststellbremse an. Motor im Leerlauf laufen lassen und bei Bedarf mit leichtem Gas Luft aufbauen; die Simulation muss laufen. Erst aktuelle Druckwerte und erloschene Warn-/Notfallflags bestätigen.
2. Geladene 0.3.11, aktuelle SDK-Daten, vier eingeschaltete Module, Full Self-Driving und die gespeicherte Kurveneinstellung prüfen. Keine Fahrfreigabe aus einer alten Statusdatei ableiten.
3. Zuerst mit Feststellbremse F8 aktivieren und die physische Bremsübernahme beziehungsweise F9 testen. Ausgeschaltete Assistenz und normale Controller-Lenkung tatsächlich bestätigen.
4. Danach vorsichtiger Kurvenvergleich mit Auflieger: frische Lenkbefehle, Abstand zur Route und tatsächliche Innen-/Außenfreiheit beobachten. Die abgeschaltete Zusatzaufweitung ist noch kein Fahrnachweis.
5. Vollständige klassische Mautdurchfahrt einschließlich Halt, falls das Tor geschlossen bleibt, und verzögerter Freigabe prüfen. Dieselbe Aufnahme muss Anfahrt, Tor, Passage, Ausfahrt und anschließende manuelle Übernahme enthalten. Einen offenen oder vorzeitig geöffneten Übergang entsprechend als Teilnachweis kennzeichnen.
6. Bei normaler Fahrt Luftdruck und Bremsbefehle gemeinsam erfassen. Erst danach über Bremsflattern oder Druckluftverbrauch als Ursache entscheiden.
7. Standard-/moderne Pedalausgabe kontrolliert vergleichen: gleiche Beladung, aufgebauter Druck, zuerst Standtest mit Feststellbremse und funktionierender manueller Übernahme. Vor Umstellung muss ETS2LA beendet sein. Dauerhaft beheben erst nach unterscheidendem Messergebnis; Hintergrund-Gas für TruckersMP bleibt ein separates offenes Problem.
8. Für Mautmodelle mit Zusatzteilen die aktive Geometrie einschließlich Höhenbalken auswerten. Eine Zahlungszuordnung allein reicht nicht zur Wahl einer Lkw-Spur. Anschließend die betroffene griechische Anlage mit Lkw und Auflieger tatsächlich prüfen.

Für eine neue Aufnahme PowerShell 7 verwenden, einen neuen Dateinamen wählen und vor dem Test einen tatsächlich laufenden Prozess sowie frische vollständige Messzeilen prüfen:

```powershell
./Tools/Record-TollDrive.ps1 -EtsRoot 'D:\ETS2LA\current' -OutputPath './neue-mautfahrt.jsonl' -DurationSeconds 600 -IncludePedalOutput
```

Während einer Fahrt nur lesen, keinen Fensterfokus wechseln und keine Plugin-Dateien oder Einstellungen austauschen. Vor späteren Änderungen aktuellen Stillstand und manuelle Übernahme prüfen; vor einem DLL-Austausch ETS2LA beenden.

**TruckersMP bleibt Pflicht.** Hintergrund-Gas ist weiterhin nicht gelöst. Der nur für Einzelspieler vorgesehene Focus Keeper wurde nicht installiert und ist kein Ersatz. Messfolge: [BACKGROUND_INPUT.md](BACKGROUND_INPUT.md).
