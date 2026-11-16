# Review 0.3.18 – lokal installiert, Liveprüfung ausstehend

Dieser Stand ist lokal installiert und hat noch keine neue Live-Fahrprüfung. Die Aussage ist eine Zusammenstellung konkreter Änderungen, Offline-Gegenproben und verifizierter Dateitransaktion. Sie belegt weder sämtliche Mautanlagen noch die vollständige Behebung aller bisherigen Fahrprobleme.

Der Sieben-Datei-Austausch ist abgeschlossen; alle sieben Zielhashes wurden anschließend unabhängig bestätigt. Einstellungen und Eingaben wurden nicht geändert, keine App wurde gestartet. Die geprüfte Sicherung liegt privat unter `backup-ets2la-game-before-0318-runtime-20261116-065106Z-8621a8e2`; die Installationsberichte führen `restarted=false` und `drivingValidated=false`. Die alten Sechs-Datei-Pakete und ihre Sperre bleiben unverändert.

- Runtimegate-SHA256: `D955AEF239623E42C41999BAFD6D187DABE804E4E25481E1BDAD0C5241AA9B2B`.
- Sieben-Datei-Manifest-SHA256: `1A5C6F6719B39279C34E2A66C8E1B985462A679927C15FD9F788B94A9129D282`.

## Installierter Laufzeitbuild

- Plugin-SHA256: `35542A74D0786DD459C686E0CE1D1B6EEAED8BAE59C8C1698A9235EAEB087CE5`.
- Plugin-MVID: `4c065272-6d4e-4c65-afff-cb931b24a0bb`.
- Erneuter Gesamtprüfstand: **152 Integration, 82 Kernlogik, 120 isolierte Host-/Pluginprüfungen**, jeweils ohne Fehler. Der Hostbericht einschließlich 37 echter Plugin-Commit-Prüfungen benennt genau dieses Plugin-Siegel und dieselbe Host3-Kombination. Dies ist kein Live-Fahrnachweis.

- Die echte Pluginregelung ruft jetzt die getrennte Truck-/Trailer-Vorhersage auf. Sie verwendet aktuelle native, gesondert ladungserweiterte Boxen, gemeldete Hooks/Radachsen und die finale gerichtete Route; unbekannte Straßenstücke werden nicht extrapoliert. Statische Flächen, begrenzt bewegte Modellteile und dynamische Körper werden gegen dieselben lokalen Vorhersagezellen geprüft. Die ehemalige 15-m-Gespannkugel ist kein Kollisionskorridor mehr.
- Die weite Spurplanung verwendet unveränderliche lokale Modellmaße statt der momentanen globalen Artikulationsbreite. Die lokale Vorhersage entscheidet erst im kurzen Brems-/Heckhorizont. Ein Planungsmaß ist kein Durchfahrtsrecht.
- Quellenphasen erzeugen gerichtete Translationssegmente je Körper, keine zusätzliche isotrope Breite. Native und SCS-Posezeit bleiben unverändert. Die Geometriequelle sampelt im Rendercallback mit mindestens 16 ms Abstand über QPC, verwendet präzise UTC und bewahrt den Capturebeginn auch bei langsamen Gettern.
- Das No-Slip-Modell unterstützt vorwärts nahezu ebenen Grund im langsamen Mautbereich bis 20 km/h, gemeldetes primäres SCS-`chain_type=single`, einen beobachteten angehängten Auflieger und feste gemessene Achsen. Native Präsenz ist keine vollständige Rigzählung; aus fünf von zehn möglichen Telemetrieslots wird keine universelle Anhängerabwesenheit behauptet. Ladungs-`unitCount` wird nicht als Gespannzahl verwendet.
- Bewegung, konfigurierte Achsgeometrie, momentaner Kontakt und Modellabweichung bleiben getrennt. Die Überwachung vergleicht die folgende Beobachtung mit der vorherigen Vorhersage; eine neue Vorhersage darf den Fehler nicht verstecken. Kontaktflattern hat bei unveränderter Achsgeometrie höchstens 600 ms Übergangszeit. Fehlende Physik wird nicht zu gemessener Geschwindigkeit null; unbekannte Bewegung betrifft den jeweiligen lokalen Korridor.
- Die akute Notbremsentscheidung verwendet aktuelle gemessene ungepolsterte Truckboxen statt des alten pauschal mehrfach verbreiterten Korridors. Der geprüfte Nachbarspurfall mit 46 cm Abstand bleibt frei; eine zentrale Box bei 20 km/h löst weiter Notbremsung aus. Die übrigen Modell-/Datenlücken begründen weiterhin normale lokale Halteentscheidungen.
- Reine Modellarbeit wird beim echten Plugin-Init als Hintergrundaufgabe vorbereitet; der F8-Wartezustand hält bis zur Bereitschaft. Die anfänglichen 317/324-ms-Kaltfälle und die spätere 77-ms-Verbesserung im separaten `AC495`-Zwischenbuild sind historische Belege; sie werden nicht als Messungen des heutigen Siegels umetikettiert.
- Der vorbereitete Korridor wird einmal je `Evaluate` erzeugt und für die nachfolgenden lokalen Prüfungen wiederverwendet. Seine synthetische Einzelprobe besteht 18 Assertions mit 714 Samples: 3.072 Abfragen dauerten 3,256 ms bei **0 gemessenen zusätzlichen Bytes innerhalb dieser Abfrageschleife**; das Vorbereiten selbst dauerte 0,989 ms. Das bedeutet nicht null Allokationen im gesamten Plugin oder dieselbe Laufzeit für reale FR-Modelle. Der eingefrorene Stand entfernt zusätzlich Heapallokationen in den inneren Flächen-Clipping-/SAT-Schleifen.
- Ältere Zwischenstände mit 151/81/120 Prüfungen oder MVID `c43653d7-c44d-4e47-a22c-1a4c20c3d1a2` sind nicht das heutige Siegel. Ihr Nachweis wurde durch den oben genannten erneuten Gesamtprüflauf ergänzt, nicht umetikettiert.

