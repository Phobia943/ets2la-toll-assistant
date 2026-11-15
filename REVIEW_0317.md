# Kritischer Review: Entwurf 0.3.17

Stand: 15. November 2026. **Nicht freigegeben, nicht installiert.** Die installierte Fassung bleibt 0.3.16. Ein erfolgreicher Build ist keine Bestätigung der Fahrfunktion.

## Arbeitsweise

Auf Wunsch des Nutzers wird jeder weitere Zwischenstand mit einem unabhängigen kritischen Review-Agenten besprochen. Der Autor erklärt Änderung, angenommene Ursache und Nachweise. Der Reviewer sucht Gegenbeispiele, Regressionen und fehlende Belege. Einwände werden durch Korrektur und erneute Prüfung beantwortet; nicht allein durch eine Beschreibung der Absicht.

Die Freigabe unterscheidet zwischen lokal geprüfter Logik, Wiederholung gespeicherter Vorfälle und tatsächlichem Fahrnachweis. Ein grüner Teiltest belegt keine vollständige Behebung aller bisherigen Probleme. Offene Punkte bleiben offen. Während einer Fahrt werden keine laufenden Module, Einstellungen oder Dateien ausgetauscht.

## Erste Reviewrunde: keine Freigabe

1. **Französische Spurwahl bleibt ungelöst.** Das exakte Modell `dlc_fr_10` wird bei der Prüfung wegen des Hookups `car.parked.physics.0.25` vollständig verworfen. Der Hookup ist laut lokaler Definition ein `parked_vehicle` mit `low_poly_only: false`, möglichem Fahrzeugtag `car` und Tages-/Nachtwahrscheinlichkeit 0.25. Er ist deshalb kein belegbar geometriefreies Objekt. Eine Namens-Whitelist oder eine pauschal angenommene kleine Ausdehnung wäre keine Lösung. Zahlungserkennung und räumliche Freigabe müssen getrennt betrachtet werden. Eine belastbare Auswertung müsste alle möglichen Objekte, Ausdehnungen und Transformationen abdecken oder einen anderen nachgewiesenen Hindernisnachweis verwenden.
2. **Geplanter Halt ist nicht gleich Notfall.** Die neue Trennung zwischen weichem Halteziel und geschützter Grenze ist plausibel, aber noch unzureichend geprüft. Die reine Logiksuite hat derzeit einen Fehler: Der alte Fall bei 2 m/s und −1.1 m zum weichen Ziel entspricht unter der neuen Semantik keinem unvermeidbaren Grenzübertritt. Statt den Test einfach zu entfernen, sind die neue harte Grenze, verzögerte Bremswirkung und die aufgezeichneten langsamen Fehlbremsungen zu prüfen. Der Haltezustand muss außerdem eindeutig an dieselbe Haltstelle gebunden sein.
3. **Direktes Wunschtempo 100 ist nicht freigabefähig.** Der Entwurf schreibt beim Start direkt in `DesiredSpeed` und umgeht damit unter anderem die native Kappung durch `AssistanceSettings.MaximumSpeed`. Wunschtempo, wirksames Ziel, F8-Wiederaufnahme und F10-SET brauchen getrennte Regeln und Regressionstests. Vor Maut, Kurven oder ungeprüfter Vorschau darf daraus keine unbelegte Beschleunigungsanhebung entstehen.
4. **Kurvenvorschau kann trotz fortlaufender Arbeit fehlen.** Jeder neu erzeugte `RouteTrace` invalidiert das alte Ergebnis. Wenn neue Routen schneller eintreffen als der Worker fertig wird, werden fertige Ergebnisse fortlaufend verworfen. Referenzgleichheit ist keine hinreichende Strategie. Wiederverwendung verlangt belegte Übereinstimmung des gerichteten Korridors, der Fahrspur, Ebene, Verbinder und benötigten Vorausstrecke. Eine erst weiter vorne abzweigende Route darf nicht versehentlich die alte Vorschau übernehmen.
5. **Einstellungen dürfen nicht zurückgesetzt werden.** Die Oberfläche rekonstruiert `TollOptions` bisher aus einer Teilmenge der Felder. Beim Speichern müssen unsichtbare und neu ergänzte Optionen erhalten bleiben. Ein Roundtrip mit deaktivierter schnellerer Fahrt und verändertem Wunschtempo ist erforderlich.

## Problems- und Beweismatrix

