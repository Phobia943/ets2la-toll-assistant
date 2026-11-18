# Prüfung der nächsten Fassung 0.4.8

Stand: 18. November 2026. v2 ist um 12:23 MEZ nach bestätigtem Schließen beider Anwendungen mit Sicherung installiert. Alle 15 installierten Dateien sind geprüft. Reale Abnahme und Gesamtauftrag bleiben offen; die letzte bereits ausgewertete Fahrt gehörte noch zu 0.4.7.

## Abschluss der lokalen Codeprüfung

Unabhängiger Gegenreview der Quellen, Prozessgrenze, Routenbindung, Tickdiagnose und optionalen nativen Messung abgeschlossen. 254 vollständige Integrationsfälle gegen die Originalhost-Fixture, 104 Kernlogikfälle und 316 isolierte Prüfungen gegen den tatsächlich gepinnten Host bestehen. Dazu kommen 23 native Messbausteinfälle, 12 unabhängige Decoderfälle und 13 bestehende Recorderfälle. Diese Zahlen betreffen unterschiedliche, teils überlappende Prüfebenen und werden nicht zu einem angeblichen Fahrtest addiert.

Ein erster vollständiger Integrationslauf wurde irrtümlich gegen die angepasste laufende Hostbibliothek gestartet: 252 Fälle bestanden, zwei Originalhost-Gegenfälle scheiterten. Der unveränderte vollständige Test wurde anschließend mit der dafür vorgesehenen, hashgeprüften Originalhost-Fixture wiederholt und bestand. Der tatsächliche angepasste Host wurde getrennt im versiegelten Host-Replay mit allen 316 Prüfungen kontrolliert.

Plugin-Kandidat v2: `C9213A67A41F4D174B2921EFF4C35DE05A2A2274208450B0F4BE38A066508340`.
Optionaler SDK-Messkandidat: `D7C849455AB17BC575D8A3DB87FE0FC34DAD1610043DCBD1ABF52A61242159E0`.
Das Hilfsprogramm wird vor dem Plugin gebaut und sein vollständiges Manifest eingebettet. Der abschließende unabhängige Paketreview sowie zwölf getrennte Installations-/Rollback-Simulationen bestehen. Der bewachte Installer hat bei geschlossenen Anwendungen ausschließlich die 15 vorgesehenen Dateien installiert und ihre Sicherungen geprüft; Hostbibliotheken und InputBridge blieben unverändert. Der Installer startet keine Anwendung.

Der abschließende Einbaureview fand einen realen Ladefehler im ersten, nie installierten Paket: Der gepinnte Host kopiert nur die Plugin-DLL in einen temporären Ordner. Eine Suche relativ zur geladenen DLL hätte dort kein Hilfsprogramm gefunden. v2 verwendet deshalb den festen geprüften Installationspfad `AppContext.BaseDirectory/Plugins/StaticRoadWorker`. Das ist der Vertrag dieses Pakets, keine allgemeine Ermittlung beliebiger Host-Pluginpfade. Die neue Regression lädt tatsächlich eine einzelne Assembly-Kopie in einem eigenen Ladekontext und verändert zusätzlich das Arbeitsverzeichnis. Sie scheiterte vor der Korrektur und besteht danach. Alle sieben Worker-Testgruppen und erneut alle 316 tatsächlichen Hostprüfungen bestehen mit dem v2-Build. Das frühere versiegelte Paket bleibt als verworfen erhalten.

## Statische Straßenhindernisse

Die bislang verwendete Kartenansicht verwirft gewöhnliche Straßenmodelle. Eine leere Modellliste konnte deshalb keine Abwesenheit von Baustellenobjekten beweisen. Die neue Quelle liest die tatsächlichen Hilfssektoren und ausgewählte aktive `coll`-Modellteile. Visualmodelle, bloße Begrenzungsboxen, unbekannte Varianten und bedingte Instanzen werden nicht zu Hindernissen umgedeutet. Layer werden vollständig gelesen, bevor ihre Modelle veröffentlicht werden. Unvollständige Layerdaten und vorübergehende Lesefehler erhalten keine Freigabe und keinen erfundenen Halt.

Die neue Prüfung folgt der gewählten gerichteten Standardspur. Ein positiver Dreieckstreffer kann eine normale frühzeitige Bremsgrenze erzeugen; er wird nicht als akute Notbremsgefahr ausgegeben. Der Standardkorridor ist kein vollständiges Gespann-/Ladungsmodell. Fahrbahnflächen werden auf ihrer lokalen Steigung ausgeklammert. Bereits überholte Treffer sind ungültig.

Ein kritischer Gegenreview fand zuerst erhebliche Allokationen bei kalten Archiv-/Registerzugriffen. Ein eigener Thread hätte die Speicherbereinigung des Fahrprozesses nicht getrennt. Daher laufen Archiv-, Modell-, BVH- und Kollisionsarbeit vollständig in einem eigenen Hilfsprozess. Der Fahrprozess übernimmt ausschließlich kleine Ergebnisse mit ursprünglicher Zeit, Quelle, Route und manueller Übernahmegeneration. Mehr als 200 ms alte Antworten werden verworfen, nicht neu datiert. Ein im Plugin eingebettetes Manifest bindet das vollständige Hilfsprogramm. Kein Ersatzpfad lädt Modelle im Fahrprozess.