## Abschließende abgegrenzte Laufzeitgegenprobe

Der private Bericht `rig-runtime-static-fr-performance-final-v3.jsonl` nennt exakt den oben versiegelten `35542…`-Build. Die echte Produkt-Warmup-Aufgabe benötigte 572,76 ms bis `Ready`. Das erste reale FR-Modellprüfpaar dauerte 111,19 ms; fünf warme Paare hatten Median 82,76 ms und Maximum 120,13 ms, ein späterer Lauf etwa 47 ms. Das erste Paar allokierte 2,279 MB statt zuvor 206,763 MB und hatte keinen GC-Lauf. **0 Bytes der synthetischen Abfrageschleife bedeutet also ausdrücklich nicht 0 Bytes für diesen Produktpfad.**

Sechs isolierte Zyklen der echten Teilmethoden lagen bei 40,49–44,67 ms und erneuerten ihre jeweilige Freigabe mit rund 40–44 ms Quellenalter, unter der geprüften 150-ms-Grenze. Der Fall kombiniert ein synthetisches Gespann, echte feste Nachbargeometrie sowie das reale gespeicherte FR-Modell und dessen Route. Er umfasst weder den tatsächlichen Erfassungsprozess noch den vollständigen Ausgabepfad oder alle Mautmodelle. Daraus folgen keine konstante Framerate, globale Laufzeitgarantie oder getestete Livefahrt.

## Historischer Offline-/Beobachtungsstand v22

