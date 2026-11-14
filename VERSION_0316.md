# 0.3.16: Kurvenvorschau und korrigierte Zahlzonen-Vorbereitung

Offline-Testfassung, noch nicht installiert oder im Spiel gefahren. Lokal installiert bleibt 0.3.14. Die Ergänzungen aus der vorbereiteten 0.3.15 sind enthalten. Das optionale native TruckersMP-Messmodul bleibt unverändert auf Komponentenstand 0.3.15 und liefert weiterhin keine automatische Freigabe für engere Zahlhaltepositionen.

## Früher vor Kurven verlangsamen

Die Erweiterung berechnet aus der bereits geprüften Route eine zusätzliche Geschwindigkeitsgrenze für kommende Kurven. Sie verwendet echte Streckenmeter und horizontal gemessene Krümmung, unabhängig vom nativen Punktabstand, dessen Radiusaufweitung oder einer dort eingestellten Mindestgeschwindigkeit. Die vorhandenen nativen ACC-Begrenzungen bleiben aktiv; die Erweiterung kann deren stärkere Bremsanforderung nicht aufheben.

Die Vorschau umfasst höchstens 600 m. Drei benachbarte Messfenster müssen eine gleichgerichtete Biegung bestätigen; einzelne Punktspitzen werden dadurch nicht direkt zur engen Kurve. Es gibt mehrere Fenstergrößen für kurze und lange Biegungen. Höhenverlauf allein zählt nicht als Kurve. Markierte kurze Übergänge zwischen Kartenabschnitten liefern keine Krümmungsfreigabe. Die Auswertung verwendet die unveränderliche geprüfte Route und erfindet keine Fortsetzung über ihr Ende oder eine Planungslücke hinaus.

Der zusätzliche Planungswert verwendet 1 m/s² Querbeschleunigung, 1,5 Sekunden Reaktionsreserve, 8 m zusätzlichen Abstand und die vorhandene komfortable Verzögerung, standardmäßig 1,2 m/s². Er ist kein gemessener Kippschutz: Schwerpunkt, Ladung, Reifenzustand, Straßenreibung, Fahrzeugschäden und Bremsdruck sind damit nicht vollständig modelliert. Eine spät erkannte enge Kurve wird als knappe Bremsreserve angezeigt; die neue Begrenzung allein fordert keinen Fehlerstopp an. Stärkere vorhandene Maut-/Notfallbremsungen bleiben wirksam. Nach einer Biegung hält der geometrische Nachlauf höchstens 8 m an; daraus darf kein Nachweis für eine vollständig freie Aufliegerhülle abgeleitet werden.

Die Berechnung läuft mit höchstens einem aktiven und einem ersetzbaren neuesten Auftrag außerhalb der Fahrsteuerung. Ein gehaltenes Rechenmodul blockiert weder ACC noch Routenzustellung oder F9. Ein Ergebnis gilt nur für die zugehörige aktuelle Route. Fehlende oder fehlerhafte Zusatzvorschau ersetzt keine gültige Route und löst allein keine Bremsverriegelung aus. F9 beziehungsweise manuelle Bremsübernahme verwirft offene Aufträge und die Kurvenanzeige; ein verspätetes Ergebnis schaltet nichts wieder ein. „Lane Assist Only“ erhält dadurch keine Pedalsteuerung.

In der Oberfläche stehen bei eingreifender Vorschau die aktuelle Geschwindigkeitsgrenze, Kurvenentfernung und das geplante Kurventempo. Der Status enthält die zugehörigen Werte unter `curve`, Fehler der optionalen Berechnung unter `curvePreviewIssue`.

Die zusätzliche Vorschau ist standardmäßig eingeschaltet. Über „Zusätzliche Kurvenvorschau verwenden“ lässt sie sich für einen Vergleich im Stand bei ausgeschalteter Assistenz separat deaktivieren und speichern. Die nativen ACC-Begrenzungen bleiben dabei aktiv.