| Fehlerklasse | Vorhandener Befund | Noch erforderlicher Nachweis |
| --- | --- | --- |
| Unnötiger Notfall beim geplanten Mauthalt | Zwei französische Aufnahmen enden bei etwa 2.5 km/h mit einer unzureichenden Restwegmeldung; weiches Halteziel wird als Notfall behandelt | Harte/weiche Grenze, verzögerte Regelung, Halte-Latch und Wechsel der Haltstelle reproduzieren |
| Keine automatische französische Lkw-Spur | Modellablehnung am geparkten Fahrzeug konkret reproduziert; Gateposition stimmt im gespeicherten Routenpräfix auf etwa 3 mm mit SDK überein | Vollständige Kindobjekt-/Hindernisevidenz, benachbarte erlaubte/gesperrte Spuren und tatsächliche Durchfahrt |
| Anschluss nach der Schranke / normaler Straße | Früherer Fall einer 4.5-m-Verbindungslücke gespeichert | Gerichteten Anschluss nachweisen; keine pauschale Vergrößerung der erlaubten Lücke |
| Kurve / fehlende Lenkausgabe | Schutz- und Kurventests existieren; neuer Review findet Vorschau-Lücken bei häufigen Routenupdates | Kontinuierliche Vorschau bei langsamem Worker, Abzweig, Fahrspur-/Ebenenwechsel, erschöpfter Reichweite und manueller Übernahme |
| Start während der Fahrt | Früher vom Nutzer mit 0.3.3 bestätigt; spätere Startprüfungen vorhanden | Regression mit neuer Wunschtempo- und Vorschau-Logik, abgebrochener Startprobe und F10 |
| Controller-Bremse / F9 | Mehrfach praktisch bestätigt, zuletzt nicht für den neuen Entwurf | Keine späte Wiederaktivierung nach Übernahme; Prüfung der neuen Kombination |
| Luftdruck / Hintergrundfahrt in TruckersMP | Standard-Pedalausgabe bewährte sich im begrenzten Vergleich; nach Alt+Tab wurde wirkungslose Bremsausgabe aufgezeichnet | Weiterhin ungelöst; nicht durch statische Mautmodellprüfung abgedeckt |
| Baustellen / Verkehrsschilder | Statische Mautgeometrie ist kein allgemeiner Hindernisdetektor | Eigene belegte Wahrnehmung/Planung und reproduzierbare Vorfälle; weiterhin offen |

## Grenzen der aktuellen Wiederholung

Die französische Kartenwiederholung rekonstruiert nur das exakte vordere Präfix aus drei gespeicherten Elementen. Ein späteres Prefab konnte nicht exakt rekonstruiert werden. Das Ergebnis belegt die frühe Modellablehnung, aber keine vollständige Strecke, keinen Bremsweg und keine gefahrene Durchfahrt. Die neue Diagnose verändert die Freigabe unbekannter Geometrie nicht.

Nächste Iteration: zuerst die Modellablehnung und die kontinuierliche Kurvenvorschau ursächlich bearbeiten, danach den vollständigen Entwurf erneut kritisch prüfen. Keine Aussage „alle Probleme gelöst“, solange die obigen offenen Nachweise fehlen.

## Fortschritt nach den weiteren Reviewrunden

Die oben beschriebenen ersten Einwände sind ein historischer Reviewstand. Inzwischen sind der keyed Hold mit harter/weicher Grenze und verzögerter Simulation, die kontinuierliche gerichtete Kurvenvorschau samt echter Reichweitenprüfung, begrenztes Wunschtempo und der tatsächliche UI-Roundtrip umgesetzt und lokal geprüft.

Die nativen Gegenproben bestätigten zusätzlich zwei Fehler der installierten Komponenten: ACC verlässt bei sehr langsamer Fahrt den Tick vor der neuen Pedalveröffentlichung; GameOutput kann alte Achsenwerte behalten, wenn andere Kanäle weiterlaufen. Lokale Kopien korrigieren beide Pfade und schützen native Befehle, Start- und Fehler-Commits mit Sitzungskennungen. Die periodische Überwachung übernimmt keinen alten Ausführungskontext. F9 während einer blockierten Startinstallation oder Wunschtempo-Abfrage darf keinen alten Enable-/Tempo-Commit zulassen.

Der neue Rollmodus neutralisiert im nativen Tick bei freier geprüfter Strecke die kleine Wunschtempo-Korrektur 85→80. Tests halten bei derselben Tempodifferenz die Bremsung für Fahrzeug, Kurve, Semaphore und Maut aufrecht. Fehlende/alte Eingangsdaten, fremde native Route/Container und abgefangene Kurvenprobenfehler sperren. Eine Gefälleverriegelung gilt sitzungsweit einschließlich Threadwechsel. Die isolierten Tests injizieren einige Constraint-Eingänge; sie ersetzen keine reale Verkehrs- oder Fahrprüfung.

Vier konkrete Dateihashes, neue Modulkennungen und ein gemeinsamer Revisionsmarker sind im Plugin eingebettet. Unvollständig oder anders geladene Pakete werden vor dem Start zurückgewiesen. Siehe Tools/HostCompatibility/README.md. Noch nicht installiert oder im Fahrbetrieb freigegeben.

Weiterhin offen bleiben die vollständige Kindobjektgeometrie des französischen Modells, der gespeicherte 4,5-m-Anschluss, wirksame Hintergrundpedale unter TruckersMP und allgemeine Baustellen-/Hinderniserkennung. Die Geometriesperren wurden nicht gelockert. Eine allgemeine Freigabe „alle Probleme behoben“ liegt nicht vor.

Abschließender kritischer Review V11: keine neuen konkreten P1/P2-Befunde im letzten Diff. Vier Pakethashes sowie 104 Integrations-, 57 Logik- und 51 isolierte native Prüfungen unabhängig bestätigt. Als separat vorbereiteter lokaler Kandidat vertretbar; keine allgemeine Fahrfreigabe. Noch nicht installiert. Die offenen Punkte und fehlenden praktischen Nachweise bleiben ausdrücklich bestehen.