- SHA256: `F9E04E2CB579C62CE10B41574236E03524D96FA1DFBD3CBCB7F94E4E84ECADD7`, MVID `02a2b38a-a5f7-45ed-a387-9f14bc308a8d`.
- 141 Integrations-, 75 Kernlogik- und 120 isolierte Host-/Pluginprüfungen bestanden. Der erneute Hostlauf benennt genau diesen Build; der ältere v20-Bericht ist separat archiviert.
- [ArticulatedRigPrediction](Core/ArticulatedRigPrediction.cs) und seine unabhängigen Gegenproben bestehen 5.364 Assertions. Separate Truck-/Trailer-Hüllen umfassen das ausdrücklich modellierte, stückweise lineare Kupplungs-/No-Slip-Fahrmodell. Gerichteter Translationssupport verhindert unnötige seitliche Verbreiterung bei Geradeausfahrt. Das ist keine allgemeine physische Kollisionsfreiheit.
- [WheelAxle](Core/WheelAxle.cs) verwendet tatsächliche Radkoordinaten, feste Achsgruppen und Kupplungsabstand; Kontaktvertrauen und Liftübergänge sind getrennt von der Geometrie. [WheelTelemetryEvidence](Plugin/WheelTelemetryEvidence.cs) kopiert die gemeldete vollständige Radzahl unabhängig von späteren Provideränderungen. Lenkbare Auflieger und fehlende Daten werden nicht geraten.
- Damals wurde das neue Bewegungsmodell **nicht** von der Fahrregelung benutzt; die alte Szenenzulassung konnte Nachbarspuren durch einen ungefähr 15-m-Radius und globale Unknown-Motion-Ablehnung blockieren. Diese Aussage beschreibt v22, nicht die oben beschriebene heutige Laufzeitverdrahtung.
- Ein privates, rein lesendes Standmesswerkzeug besteht 16 unabhängige Tests. Fünf beobachtete Slots bei maximal zehn bleiben ausdrücklich eine Teilaufnahme; Ladungs-`unitCount` ist kein Gespannzähler. Keine echte Speicheraufnahme wurde durchgeführt.
- Damals wurden die sechs vorgesehenen Originaldateien unverändert nachgewiesen. Die alte Sechs-Datei-Installationssperre bleibt für diese historischen Pakete bestehen.

Die alten Siegel sind historische Gegenbelege. Der neue reversible Einbau benötigt den gesonderten Sieben-Datei-Workflow und sein finales Runtimegate; eine Livefahrt ist damit nicht nachgewiesen und wurde bis jetzt nicht durchgeführt.

## Ursachen und Änderungen