Ein zweiter Gegenfall betraf fortlaufend neu veröffentlichte identische Routen: bloße Referenzgleichheit hätte praktisch alle asynchronen Antworten verworfen. Die Annahme verlangt deshalb denselben nachgewiesenen gerichteten Verlauf; ursprüngliche Route, Antwortzeit und Wegkoordinaten bleiben erhalten. Tatsächliche Spurwechsel, neue Quelle und manuelle Übernahme widerrufen alte Ergebnisse.

Die historische Baustellenprobe erkennt weiterhin den Bagger und zwei Materialhaufen mit tatsächlichen Kollisionsflächen. Das belegt eine Überlappung mit der damaligen geplanten Standardlinie, nicht die vollständige Ursache des historischen Unfalls. Die getrennte Prozessprobe mit fortlaufend neuen Route-Objekten ergab 191 positive Antworten bei 254 Abfragen; die Abfrage im Fahrprozess benötigte dabei im 95. Perzentil 0,354 ms, höchstens 1,866 ms. Das ist eine lokale Probe und keine universelle Laufzeitgarantie.

## Neue Live-Befunde aus 0.4.7

- 11:31 MEZ: Der native Lenkregler meldete eine zu hohe Lenkforderung und veröffentlichte anschließend keinen neuen Lenkbefehl. Der gespeicherte Betrag lag ungefähr bei 1,123. Die Route enthielt mehrere sehr enge Bogenstücke; ein einzelner fehlerhafter Übergang ist nicht belegt. Die bestehende Ausgabewache reagierte. Weder eine beliebige Radiusgrenze noch eine Verlängerung ihrer Frist ist damit gerechtfertigt.
- 11:35 MEZ: Der Vorfall liegt vor Beginn der leichten Laufzeitaufnahme. Seine vollständige Fehlerdatei wurde später überschrieben; die verbleibenden Statusdaten reichen nicht zur eindeutigen Ursachenbehauptung.
- 11:40 MEZ: Nach F8 gab es mehrere erfolgreiche eigene Pedalausgaben. Erst danach fehlte ein neuer Abschluss. Ein fehlender allererster Tick ist somit nicht die Ursache. Der dauerhafte Bericht enthielt bisher nicht den vollständigen Auslöser der Pedalwache. Die neue Diagnose bewahrt ihn zusammen mit den ursprünglichen Tickphasen und Zeitpunkten; sie verlängert keine Ausgabeautorität.

Die leichte Laufzeitaufnahme ab 11:35:55 MEZ zeigt keinen neu erfassten 150-ms-GC-Stopp. Der Decoder meldet jedoch einen übergelaufenen Verlustzähler; Vollständigkeit wird daher ausdrücklich nicht behauptet. Die ältere Aufnahme mit Stack-Sampling enthielt viele diagnostisch erzeugte `SuspendOther`-Ereignisse, die keine Speicherbereinigungen beweisen.

Der zusätzliche unabhängige Stellgrenzenreview rekonstruierte 45 gespeicherte Positionen mit den tatsächlichen gepinnten Prefab-/Interpolationsmethoden. Der Abstand am Fehlerpunkt stimmt auf Millimeter mit der Aufnahme überein. Die vorhandene Kurvenverfeinerung reduziert den maximalen proportionalen Anteil zwar von etwa 1,280 auf 1,208, überschreitet die native Grenze aber teilweise früher. Sie ist damit keine ausreichende Behebung. Ein eigener gerader Gegenfall mit 46 Grad Fahrzeugfehlstellung erreicht bereits etwa 1,012: Eine Forderung über 1 beweist allein keine fehlerhafte Route. Umgekehrt macht eine bloße Stellwertbegrenzung keine physisch zu enge Kurve befahrbar.

Historische Filter- und Integratorzustände fehlen. Der Vergleich ist deshalb keine vollständige Regler-/Physikwiederholung. Für die nächste Untersuchung fehlen zusammenhängende native Entscheidungsdaten mit Originalzeit und Steuergeneration, Zielpunkten, rohen/gefilterten Fehlern, beiden Regleranteilen und Summe; für eine vollständige Wiederholung zusätzlich der Vorzustand und die Schrittzeit. Ein möglicher begrenzter Ausgang benötigt eine passende Geschwindigkeitsreaktion und Integrationsbegrenzung. Ein solcher Ersatz ist in 0.4.8 nicht implementiert; bestehende Ausgabefristen und Übernahme bleiben unverändert.

## Hintergrundbetrieb und verbleibende Abnahme

Der installierte InputBridge behebt einen belegten Callback-Kontextfehler. Die neue Hintergrundmessung zeigt trotzdem aussetzende Callback-Abfragen. Eine positive letzte Aktivitätsmeldung beweist keine laufende Abfrage. Der optionale native Pedal-Probe beobachtet deshalb nur den bestehenden modernen Schreibvorgang und ist noch keine funktionierende Hintergrundlösung.

Offen bleiben die belastbare Behebung der unterschiedlichen Ausgabeverzögerungen, der TruckersMP-Hintergrundnachweis, die Warnphase am Bahnübergang sowie reale vollständige Maut-/Zahlungs-/Ausfahrtprüfungen der zusammen eingesetzten Fassung. Vorhandene Offlineprüfungen werden nicht als Ersatz für diese Messungen bezeichnet.