## Verlorene Schrankenpositionen bei manuellen Zahlzonen

Die Hintergrund-Modellprüfung kopierte bisher die Fahrkurven, ließ aber die Signalliste des Prefabs weg. Die Prüfung eines manuellen Modelltriggers benötigt genau diese Signalpositionen. Deshalb konnte sie selbst bei ansonsten passenden Modelldaten keinen gültigen Haltebereich bestätigen.

Jetzt übernimmt die unabhängige Arbeitskopie zusätzlich Schrankenkennung und Position. Der Test verwendet den echten asynchronen Lader mit gültiger Modellgeometrie und hält ihn vor der Auswertung an. Anschließend verändert er die ursprüngliche Schrankenposition: Das Ergebnis muss trotzdem zum ursprünglichen, kopierten Modell passen. Ein Vergleich mit der früheren signalarmen Kopie zeigt, dass damit derselbe gültige Haltebereich verloren geht.

Die zulässigen manuellen Trigger bleiben auf die bereits dokumentierte gerade, eindeutige Geometrie begrenzt. Die Korrektur lässt vorhandene passende Modell-Zahlhaltebereiche überhaupt im realen Ladevorgang ankommen; sie bestätigt keine Zahlungsreichweite jedes Lkw. Der Sicherheitsabstand wird weiterhin nicht automatisch verkleinert, und das Bezahlen bleibt manuell.

## Nachweise und Grenzen

- Im vereinfachten Längsfahrmodell fährt der Lkw mit 90 km/h auf eine 40-m-Kurve zu. Bei einer zusätzlichen Befehlsverzögerung von einer Sekunde beträgt die Geschwindigkeit im geprüften Kurvenabschnitt höchstens rund 24 km/h. Die angeforderte Zusatzverzögerung bleibt bei höchstens 1,2 m/s²; es wird kein Halt erzeugt. Das Modell simuliert keine native ACC-Regelung, Luftbremse oder reale Kipp-/Kollisionsphysik.
- Prüfungen decken Geraden, Steigungen, kurze Abschnittsübergänge, einzelne Punktspitzen, Links-/Rechtskurven, wechselnde S-Kurven, unterschiedliche Punktabstände und große Weltkoordinaten ab. Die Integration prüft den tatsächlichen ACC-Aufruf, stärkere vorhandene Halteanforderungen, Lane-only-Modus, F9 und absichtlich gehaltene Berechnungen.
- Beide gespeicherten griechischen Kartenwiederholungen wählen weiterhin dieselbe verbundene automatische Zahlspur ohne unbestätigten Halt. Der erste Verlauf umfasst nur drei verifizierte Abschnitte, der zweite fünf. Die Vorschau benötigt in diesen zwei lokalen Einzelmessungen etwa 4,7 beziehungsweise 4,9 ms außerhalb der Steuerung. Bei den gespeicherten Standpositionen fordert sie keine zusätzliche Bremsung an. Daraus folgt keine allgemeine Laufzeit- oder Fahrgarantie. Neue manuelle Zahlpunkte liefern diese beiden Fälle weiterhin nicht.
- Für die neue Fassung fehlen ein echter Kurvenvergleich mit Last, die erneute manuelle Übernahme im Spiel und eine vollständige Mautdurchfahrt. Hintergrundpedale nach Alt+Tab und allgemeine Erkennung temporärer Baustellenabsperrungen sind weiterhin offen. Heute ist dafür kein Spielstart erforderlich.

96 lokale Integrations- und 53 unabhängige Logikprüfungen bestehen, jeweils einschließlich des optionalen C-Protokollrahmens. Ohne diesen Rahmen sind es 95 beziehungsweise 52. Zusätzlich bestehen die 44 Aufnahmeprüfungen und drei Inventurprüfungen. Der unveränderte native Beobachter hat weiterhin 18 simulierte Host-Prüfungen; auf GitHub wird er separat neu gebaut. Die vollständige native ETS2LA-Integration wird lokal gegen die dokumentierte Installation geprüft.