1. Der bisherige feste 6 × 3-m-Prüfkorridor konnte normale Mautdächer als unpassend behandeln. Der Kandidat benötigt stattdessen einen gesonderten Hüllennachweis: aktuelle native Modellboxen und Körperposen, Telemetrie-Identität, belegte Ladungs-/Zubehördefinitionen, aktuelle Navi-Höhe/-Lage und echte Kupplungspunkte. Eine unbekannte Hülle gibt nichts frei. Der native Leser meldet weiterhin `CargoCoverageKnown=false`; diese Eigenschaft wird nicht umetikettiert.
2. Die frühere SDK-Parkliste enthielt nur bis zu 40 Körper ohne belastbaren Capture-/Vollständigkeitsvertrag. Außerdem verwendet der geprüfte öffentliche Quellstand bei geparkten Anhängern irrtümlich den Zugmaschinenzeiger. Die ergänzende Erfassung folgt echten angepassten Anhängerbasen, führt Quellepoche, Capture-Zeit und gelesene Bereiche und erhält die ursprünglichen SDK-Formate. Der Quellbefund ist kein rückwirkender Binärbeweis für jede zuvor installierte SDK-Datei.
3. Geparkte Fahrzeuge sind von festen Mautgebäuden getrennt. Die zusätzliche Quelle beschreibt nur die gegenwärtig tatsächlich gelesenen geparkten Körper. `Complete` und `ActorsEnumerated` bedeuten vollständig gelesene bekannte Listen/Bereiche, **nicht** vollständige Welt, vollständigen aktiven Verkehr oder `SpawnFinished`. Fehlende Bewegung, Quelle oder Bereichsabdeckung können weiterhin normal stoppen.
4. Eine bereits modellseitig passende Spur kann vor der kurzen SDK-Torsicht geplant werden. `LanePlanned` ist kein `LiveBarrierBound`: Im Bremshorizont bleibt bis zur echten frischen Typ-/ID-/Welt-/Richtungsbindung ein normaler Halt am Kartenanker. Ziel und aktuelle Position werden gegen konkrete gerichtete Trace-Bereiche geprüft; eine spätere parallele oder kreuzende Routenstelle darf kein falsches „passiert“ liefern. Konflikte oder fehlende Twin-Arms ergeben keine Öffnungsfreigabe.
5. Bei ausstehendem neuem Freiraumzertifikat kann exakt belegte angewandte Topologie erhalten bleiben. `ClearanceHoldRequested` entzieht dafür Bewegung; es ist kein Ersatz für eine aktuelle geometrische Freigabe. Neue unbewiesene Routen werden nicht mit alten Zertifikaten angewandt. Die physische Ausgabe muss sämtliche offenen Haltegründe zusammenführen.
6. Geplante physische Haltebremse und Host3-Ausgabeautorität verhindern zwei getrennte Fehlerklassen: zu schwacher Halt bei langsamem Kriechen und verspätete alte Gas-/Halteberechnungen. Native Ergebnisse werden am Tickbeginn gestempelt. Hosteinlieferung und Ausgabe verlangen die richtige Sitzung und höchstens 200 ms Alter; eine Halteleihe endet beim Ursprung plus 250 ms. Das Ende eines Haltegrundes darf einen anderen nicht aufheben; stärkere Bremsungen bleiben vorrangig.
7. Manuelle Modell-Zahlzonen benötigen gerichtete Next-Kette, passende Übergänge, Weltmaßstab und Triggerbindung. Ein irreführendes Backward-Metadatum allein reicht nicht mehr für die Ablehnung; tatsächlich umgekehrte oder beschädigte Ketten bleiben abgelehnt.
8. Bewegte Modellteile werden für die gemeinsame Rotationshülle auf einen gemeinsamen Mittelpunkt bezogen. Die mathematische Korrektur und der verstärkte LOD-Gegenfall sind lokal geprüft; daraus folgt keine allgemeine Durchfahrtsfreigabe für breitere Gespanne. Eine generische `class: flare` wird nur unter dem vollständigen Render-/Schema-/Geometrienachweis als reine Lichtdarstellung behandelt, nicht aufgrund ihres Namens allein.

Relevante Quellen: [TollRouter](Plugin/TollRouter.cs), [BarrierBinding](Plugin/BarrierBinding.cs), [Hüllennachweis](Plugin/VehicleEnvelopeEvidence.cs), [Zusatzladungen](Plugin/AuxiliaryCargoEnvelope.cs), [Scene-Vertrag](Native/ParkedScene/README.md), [Hostwerkzeug](Tools/HostCompatibility).

## Zusammengehörige lokale Host3-Dateien

Die folgende Inventur gehört genau zu der bereits vorbereiteten Hostrevision `0.3.18-host-3`. Eine neue Vorbereitung erzeugt neue Modulkennungen und verlangt neue Prüfsummen und ein neues eingebettetes Plugin-Manifest.

| Datei | SHA256 | MVID |
| --- | --- | --- |
| `ETS2LA.Game.dll` | `0E463A96B4673A43419E4E89FD851985829053C247E3879ADB6160D693440551` | `bb9b2881-eeeb-45a1-aaf7-01a792ebb7ed` |
| `ETS2LA.State.dll` | `65381977D66EAEE18172D5EB63569A5AB68DE6D7E2F71611A1B3E7BD335A7F9A` | `a3ece590-1452-422c-9a95-581cdb5cb449` |
| `AdaptiveCruiseControl.dll` | `AE99F378CAA3E21474DB4C0059586902F4611B39DD303E80132E0642CD9957F0` | `cc459e7f-0fb9-41f6-8fc1-4ebfef0e4c72` |
| `LaneAssist.dll` | `5AA5211765E5D8329039D4E720144871B7DC7FACBD4041C2ACA16F8C89D43C37` | `b66f1fa2-4cde-4ae7-9797-4ee47728cec6` |

SHA256 des zugehörigen `compatibility.json`:

`7E812E68BC769C2A546E6360E6927DC1EB474F95D835E65E402365381DA046A9`

Die zusätzliche spielseitige SDK-Datei wurde separat gebaut:

