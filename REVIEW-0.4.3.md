# 0.4.3 – Kartenampeln und wiederholte Physikframes

Stand: 17. November 2026. In Vorbereitung; noch nicht installiert oder im Spiel geprüft.

## Getrennte Vorfälle

Der Halt gegen 18:17 Uhr wurde von der Ampelregelung angefordert, nicht von einer verriegelten Gassperre. Der Nutzer meldete freie Straße ohne Ampel. Die gespeicherte Entscheidung enthielt keinen bestätigten roten Zustand, sondern einen nicht zugeordneten Ampelpunkt und einen Halteabstand von rund 0,30 m.

Die anschließende Archivprüfung fand das konkrete Straßenmodell und dieselbe Signalkoordinate. Das Profil beschreibt tatsächlich Ampelphasen. Bei dieser **Karteninstanz** ist aber das Bit zum Erzeugen der Ampelmodelle ausgeschaltet. Das bisherige Verfahren wertete allein den Profilmarker als Ampel und behandelte einen fehlenden oder nicht bestätigten Freigabezustand als Haltegrund. Der dokumentierte Beginn der betroffenen Navigationskurve reproduziert zusammen mit dem bestehenden 5-m-Abstand den aufgezeichneten Haltepunkt. Das ist eine statische Rekonstruktion, kein nachträglicher Nachweis aller Runtime-Signale.

Davon getrennt forderte der Regler vor dem Fehlerhalt um 18:11 Uhr Betriebsbremse an, während sowohl empfangene als auch effektive Bremse null blieben. Eine Ursache des Eingabeausfalls, insbesondere Fensterfokus, ist mit dieser Aufnahme nicht bewiesen. Dieses Update erklärt den Ampelhalt nicht durch diesen früheren Transportfehler und behauptet keine Behebung des Hintergrundbetriebs.

## Instanzprüfung

Die Regelung liest das Spawnflag aus der tatsächlich geplanten Karteninstanz. Die verwendete TruckLib-Version enthält das Flag, bietet aber noch keine öffentliche Eigenschaft dafür; der kleine Adapter ist deshalb an die geprüfte Bibliotheksprüfsumme gebunden. Fehlende Instanz, unbekannte Version, fehlendes Feld oder Lesefehler ergeben **unbekannt**, niemals automatisch ausgeschaltet.

Nur bei nachgewiesen ausgeschaltetem Flag **und keinem passenden Runtime-Actor** entfällt der aus diesem Marker abgeleitete Ampelhalt. Ein passender beobachteter Ampelzustand hat Vorrang. Eingeschaltete oder unbekannte Instanzen ohne eindeutigen Freigabezustand bleiben haltpflichtig. Ein beliebiger als Schranke typisierter Actor ersetzt ebenfalls keinen positiven Ampelbeleg; die kanonisch zugeordnete Mautschranke bleibt Sache der Mautregelung.

Die bisherige Aufnahme enthielt nur rohe Schranken und einen zusammengefassten Ampelgrund. Sie beweist deshalb nicht, ob damals wirklich kein passender Ampel-Actor vorhanden war. Die Offlinegegenprobe verwendet leere, rote und grüne Runtime-Zustände als ausdrücklich getrennte Szenarien. Eine spätere Aufnahme ist für den tatsächlichen Zustand an dieser Stelle nötig.

Die Diagnose speichert nun Instanz, Profil, Rohrolle, Spawnflag, Anker, Kurvenbeginn und passende Runtime-Zustände. Die unabhängige Aufnahme bewahrt zusätzlich alle Signaltypen und Rohzustände getrennt von den bisherigen Schrankendaten. Dadurch lässt sich ein weiterer Halt mit seiner tatsächlichen Bindung untersuchen.

