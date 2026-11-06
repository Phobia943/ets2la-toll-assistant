# Nächste Sitzung

Aktualisierung vom 6. November: **0.3.7 läuft im Spiel.** F8 und frische Lenksignale wurden bestätigt. Beim gezielten Deaktivieren von Lane Assist im Stand verriegelte die Überwachung nach 152 ms ohne Signal die Fehlerbremsung und schaltete die Assistenz aus. Der Fahrer bestätigte die anschließende manuelle Übernahme und Controller-Lenkung; Lane Assist wurde wieder eingeschaltet. Die separate SDK-Aufnahme endete vor dem Auslösen und belegt deshalb die tatsächliche Bremsausgabe nicht. Diese Messung im Stand und die vollständige Mautdurchfahrt bleiben offen. Ein unabhängiger Ausfall mit knapp zwei Sekunden fehlenden Lenk- und ACC-Aufzeichnungen ist in VALIDATION.md dokumentiert; seine Ursache bleibt ungeklärt. Keine Plugin- oder Grenzwertänderung aus diesem Befund.

Vorbereitet ist inzwischen **0.3.8**: Abschaltung und Bremsbefehl gehen der nativen Diagnose voraus; deren Ausführung hält die Lenküberwachung nicht mehr gesperrt. Ein verspätetes Ergebnis nach manueller Übernahme wird verworfen. Vorher reproduziert und danach mit 68 vollständigen lokalen Tests sowie 42 Logikprüfungen geprüft. **Noch nicht installiert.** Austausch erst bei geschlossenem ETS2LA und stehendem Lkw.

Für die nächste Prüfung: 0.3.8 im Stand installieren und über die übliche Verknüpfung laden. Bei Feststellbremse F8 aktivieren, dann eine neue SDK-Aufnahme starten. Vor dem gezielten Abschalten von Lane Assist anhand des laufenden Aufnahmeprozesses und einer frischen Zeile prüfen, dass die Aufnahme tatsächlich noch läuft. Während der Fehlerbremsung bei Spielfokus mehrere SDK-Pedalwerte erfassen, bevor Bremse oder F9 quittieren. Anschließend manuelle Übernahme und fehlenden Neustart nach Loslassen prüfen, Lane Assist wieder bereitstellen und Assistenz aus lassen. Ein abgelaufenes Aufnahmefenster darf nicht als Nachweis der Auslösung gelten.
## Bisheriger Abschluss und ursprünglicher Prüfplan

Abschluss vom 5. November 2026: **0.3.7 installiert**, noch nicht im Spiel gestartet oder fahrgeprüft. Spiel und ETS2LA sind beendet. 67 vollständige lokale Prüfungen bestanden; die unabhängige Logik umfasst 42 Prüfungen. Der Installations-Hash beginnt mit `E2EC56944A7C76A1`. Rücksicherung von 0.3.6 liegt lokal beim Installationspaket.

## Enthalten

- Bereits gelieferte Pause und Wiederaufnahme erreichen die unabhängige Lenküberwachung auch bei beschäftigter Routenverwaltung.
- Native Lenkfehler und bereits geladene Lenkparameter werden bei einem Ausfall rein lesend aufgezeichnet. Unbekannte Bibliotheksstände werden ausdrücklich nicht mit geratenen Feldern ausgewertet.
- Die letzte akzeptierte Route bleibt für die Fehleranalyse als getrennte Kopie mit Alter, Kurvenauswahl und Spurwechselzustand erhalten, auch wenn der native Planer danach nur eine leere Fehlerroute liefert.
- Eine ältere Lenkausfallmeldung wird einem späteren unabhängigen Routenfehler nicht mehr als aktuelle Ursache zugeordnet.
- Bremsübernahme, Ausgabeüberwachung und bestehende Mautbegrenzung bleiben enthalten. Diagnosekopien erlauben keine Weiterfahrt trotz Fehler.

## Reihenfolge morgen

1. ETS2LA über die übliche Verknüpfung starten. Geladenen Stand anhand frischer Diagnosedaten auf 0.3.7 prüfen. Die vier Module bereitstellen; Overlay ausgeschaltet lassen. Im Stand mit Feststellbremse bleiben, F8 zunächst aus.
2. Zuerst die Ausgabeüberwachung im Stand prüfen: F8 aktivieren, frische Lenksignale bestätigen, dann Lane Assist gezielt deaktivieren. Abschaltung und gehaltene Bremsanforderung prüfen; bei Spielfokus zusätzlich die tatsächlichen SDK-Pedalwerte lesen. Keine Fahrbewegung für diesen Test.
3. Mit physischer Bremse beziehungsweise F9 manuell übernehmen. Fehlerbremsung muss quittiert bleiben, Controller-Lenkung muss funktionieren; Loslassen darf nicht automatisch neu starten. Lane Assist wieder bereitstellen, Fahrassistenz zunächst aus lassen.
4. Erst danach kurze beaufsichtigte Straßen-/Kurvenprobe, möglichst im Einzelspieler mit Savepoint und gleicher Beladung. Bei erneutem Ausfall die neue native Fehlermeldung, Reglerwerte und Routen-/Spurwechselzustände sichern. Nicht durch wiederholtes F8 die Ursache übergehen.
5. Anschließend den vollständigen Mautdurchlauf aus [VALIDATION.md](VALIDATION.md) prüfen: Lkw-Spur, Zahlart, Anfahrt, nötiger Halt, verzögerte Freigabe, langsame Passage, Ausfahrt und manuelle Übernahme.

## Weiter offen

Die ursprüngliche Ursache des Kurvenunfalls ist nicht bewiesen behoben. Gesichert sind sowohl ausbleibende Lenksignale bei weiterhin gültiger Route als auch ein anderer nativer Routenabbruch nach einem protokollierten Spurwechsel. Für beide fehlen im alten Stand bestimmte native Zustandsdaten. Die neue Diagnose soll diese Lücke schließen. Eine vollständige Mautdurchfahrt mit dem aktuellen Stand ist ebenfalls noch nicht nachgewiesen.