- Datei: `ets2la_plugin.dll`, ETS2 1.62.x, ausschließlich die zwei ursprünglichen SCS-Exporte.
- SHA256: `BC87E1E35D87A777BB4D86DBA1F5D18F5FA0937F83FE63C22B71B0DB454FDBA9`.
- Öffentliche Ausgangsquelle: ETS2LA/plugin Commit `31f1cf1406d03f0d647a10c72887eaab5c2a748e`, fmt Commit `9ff9c695db8aeadf70eb26498a0e1cdceeeb849a`. Hashgeprüfte Archive, eigener Patchcode und exakter Buildablauf stehen unter `Native/ParkedScene`.
- Windows-Lade-/Exportprobe außerhalb des Spiels bestanden, ohne Aufruf der SDK-Initialisierung. Das ersetzt keinen Test im tatsächlichen Spielprozess.

Der historische v20-Plugin-Build `0.3.18.0` wurde mit genau der oben genannten Host3-Kombination isoliert geprüft:

- SHA256: `894B0291DCF6B851E27F35BF18D359B04D866B9FEE525CECD97F99044961FE2A`.
- MVID: `ad319b97-2c31-40eb-a391-1e2249b83536`.

Dies ist der Kandidat v20. Der frühere Build mit SHA-Präfix `410D8E90` und dessen vorbereitetes Paket sind überholt. Der vorbereitete v20-Paketmanifest-SHA256 ist `1D9FF391B3630D14568CA5B347FFAF68C98F44E880F0FCC3A3D8DA6093752171`; das Paket ist nicht installiert und bleibt wegen des oben genannten funktionalen Blockers gesperrt.

Das historische Paket hatte sechs Austauschdateien. Der heutige Laufzeitkandidat hat **sieben**: Plugin, vier Hostdateien, Szenen-SDK und die aktualisierte `MautGeometry.dll`. Der Beobachter hat SHA256 `A91E2C5354D03DA0E07E79E9AB732443EE277B995297293FB65D93D3A96E5262`, keine verwaltete MVID, exakt zwei ursprüngliche TruckersMP-Exporte und unverändertes 256-Byte-Protokoll v1. 31 isolierte Fake-Host-Assertions bestehen. Ein einzelner Plugin-Austausch ist keine unterstützte Kombination.

## Historische und abgegrenzte Einzelproben

| Probe | Beleg und Grenze |
| --- | --- |
| Native Park-Collector | 33 unabhängige Assertions: reale Win64-Klassen, Anhängerbasis +0x38, getrennte Körperposen, Zyklen, Grenzen, Bewegung und Überlauf. Keine Spielinitialisierung. |
| Native Producer-Vertrag | 18 Assertions: Capture-Zeit, Pause/Wiederaufnahme, Timerreset, Frame-Pose, fehlende Welt, Bereichs-/Actorfehler und verlorene Writer-Leihe. Kein Spiel/MMF. |
| Sauberer SDK-CI-Einstieg | Dieselben 51 nativen Prüfungen gegen ein frisch entpacktes, hashverifiziertes ungepatchtes SDK-Archiv bestanden. Keine zusätzliche Behauptung einer Spielinitialisierung oder Fahrprüfung. |
| Historische v20-Integrations- und Kernlogikprüfungen | 138 Integrationsprüfungen und 73 Kernlogikprüfungen bestanden gegen v20, nicht den heutigen Laufzeitbuild. Synthetische Zustände und gespeicherte Gegenfälle ersetzen keine reale Anfahrt oder vollständige Wahrnehmungsprüfung. |
| Host3 ohne Pluginprobe | 83 isolierte native Prüfungen. Reale Tick-/Publisher-/Ausgabewege mit injizierten Constraint-Grenzen und Testsenken; keine Ausgabe-Threads oder Live-Speicher. |
| Echte Plugin-Commit-Probe | 37 weitere Assertions der echten `CommitPlannedHolds`-/`ClearSceneHold`-Methoden, zusammen mit Host3 120 Prüfungen je protokolliertem Plugin-Siegel; erneut gegen den oben versiegelten Laufzeitbuild ausgeführt. Branch/Scene-Maximum, Clear, beide Pedalwege, stärkere Fehler-/native Bremse, F9, 230-ms-Altberechnung und einzeln ungültige Produktzustände. Die Floor-Eingänge sind kontrollierte Testwerte. |
| Frühe Spur-/Bindungsfälle | Synthetische Fälle mit null Torproben, Modell-/Nutzer-Zahlarten, falschem Typ/Ort/Nummer, Twin-Arms, Gegenrichtung, selbstkreuzender Route, Profil-Pending und neuen Wrapperobjekten geprüft. |
| Manuelle Zielzone | Gerichtete gültige Kette trotz Backward-Metadatum akzeptiert; echte Umkehrung, defekte Next-Kette und unpassende Naht abgelehnt. |
| Bewegte Modellhülle / Lichtklasse | 237 lokale Mover-Prüfungen und ein gezielt verstärkter LOD-Gegenfall bestanden. Gemeinsamer Mittelpunkt und vollständiger Vertrag für generische `class: flare`; kein Beleg freier Nachbarspuren oder breiterer Gespanne. |