Das Flag betrifft Ampelmodelle, nicht sämtliche KI-Regeln. Grundlage: [TruckLib Prefab](https://github.com/sk-zk/TruckLib/blob/master/TruckLib/ScsMap/Prefab.cs), [isolierter Vergleich des Flags im Spiel](https://github.com/sk-zk/TruckLib/pull/22). Profilmarker können unterschiedliche KI-Aufgaben haben; daraus allein folgt weder eine sichtbare Ampel noch eine Freigabe. [SCS-Dokumentation](https://modding.scssoft.com/wiki/Documentation/Tools/SCS_Blender_Tools/Locators/Prefab_Locators#Traffic_Semaphore)

## Wiederholte Telemetrie

Die bisherige Ableitung verwarf die gemessene Beschleunigung bei jedem wiederholt gelesenen Simulationsframe. Der Regler konnte darauf mit einer zusätzlichen Verzögerungsgrenze reagieren, obwohl der unmittelbar davor bestätigte Messwert noch aktuell war.

Eine aus zwei fortschreitenden Physikframes gebildete Ableitung darf bei identischer Wiederholung innerhalb ihrer ursprünglichen 200-ms-Frist weiterverwendet werden. Wiederholungen erneuern diese Frist nicht. Widersprüchliche Werte, Pause, inaktive Quelle, Zeitrücklauf und ungültige oder unplausible Bewegung verwerfen den Beleg. Ohne ausreichenden frischen Beleg bleibt die vorsichtige Ersatzregelung bestehen. Vor positiver Gasausgabe wird die ursprüngliche Frist erneut geprüft und in die vorhandene Ausgabefrist einbezogen.

## Offene Grenzen

- Keine allgemeine Profilauflösung oder vollständige Abdeckung aller Ampeltypen und Kartenregionen.
- Keine nachgewiesene Lösung für ausbleibende Spieleingaben nach Alt+Tab in TruckersMP.
- Automatisches Bezahlen an manuellen Mautspuren ist hier noch nicht enthalten. Dafür fehlt weiterhin ein ausreichend abgesicherter Interaktionspfad; ein ungebundener Enter- oder Aktivieren-Impuls ist keine belastbare Umsetzung.
- Erst Offlineprüfungen und kritischer Gegenreview, danach Installation bei geschlossenem ETS2LA und eine getrennte Fahrprüfung.

## Abgeschlossene Offlineprüfung

210 Integrationsgruppen, 98 reine Logikgruppen, 251 isolierte Hostprüfungen und 70 Aufnahmeprüfungen bestehen. Die unabhängige kompilierte Signalprüfung umfasst 26 Assertions; die reine Dynamikprüfung 41. Diese Zahlen überschneiden sich teilweise und sind keine Summe unabhängiger Fahrversuche.

Die zusätzliche Archivgegenprobe verwendet das tatsächliche Straßenmodell und dessen Instanzflag, einen ausdrücklich synthetischen lokalen Trace sowie sechs explizite Runtime-Szenarien. Kein Actor bei ausgeschaltetem Flag erzeugt keinen Halt; eigenes Rot bleibt haltwirksam; eigenes Grün darf freigeben; fremdes Grün hebt eigenes Rot oder fehlenden eigenen Zustand nicht auf. Ein privater Vergleich mit ausschließlich eingeschaltetem Spawnflag behält den unbekannten Halt. Der Variantenname allein wird nicht als Beleg verwendet; eine nahe Instanz derselben Variante besitzt eingeschaltete Ampeln.

Die Hostprüfung führt echte Telemetrie-Callbacks durch den Plugin-Regler zu isolierten Legacy- und modernen Ausgabesenken. Identische Physikframes behalten innerhalb der ursprünglichen Frist Gas ohne neue Bremsgrenze. Nach deren Ablauf entzieht der Host Gas auch dann, wenn Verkehrsframes inzwischen erneuert wurden. Die bestehenden Park-, Übernahme-, Fehler- und kanonischen Mauttests bleiben grün.

Geprüfter Plugin-SHA256: `3ED2DC65898B28F2168379697AC9932A6287B6FFBF936B1E74E650878605102F`, MVID `a871042f-575a-446a-aa21-ae25bd7d2cee`. Das Hostpaket bleibt unverändert. Paket und reversibler Ein-Datei-Installer sind gegengeprüft. Der abschließende Prozesscheck fand noch laufendes ETS2LA und stoppte **vor jeder Installation**; der Austausch wartet auf dessen Ende. Keine Fahrprüfung oder automatische Zahlung behauptet.
