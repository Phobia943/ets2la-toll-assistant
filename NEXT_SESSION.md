# Nächste Sitzung

Stand: 13. November 2026, nach Beenden von Spiel und ETS2LA.

## Installiert und gesichert

- **Mautassistent 0.3.10** ist installiert und per Dateiversion sowie SHA256 verifiziert: `A6A5AD051213393072F646556F35CF10747D7930696BEE54FA22862C1B1E784A`. Nach dem Austausch wurde er noch nicht neu gestartet oder im Spiel geprüft. Die vorherige 0.3.9 liegt im lokalen Installationspaket als Rücksicherung.
- Geprüfte Kombination: ETS2LA 2026.11.5297, ETS2/SDK 1.62, Pathfinding 1.2.9, Lane Assist 1.6.2, PathLib 1.2.8 und ACC 2.1.1. Fremde DLLs und Rohaufnahmen bleiben außerhalb des Repositorys.
- Diagnose-Serialisierung und Schreibzugriffe sind vom Fahrzyklus getrennt. 72 Integrationstests, 44 unabhängige Logiktests und 27 Aufnahmeprüfungen bestehen. Die Suiten ersetzen keinen Fahrnachweis.
- In der lokalen Lane-Assist-Einstellung wurde bei beendeten Anwendungen ausschließlich **WidenTurnsWithTrailer = false** gespeichert. Die unveränderte Ausgangsdatei ist gesichert. Eine rekonstruierte vordere Kurvengeometrie überschreitet mit dem früheren Zusatzversatz die native Abstandsgrenze; ohne ihn nicht. Wirkung und Aufliegerführung sind noch im Spiel zu prüfen.
- Moderne Pedalausgabe bleibt eingeschaltet, Overlay ausgeschaltet. F8/F9/F10/F11 und die physische Bremsübernahme bleiben erhalten. Die Bremsachse ist weiterhin Axis Z / SplitPos; ohne Anlass nicht erneut anlernen.
- Beide Anwendungen sind beendet. Die Aufnahmen sind beendet; es läuft keine behauptete Hintergrundbeobachtung.

## Belegt und noch offen

Eine rollende Mautpassage mit **0.3.9** wurde aufgezeichnet und vom Fahrer bestätigt: zweite Lkw-Spur von rechts, automatische Abrechnung, langsame Anfahrt und Passage, anschließend stabile Route und keine neuen Kollisionsschäden. Das Tor öffnete vor einem längeren Stillstand. Der Halt vor einer geschlossen bleibenden Schranke ist damit nicht validiert. Die separate physische Bremsübernahme mit 0.3.9 wurde bestätigt. Einzelheiten, Abtastgrenzen und die Anforderungsmatrix stehen in [VALIDATION.md](VALIDATION.md).

Nach der Reparatur trat ein weiterer nativer Kurvenabbruch auf. Der seitliche Zusatzversatz erklärt die rekonstruierte Abstandsablehnung, aber keine beliebige frühere Signallücke. Ein anderer Routenabbruch betrifft einen unvollständigen Spurwechsel auf einem kurzen Straßenabschnitt. Keine Abschaltschwelle wurde gelockert.

Die neue Luftdruckaufnahme enthält die tatsächlichen Warn-/Notfallflags und fahrzeugspezifischen Schwellen. Zuletzt waren beide Flags aktiv und die Simulation pausiert. Ein Druckaufbau ist noch nicht nachgewiesen; ältere Aufnahmen enthalten keinen Luftdruck. Keinen früheren Vorfall nachträglich allein damit erklären.

## Nächste praktische Prüfung

1. Nutzer startet TruckersMP und ETS2LA über die gewohnten Verknüpfungen und lädt den Lkw im Stand. Assistenz aus, Feststellbremse an. Motor im Leerlauf laufen lassen und bei Bedarf mit leichtem Gas Luft aufbauen; die Simulation muss laufen. Erst aktuelle Druckwerte und erloschene Warn-/Notfallflags bestätigen.
2. Geladene 0.3.10, aktuelle SDK-Daten, vier eingeschaltete Module, Full Self-Driving und die gespeicherte Kurveneinstellung prüfen. Keine Fahrfreigabe aus einer alten Statusdatei ableiten.
3. Zuerst mit Feststellbremse F8 aktivieren und die physische Bremsübernahme beziehungsweise F9 testen. Ausgeschaltete Assistenz und normale Controller-Lenkung tatsächlich bestätigen.
4. Danach vorsichtiger Kurvenvergleich mit Auflieger: frische Lenkbefehle, Abstand zur Route und tatsächliche Innen-/Außenfreiheit beobachten. Die abgeschaltete Zusatzaufweitung ist noch kein Fahrnachweis.
5. Vollständige klassische Mautdurchfahrt einschließlich Halt, falls das Tor geschlossen bleibt, und verzögerter Freigabe prüfen. Dieselbe Aufnahme muss Anfahrt, Tor, Passage, Ausfahrt und anschließende manuelle Übernahme enthalten. Einen offenen oder vorzeitig geöffneten Übergang entsprechend als Teilnachweis kennzeichnen.
6. Bei normaler Fahrt Luftdruck und Bremsbefehle gemeinsam erfassen. Erst danach über Bremsflattern oder Druckluftverbrauch als Ursache entscheiden.

Für eine neue Aufnahme PowerShell 7 verwenden, einen neuen Dateinamen wählen und vor dem Test einen tatsächlich laufenden Prozess sowie frische vollständige Messzeilen prüfen:

```powershell
./Tools/Record-TollDrive.ps1 -EtsRoot 'D:\ETS2LA\current' -OutputPath './neue-mautfahrt.jsonl' -DurationSeconds 600 -IncludePedalOutput
```

Während einer Fahrt nur lesen, keinen Fensterfokus wechseln und keine Plugin-Dateien oder Einstellungen austauschen. Vor späteren Änderungen aktuellen Stillstand und manuelle Übernahme prüfen; vor einem DLL-Austausch ETS2LA beenden.

**TruckersMP bleibt Pflicht.** Hintergrund-Gas ist weiterhin nicht gelöst. Der nur für Einzelspieler vorgesehene Focus Keeper wurde nicht installiert und ist kein Ersatz. Messfolge: [BACKGROUND_INPUT.md](BACKGROUND_INPUT.md).