Die archivierte v20-Pluginprobe nennt exakt SHA256 `894B0291DCF6B851E27F35BF18D359B04D866B9FEE525CECD97F99044961FE2A` und MVID `ad319b97-2c31-40eb-a391-1e2249b83536`. Ihre 120 Gesamtprüfungen waren nicht auf einen späteren Build übertragbar. Für v22 wurde deshalb ein neuer Lauf mit `MAUT_PLUGIN_CANDIDATE` und seinem eigenen SHA256/MVID ausgeführt. Die frühere v13-Probe ist ebenfalls nur ein historischer Zwischenbeleg.

Die historischen Einzelbelege sind nicht mit dem oben gesondert angegebenen aktuellen Gesamtprüfstand gleichzusetzen. Beide ersetzen keinen Fahrnachweis. Die nativen SDK-Standalone-Suiten ergeben zusammen 51 Prüfungen (33 Collector + 18 Producer), getrennt von den 31 Beobachterprüfungen. Private Berichte und Kartenwiederholungen liegen außerhalb des Repositorys; Herstellerbibliotheken und extrahierte Spieldaten werden nicht mitverteilt.

## Frankreich: was der vorhandene Fall aussagt

Der gespeicherte späte Startpunkt hatte nur etwa **14,44 m** auf dem letzten Zufahrtsstraßenstück übrig. Dort kann der notwendige Spurwechsel nicht dadurch sicher werden, dass niedrigere Dächer erkannt werden. Die neue frühe Planung soll ihn rechtzeitig auf einem noch geeigneten Abschnitt vorbereiten; strikte Anschluss-/Spurwechselprüfungen bleiben erhalten.

Eine frühere virtuelle Position auf demselben aufgezeichneten Straßenstück mit ungefähr **67 m Rest** ist eine gesonderte Offline-Hypothese. Sie beweist weder eine tatsächlich früher ausgelöste Planung noch die Passage bei Autobahntempo. Eine weitere gezielte Offline-Gegenprobe mit **null SDK-Toren**, etwa **53,82 m Straßenrest / 198 m vor dem Tor** und gespeichertem Niedrigtempo wählte im v16-Zwischenstand die statisch passende automatische Spur 10 (vierte von rechts) ohne Routenfehler. Das belegt frühe Kartenplanung, ausdrücklich keine Live-Schrankenzuordnung, Öffnung oder vollständige Durchfahrt. Die reale Anfahrt bleibt offen. Fehlende vorherige Straßenstücke werden dafür nicht extrapoliert. Aus einer statisch passenden Spur folgt keine aktuelle Freigabe durch stehende Fahrzeuge oder geschlossene Schranken.

