# Rückblick mit 0.3.13: nicht alle Probleme behoben

Stand: 14. November 2026. Die neue DLL ist installiert, aber noch nicht im Spiel gestartet. Auf Wunsch des Nutzers erfolgt heute kein weiterer Start. Diese Untersuchung arbeitet ausschließlich mit vorhandenen Aufnahmen und getrennten lokalen Prüfprogrammen.

## Zusätzliche Wiederholung gespeicherter Entscheidungen

Von 24 deduplizierten Fehlerzeit-/Meldungspaaren besitzen 16 einen gespeicherten Verlauf der Fahrüberwachung. Insgesamt wurden 8.192 Entscheidungen mit dem aktuellen `DriveSupervisor` wiederholt. Bei 15 Verläufen stimmen die Entscheidungen vollständig mit der Aufzeichnung überein; bei einem unterscheidet sich genau eine Entscheidung. Acht Diagnosefälle enthalten keinen solchen Verlauf. Die Fälle umfassen auch Standprüfungen und mehrere Meldungen desselben Vorfalls, nicht 24 unabhängige Fahrunfälle.

Die Abweichung betrifft den harten Stopp vom 4. November wegen „Keine stabile Fahrentscheidung verfügbar“: Nach einer kurzen Unterbrechung kam bereits wieder eine gültige Fahrentscheidung bei frischer Route an. Der alte Verlauf wechselte an dieser Stelle auf `EmergencyStop`; die aktuelle Logik liefert `Drive`. Damit ist genau diese unnötige Abschaltung auf Ebene der gespeicherten Überwachungseingaben nicht mehr reproduzierbar. Das beweist keine vollständige unfallfreie Weiterfahrt.

Übereinstimmung bedeutet ebenso wenig, dass die ursprünglichen Fehler beseitigt sind. Diese Wiederholung hält vorgelagerte Eingaben wie „Route abgelehnt“ oder „Entscheidung bereit“ fest. Sie berechnet keine neue Spur, simuliert keine Fahrzeugphysik und prüft keine Hindernisse oder tatsächliche Pedalwirkung. Eine durch die neue Modellprüfung geänderte Route muss separat geprüft werden. Der Kollisionsdetektor benötigt zusätzliche Schadens- und Quellzeitdaten und wird von dieser vereinfachten Wiederholung nicht ausgeführt.

Codebasis der ausführbaren Prüfung: `202acdf597cef32731d26b9b947db516daba1d99`. Lokale Rohberichte bleiben außerhalb des Repositorys. Die eine historische Entscheidungsabweichung ist ein Vergleichsbefund, kein fehlgeschlagener aktueller Integrationstest.

## Bewertung der Problemgruppen

| Problem | Aussage aus den vorhandenen Daten |
| --- | --- |
| Zwei griechische Mautanlagen werden abgelehnt | Die getrennte Kartenwiederholung mit aufgezeichneten SDK-Schranken wählt unter 0.3.13 eine verbundene automatische Spur. Einer der Fälle ist nur über drei Abschnitte verifiziert, der andere über fünf. Spurwahl korrigiert; physische Durchfahrt noch nicht belegt. |
| Maut scheitert in verschiedenen Regionen | 16 von 81 gefundenen Modelldefinitionen wurden früher wegen des Ordners gar nicht geprüft. Der Produktionskatalog erfasst jetzt alle 81. Die Zahlungsdefinitionen werden breiter erkannt. Daraus folgt weder, dass überall eine automatische Lkw-Spur existiert, noch dass alle Länder und Modellvarianten erfolgreich durchfahren wurden. |
| Früherer harter Stopp nach kurzer Entscheidungsunterbrechung | Ein konkret gespeicherter Fall liefert jetzt `Drive` statt `EmergencyStop`. Andere Bremsungen können andere Ursachen haben. |
| Lenkung oder Tracking fällt scheinbar grundlos aus | Einzelne Ursachen wie blockierende Diagnosearbeit und ein problematischer Kurvenversatz wurden untersucht. Die ursprüngliche Ursache aller spontanen Lenk-/ACC-Pausen ist nicht nachgewiesen beseitigt. Die Schutzabschaltung behandelt die Folgen, nicht jede Ursache. |
| Unfall in Baustelle | Nicht gelöst als vorausschauende Hinderniserkennung. Die alte Aufnahme zeigt frische Spurführung bis zum abrupten Stopp, enthält aber keine passenden Schadenswerte zur Prüfung des neuen Detektors. 0.3.13 kann nach einer erkannten Kollisionssignatur abschalten; der erste Aufprall wird dadurch nicht verhindert. |
| Zu wenig Druckluft | Die lokale Standard-Pedalausgabe zeigte im vierminütigen Vergleich deutlich geringeren Verbrauch und keine Warnflags. Das ist eine beobachtete Abhilfe, keine Langzeitgarantie oder abschließend lokalisierte SDK-Ursache. |
| Gas/Bremse nach Alt+Tab | Nachweislich offen. In der gespeicherten Standardaufnahme bleiben ungefähr 17 Sekunden Bremsanforderung ohne gemeldete Spielbremsung. Der Nutzer bestätigt den Fokuswechsel. 0.3.13 ändert den Ausgabeweg nicht. |
| Halt vor geschlossener Schranke, Zahlzone und Ausfahrt | Eine frühere rollende automatische Passage ist vom Fahrer bestätigt. Ein vollständiger Halt vor länger geschlossen bleibender Schranke und anschließende Freigabe mit 0.3.13 fehlen. Manuelle Zahlspuren bleiben manuell; der tatsächliche Zahlpunkt ist nicht generell gelöst. |

Die Schlussfolgerung ist daher ausdrücklich: **Mehrere konkrete Fehler sind offline korrigiert oder eingegrenzt, aber ein funktionierender Assistent für alle bisherigen Problemsituationen ist mit 0.3.13 nicht nachgewiesen und in den genannten offenen Bereichen auch nicht implementiert.**

Die nächsten Schritte erfordern keine zusätzliche Fahrt heute Abend. Bei einer späteren freiwilligen Prüfung zuerst geladene Version und manuelle Übernahme im Stand bestätigen; anschließend eine zusammenhängend aufgezeichnete klassische Mautpassage prüfen. Für Baustellen ist eine nachgewiesene vorausschauende Hindernisquelle nötig. Großzügigere Trackinggrenzen oder eine bloße Abschaltung nach dem Aufprall ersetzen sie nicht.
