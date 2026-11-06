# Hintergrundbetrieb unter TruckersMP

Stand: 6. November 2026. **Hintergrundfahrt ist nicht gelöst. TruckersMP-Unterstützung bleibt eine Anforderung.** Die Einzelspieler-Lösung ersetzt sie nicht.

## Gemessene Kombination

- ETS2LA 2026.11.5251, Mautassistent 0.3.8, ETS2 1.61 über TruckersMP.
- Der moderne Pedalausgang wurde bei geschlossenem ETS2LA in dessen `GameSettings.json` mit `EnableModernOutputForPedals: true` aktiviert. Der Mautassistent schaltet diese globale Option nicht selbst um.
- Der Spiel-Plugin-Status meldete Version `1610`. Die Aufnahme öffnete sämtliche Speicherbereiche ausschließlich lesend.
- Die Tests fanden im Stand mit Feststellbremse statt. Die Spielsimulation war nicht pausiert. Es wurde kein Fahr- oder Bremswegversuch durchgeführt.

## Ergebnis

| Prüfung | Beobachtung | Aussage |
| --- | --- | --- |
| Automatische Fehlerbremsung bei ausgeschaltetem Lane Assist | Etwa 168 ms seit dem letzten Lenksignal; Assistenz aus, Fehlerbremsung verriegelt. Moderner Pedalbefehl −1, Übernahme vom Spiel-Plugin gemeldet, SDK `gameBrake = 1`, `gameThrottle = 0` im Hintergrund | Bremsbefehl erreicht in dieser Konfiguration das Spiel. Die getrennte physische Bremsachse war dabei 0 |
| Derselbe Bremszustand bei Spielfokus | SDK meldet ebenfalls volle Bremse und kein Gas | Vordergrundübernahme des Bremsbefehls bestätigt; weitere Werte nach manueller Quittierung gehören nicht zur Verriegelungsphase |
| Manuelle Übernahme | Fahrer tippt Controller-Bremse an, bestätigt anschließend normale Lenkung. Verriegelung und moderne Pedalübernahme werden gelöst; Assistenz bleibt aus | Übernahme nach diesem Standtest bestätigt |
| Gas bei Spiel im Hintergrund | Anforderung +1 und Übernahme vom Spiel-Plugin dauerhaft gemeldet, SDK `gameThrottle` bleibt 0 beziehungsweise numerisch praktisch 0; Motor bleibt bei rund 500 U/min | Eine bestätigte Speicherübernahme beweist keine wirksame Beschleunigung |
| Rückkehr ins Spielfenster | Unveränderte Anforderung +1, SDK `gameThrottle` etwa 0,17–0,67, Drehzahl etwa 539–554 U/min | Motor und wirksamer Gaswert reagieren wieder auf den Fensterwechsel |
| Erneuter Hintergrund | Weiterhin Anforderung +1; wirksames Gas praktisch 0, Drehzahl etwa 475 U/min | Fokusabhängigkeit reproduziert; die Option allein behebt sie nicht |

Die SDK-Felder `userThrottle` und `userBrake` sind logische Spiel-Eingänge und können bereits die SDK-Übersteuerung enthalten. Sie dürfen nicht als reine physische Controller-Eingabe ausgelegt werden. Die moderne Befehls- und Statusquelle sowie die Spieltelemetrie werden nacheinander gelesen, nicht atomar. Einzelwerte am Umschaltpunkt genügen deshalb nicht als Nachweis; erforderlich sind mehrere unveränderte Beobachtungen vor und nach dem Wechsel. Die lokal gesicherten Rohaufnahmen enthalten Zeitstempel und Spielpositionen und werden nicht veröffentlicht.

## Bekannte Ansätze und Grenze

Der [ETS2LA-Bericht #642](https://github.com/ETS2LA/ETS2LA/issues/642) enthält einen separaten Focus Keeper. Dessen beigefügter Quellcode und README beschränken die Verwendung ausdrücklich auf Einzelspieler und schließen TruckersMP aus. Die bereitgestellte Binärdatei wurde weder ausgeführt noch installiert. Die Aussage des Autors ist keine unabhängige Prüfung der TruckersMP-Regeln. Aus allgemeiner ETS2LA-Unterstützung darf keine Freigabe dieses zusätzlichen Eingriffs abgeleitet werden.

Die geprüfte [offizielle TruckersMP-Eingabedokumentation](https://github.com/TruckersMP/Docs/blob/master/client-sdk/modules/input.md) beschreibt Eingabeereignisse, Mausfreigabe und Eingabesperren für Oberflächen. Darin ist keine Funktion zum Erzwingen von Hintergrund-Gas oder zur direkten Pedalsteuerung dokumentiert. Damit liegt noch kein nachgewiesener Ersatzweg vor. Ein eigener Ansatz muss sowohl die technische Wirkung als auch die TruckersMP-Verträglichkeit nachweisen; die Bremsmessung allein reicht dafür nicht.

## Weitere Prüfung

Mit `Tools/Record-TollDrive.ps1 -IncludePedalOutput` zusätzlich zum SDK den modernen Ausgang und dessen Bestätigung aufzeichnen. Der Schalter benötigt die vorhandenen Speicherbereiche `Local\ETS2LAPluginInput` (26 Byte) und `Local\ETS2LAPluginStatus` (6 Byte); er erzeugt sie nicht und schreibt nichts hinein. Ohne diesen Schalter bleibt die Aufnahme der Ausgangsspeicher ausgeschaltet.

Für einen neuen Kandidaten dieselbe Folge messen: Assistenz aus und Motor im Leerlauf; im Stand bei Feststellbremse aktivieren; Hintergrund, Vordergrund und erneut Hintergrund vergleichen; Abschaltung und physische Bremsübernahme prüfen. Erst nach erfolgreichem Standnachweis einen beaufsichtigten Fahrversuch durchführen. Das ursprüngliche Ziel der vollständigen Mautdurchfahrt mit geeigneter Spur und stabiler Ausfahrt bleibt davon unabhängig offen.