Die v20-Breitengegenprobe muss getrennt gelesen werden: 3,4 m Breite passt statisch zur automatischen Spur 10. Bei 3,5 / 3,6 / 4,0 m bleibt ein geplanter Halt aufgrund fester Modellgeometrie an den Kurven 51/52/69/71. Das ist in dieser Gegenprobe kein Mover-Mittelpunktfehler. Eine frühere kurzzeitig positive Zwischenmeldung wurde korrigiert; eine allgemeine Behebung der Frankreich-Durchfahrt wird nicht behauptet.

## Fehlende Live-Nachweise und Einschränkungen

Vor einer Fahrfreigabe fehlen mindestens:

1. Gemeinsamer Start der endgültigen sieben Dateien im realen TruckersMP-Client; gültige PID/Sitzung/Uhr, belegte aktuelle Bereichsabdeckung, gelieferte Objektzahlen und deren Ausfallquote sowie ein plausibles Gespann einschließlich echter Hooks im Stand. Die 16-ms-Korrelation bei niedriger realer Bildrate ist noch nicht live bestätigt.
2. Aktuelle Hüllen-/Ladungs-/Navi-Zuordnung ohne angenommene Nutzermaße; native `CargoCoverageKnown=false` bleibt sichtbar. Für unbekannte Haupt-Ladungsmodelle, Mehrfachauflieger und ungeklärte Kopplungen keine automatische Freigabe.
3. Stillstand vor geschlossen bleibendem Tor, passende Zahlzone, Twin-Arms, Datenverlust sowie F9-/Bremsübernahme bei normalem Luftdruck und derselben Beladung.
4. Kontrollierte Anfahrt, frühe physisch erreichbare Lkw-Spur, rechtzeitige Bremsung, tatsächliche Abrechnung/Öffnung, verzögerte Freigabe und zusammenhängende Ausfahrt ohne Lenkverlust.

Die komplette zukünftige Schleppkurve, allgemeine Baustellen-/Verkehrsschildvermeidung und Hintergrundpedale nach Alt+Tab in TruckersMP bleiben offen. Unbekannte Bewegung oder unvollständige lokale Szene kann weiterhin einen vorsichtigen Halt verursachen. Die Quelle deckt keine zukünftigen Spawns oder den gesamten aktiven Verkehr ab. Ein Ausfall der Anwendung oder des SDK kann keine verlässliche Bremsausgabe garantieren.

## Austausch und Prüfung

Das neue lokale Sieben-Datei-Schema verlangt ein separat gehashtes Runtimegate für exakt Plugin-SHA/MVID, Host3, SDK und Beobachter sowie die ausdrückliche Disposition des unveränderten alten Sperrmarkers. Gate und Manifest werden aus genau den gehashten Bytes gelesen. Alle sieben Originale werden gesichert und geprüft und alle sieben Ersatzdateien vor dem ersten Wechsel staged. Gateprüfung erfolgt vor jedem Staging und Commit, Prozessprüfung unmittelbar vor jedem Commit-/Rollback-Move. Jeder einzelne Wechsel ist atomisch; sieben Dateien sind kein gemeinsamer atomischer Dateisystemwechsel. Fehler führen bei geschlossenen Anwendungen zum geprüften Rücksetzen oder zu protokollierter offener Wiederherstellung. Gate und sein Hash liegen vollständig im Journal/Backup. Der Installer startet keine Apps, Assistenz oder Fahrt. Prozessprüfungen bleiben Best Effort; Apps müssen geschlossen bleiben.

Die Vorbereitungsordner enthalten auch instrumentierte `isolated-replay-*`-Kopien mit abweichenden DLL-Prüfsummen. Diese Kopien und der gesamte Vorbereitungsbaum dürfen niemals als Installationspaket verwendet werden. Nur die sieben versiegelten Produktionspfade gehören in das Austauschpaket.

Das finale Paketgate und die verifizierte Transaktion sind oben festgehalten. Beim nächsten Start zunächst im Stand Aktivierung sowie manuelle Übernahme mit Controller-Bremse/F9 prüfen; anschließend folgt die kontrollierte Mautdurchfahrt. Diese spätere Validierung bleibt vom abgeschlossenen reversiblen Dateiaustausch getrennt. Eine Installation behauptet keine getestete Fahrt.
