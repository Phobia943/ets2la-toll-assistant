# Review 0.4.0 – Eigene Geschwindigkeitsregelung und Routenanschluss

Stand: 17. November 2026. **Offline fertig und Paket geprüft, nicht installiert und noch nicht im Spiel geprüft.** Installierte Ausgangsfassung ist 0.3.22. Nur die konkret versiegelte Kombination gehört zum Update; Zwischenbuilds und isolierte Testkopien sind keine installierbaren Ersatzdateien.

## Problem und neue Zuständigkeit

Die bisherigen Aufnahmen enthalten getrennte Fehlerklassen: einen zu späten Halt vor einer notwendigen Einordnung, Halte in ausgewählter Spur vor laut Fahrer offenen Toren, eine vorausliegende 4,5 m-Routenlücke und Phasen mit angeforderten, aber im Spiel kaum wirksamen Pedalen. Die Aufnahme enthält keine vollständige historische Ursache für alle diese Ereignisse. Eine neue Regelung darf sie nicht zu einem einzigen belegten ACC-Fehler zusammenfassen.

0.4.0 übernimmt die normale Längsregelung vollständig im Mautassistenten. Pathfinding und Lane Assist bleiben verantwortlich für die Route beziehungsweise Lenkung. Ein eigener Regler bildet aus Zieltempo, beobachteter Bewegung und aktuellen Sicherheits-/Streckenbegrenzungen Gas und Bremse. Das neue Hostpaket lässt genau diesen normalen Pedalkanal zu; der alte native ACC-Kanal kann selbst mit frischen gültigen Zeitstempeln keine normale Pedalautorität erwerben. Fehlerbremsung bleibt eine eigene, stärkere Bremsquelle.

Ein unabhängiger Pedalwächter überwacht den Ursprung eigener Reglerbefehle außerhalb des eigentlichen Regelzyklus. Ein ausgefallener eigener Zyklus wird dadurch nicht allein von weiter frischen Lane-Assist-Lenkbefehlen verdeckt. Die bisherigen manuellen Brems-/F9-Übernahmewege bleiben erhalten. Die Frist für den Ursprung eigener Pedalbefehle bleibt 150 ms. Ein aktueller Quellenwiderruf allein wird über den vorgesehenen normalen Brems-/Haltepfad behandelt, nicht pauschal sofort als Notfall. Echte Gefahren und tatsächlich ausgefallene Befehlsproduktion bleiben davon getrennt.

Die Ausgabe bleibt an aktuelle Sitzung und ursprüngliche Berechnungszeit gebunden. F9, physische Bremsübernahme und Pause entziehen die aktive Fahrfreigabe. Quellenverlust entzieht die Gas-/Fahrfreigabe; der vorgesehene Brems-/Halte- und Übernahmepfad bleibt verfügbar. Eine späte Veröffentlichung darf die Ursprungszeit nicht verjüngen. Das ist ein begrenzter Schutzvertrag, kein Nachweis, dass das Spiel jede akzeptierte Ausgabe tatsächlich umsetzt.

Der eingebaute Spieltempomat muss vor der Übernahme ausgeschaltet sein. Die eigene Regelung übernimmt bei aktivem Spieltempomat nicht; sie betätigt keinen blinden Toggle, der einen fremden/zwischenzeitlich geänderten Zustand unbeabsichtigt einschalten könnte.

## Tempo, Rollen und Wahrnehmung

Auf freier Strecke ist bis 100 km/h vorgesehen. Konfigurierte Geschwindigkeitsgrenzen, Kurvenvorschau, erkannter Verkehr, Ampeln, Mautphase und fehlende Streckenbelege können das Tempo senken. Kleine Überschreitungen eines weichen Tempowunschs dürfen bei belegter freier Strecke durch Rollen abgebaut werden. Eine harte Begrenzung oder ein Haltgrund wird nicht unterdrückt. Die Regelung beobachtet zusätzlich die Umsetzung ihrer Pedalbefehle. Ein nicht gemeldetes Straßenlimit mit Wert 0 ist kein Solltempo 0: dann gilt vorsichtig höchstens 30 km/h als Ersatzgrenze. Niedrigere aktuelle Kurven-/Mautgrenzen bleiben wirksam; 100 km/h benötigen den belegten freien Streckenfall.

Verkehrs-/Parkobjekte werden nach der nachgewiesenen Konvention des vorhandenen Providers umgerechnet. Rohe WXYZ-Produzentendaten dürfen nicht ungeprüft als übliche Welt-XYZW-Quaternion interpretiert werden. Byte-/Eckpunktfixtures prüfen den tatsächlich verwendeten Transform. Nur vollständig leere Slots werden verworfen; ein teilweise belegter oder ungültiger Körper ist kein leerer Platz.

Die Quelle liefert begrenzte 39 Verkehrs- und 40 Parkslots. Die vorhandenen Aufliegerslots werden begrenzt und konsistent verarbeitet. Das belegt keine vollständige Sicht aller Fahrzeuge oder Hindernisse; zukünftige Spawns und Objekte außerhalb der Quelle bleiben unbekannt. Eine nahe Baustelle oder ein Verkehrsschild ist dadurch nicht allgemein erkannt.

Maut-GATEs erhalten ihre Autorität ausschließlich aus der gerichteten Tor-/Routenzuordnung. Sie werden nicht durch den räumlich nächsten SDK-Pfosten als gewöhnliche Ampel ersetzt. Ampeln werden getrennt ausgewertet. Der ältere geometrische Gegenfall zum nativen GetClosestGate zeigt, dass ein gegenläufiger Toranker knapp unter 4 m an derselben Route liegen kann; er beweist nicht, dass genau dieser Aufruf die früheren Livehalte verursacht hat.

## Einordnung vor der Maut

Die bestehende Mindeststrecke eines kurzen Einordnungswechsels beträgt 45 m. Die aktuelle spanische Haltprobe hatte nur 15,31 m übrig. Kontrafaktische frühere Positionen auf derselben tatsächlichen Straße bestehen bei 160/220 m Rest und 20/77 km/h die echte Auswahl-/Applyprüfung. Das ist kein historischer Beleg, wann der Planer damals eine bestimmte Spur hätte wählen können.

Neu ist ein separater normaler Vorhalteanker 60 m vor dem realen gerichteten Ende der eindeutig angeschlossenen Zufahrtsstraße: 45 m Wechselstrecke plus 15 m normale Reserve. Er verwendet die echte gerichtete Bogenlänge. Ein nur teilweise aufgenommener Straßenprefix, falsche Kontrollknoten, unklare Position oder fehlende Berechtigung erzeugen keinen solchen Anker. Ein schon hinter dem Lkw liegender neuer Anker wird nicht als vorausliegend ausgegeben.

Der einmal auf derselben Quelle festgelegte Punkt wandert nicht mit wiederholten Pending-Frames. Quellen-/Eigentümerwechsel und F9 widerrufen ihn. Die bisherige physische Aufteilungsgrenze und deren 15 m-Sicherheitsabstand bleiben separat unverändert; der neue Punkt ist ein normaler Planungshalt und keine global vorgeschobene Notbremsgrenze.

### Vorbereitung trotz vorläufiger Manualwahl

Zwei konkrete Blocker sind getrennt reproduziert und korrigiert: Die alte Upgradeprüfung verlangte null Entryänderungen, obwohl ein inzwischen zulässiger neuer kurzer Wechsel gerade eine echte Änderung benötigt. Außerdem entfernte das tatsächliche Manual-Apply die alternativen Host-Einfahrtsoptionen. Der Router behält deshalb höchstens 64 zuvor gefundene automatische Topologiekandidaten als Suchraum, nicht als Clearance- oder Fahrfreigabe. Er bindet sie an dieselben ParsedPrefab-/Descriptor-/RawPrefab- und gerichteten Nodeobjekte, Welttransformation, kanonischen Kurven und Model-/Variant-/Partsangaben. Profil-/Quellenwechsel, geänderte Platzierung und F9 dürfen keine alte Freigabe leihen. Jede Nutzung erfordert neue vollständige Suche, Zahlungs-/Geometrieprüfung und aktuell zulässigen Entry.

Eine neue gültige Einordnung wird auf einer getrennten Planungskopie geprüft; die tatsächliche Übernahme berechnet die Änderungen gegen den aktuellen Plan erneut. Die strenge nulländernde Prüfung bleibt für die getrennte Wiederherstellung bereits angewandter Pfade erhalten.

Allein diese neue Upgradefähigkeit hätte noch keine rechtzeitige Verzögerung bewiesen. Deshalb bleibt bei vorläufiger Manualwahl ein eigener normaler Vorbereitungshalt aktiv, wenn eine Automaticroute aktuell physisch frei ist, der Entry allein wegen Tempo scheitert und derselbe echte Entry bei höchstens 20 km/h einen erlaubten kurzen Wechsel auf der direkt zugeordneten Zufahrtsstraße bestätigt. Auch dessen nachfolgender Anschluss wird vorbereitet geprüft. Eine neue Vorbereitung verlangt eine positive nachgewiesene Distanz und einen innerhalb des bestehenden eigenen Reglerlimits möglichen Bremsweg mit derselben 350-ms-Antwortreserve. Die physische Aufteilungsgrenze bleibt unverändert.

Während der Vorbereitung wird der Entry laufend neu geprüft. Sobald er tatsächlich zulässig ist, darf die automatische Wahl übernommen werden; Vorhalteanker und Vorbereitungsstatus werden gelöst. Blockierte Automatik, fehlende Wechselberechtigung, unbekannter Modellbeleg oder unmögliche neue Vorbereitung erzeugen keinen endlosen zusätzlichen Vorbereitungshalt. Ein sicher erreichbarer manueller Pfad bleibt der ehrliche Rückfall, keine pauschale Aussage über die Zahlarten der ganzen Anlage.

Die geschlossene synthetische Geschwindigkeitsprobe verwendet den echten Router und eigenen Regler, 50-ms-Schritte, 150-ms-Pedalantwort sowie ein unabhängiges beladenes flaches Fahrzeugmodell mit 3,2 m/s² maximaler Bremswirkung und geschwindigkeitsabhängigem Widerstand. Start bei 130 m Rest und 77 km/h. Ohne vorgegebene Geschwindigkeitsfolge wird der tatsächliche automatische Entry bei **61,995 m und 19,481 km/h** angewandt: aktuelle Lane 1 bleibt, Ziel wird Lane 2, der kurze Wechsel ist markiert und der Vorbereitungshalt endet. Die Probe löst keine physische Fehlergrenze aus und hält die Pedale exklusiv. Sie prüft die Geschwindigkeits-/Planungskopplung bis zum Wechselbeginn, keine vollständig simulierte seitliche Spuränderung oder reale Spielphysik.

Die zusätzliche tatsächliche Plugin-/OnPath-/Watchdog-Integration ist eine eigene bestandene Abnahme: dieselbe Manual→Auto-Änderung wird mit echter Supervisorprüfung und beiden physischen Pedalsenken isoliert ausgeführt. Die Zwischenfassung mit Produkt-SHA beginnend `496B8DFF` reproduzierte den Fehlerhalt; die Endfassung leitet die neue begrenzte Entscheidung am Pfadübergang aktuell ab. Ein isoliert grüner Router-/Reglerlauf allein hätte diesen Aufruferfehler nicht erkannt. Auch diese durchgehende isolierte Prüfung ersetzt keine reale Fahrt.

## Anschluss hinter der Maut

Die Ausfahrtsprüfung endet nicht mehr nach dem ersten anschließenden Straßenstück. Sie prüft begrenzt auch dessen nächsten gerichteten Prefabanschluss und muss den unveränderten Restpfad tatsächlich erreichen. Erlaubt ist eine eindeutige reale Alternative desselben Prefabs mit denselben gerichteten Ein-/Ausfahrts-Controlnodes und physisch passender Naht. Eine notwendige zukünftige Nachbarspuränderung wird nur mit gültiger Berechtigung und genügend belegter Straßenlänge geplant; die aktuelle Einfahrspur wird nicht seitlich versetzt.

Bereits verbundene unveränderte Pfade bleiben geometrisch erhalten, auch wenn eine separat belegte automatische Spur eine SmallVehicles-KI-Präferenz trägt. Neue Alternativen bleiben streng gefiltert: kein unbelegter Small-/Player-/Escortpfad, kein übersprungenes Semaphore, keine abweichende Verkehrsregelbindung. Falsche Ebene, Gegenrichtung, mehrdeutige Anschlusskurven und unterschiedliche Kontrollnodes bleiben ausgeschlossen. Eine 4,5 m-Lücke wird nicht durch Toleranzänderung geschlossen.

Änderungen werden auf einem Snapshot vorbereitet. Die komplette ursprüngliche Item-/Feldbasis wird vor dem Batch verglichen. Abweichungen verwerfen den Commit. Für den kurzen eigentlichen Commit gilt die verifizierte synchrone Publisher-Ownership des Hostpfads; eine allgemeine Atomizitätsgarantie gegen beliebige Fremdplugin-/UI-Mutationen ist nicht belegt.

### Reale Offline-Kartenprobe

Frisch geladene Kartendaten reproduzieren einen RoadList/Backward/Lane 1→Prefab/Forward-Spalt von 4,499310 m bei 357,613076 m Restvorschau. Gespeichert waren 4,50 m und 357,613070 m. Die ursprünglich rekonstruierte Folgekurve [0] ist lateral falsch angeschlossen. Eine vorhandene Alternative [1] verbindet dieselben gerichteten Kontrollknoten; Straße/Lane 1 und Zielspur 0 bleiben unverändert. Danach ist die rekonstruierte Route ohne diese Lücke verfolgbar.

Letzter identischer Kartengegenlauf gegen Produkt-SHA256 `2DCE3375D186D74FDD169C560F8C618BED7E7008DB11C2AB218BE3925C9BE0EB`: erfolgreich, reine `Align`-Zeit 15,0004 ms. Diese Messung umfasst weder Modellladen noch den gesamten Fahrzyklus und ist keine allgemeine Laufzeitgarantie.

Die vollständigen historischen Nachfolge-UIDs wurden nicht gespeichert. Daher ist dies eine geometrisch sehr eng passende Rekonstruktion des aufgezeichneten Befunds, kein Beweis des vollständigen damaligen Folgeplans oder einer real gefahrenen korrigierten Passage. Die Aufnahme mit manuellem Gas gegen 14:40 Uhr und anschließend gemeldetem Trackingverlust ist ausdrücklich kein autonomer Durchfahrterfolg.

## Zahlungsart und Profile

Unbekannte Zahlungsart wird nicht als manuelle Zahlung bezeichnet. Ist eine manuelle Spur ausgewählt und eine Automatikspur zwar bekannt, aber derzeit nicht erreichbar/freigegeben, sagt die Meldung dies getrennt. Daraus folgt keine Behauptung, die Anlage rechne ausschließlich manuell ab.

Standardhöhe 4,5 m und Standardbreite 3,4 m bleiben ausdrückliche Planungsannahmen. Lesbare Zahlungsmetadaten sind kein Freiraumbeleg. Unbekannte oder blockierte Geometrie bleibt gesperrt. Der optionale detaillierte Ladungs-/Gespannschutz behält seine bisherigen Quellen- und Modellgrenzen.

## Nachweisumfang und Auslieferung

Der eigene isolierte Routingrunner besteht 255 Assertions einschließlich der bisherigen Ausfahrtsregressionen. Er prüft unter anderem Vorwärts-/Rückwärtskartenausrichtung, aktuelle gegenüber künftiger Spur, 15 m-Ablehnung, Kontrollknoten/Ebenen, Alternative und Mehrdeutigkeit, geänderte Basis, frühzeitigen unverrückbaren Anker, gekrümmte Bogenlänge, Teilprefix und Zahlungsbeschriftung. Die kritische Gegenprüfung umfasst die begrenzten Routing- und Topologiebestandsbedingungen sowie die gesonderte tatsächliche Plugin-/Watchdog-Übergangsprüfung. Das ist keine allgemeine Fahrfreigabe.

Finaler Prüfstand: **200 Integrationsprüfungen, 93 Kernlogikprüfungen, 223 isolierte Host-/Plugin-Pipelineprüfungen, 64 Aufnahmeprüfungen und 3 historische Wiedergabeprüfungen** bestanden. Die zusätzlichen 255 Routingassertions enthalten bereits anderweitig ausgeführte Fälle und werden nicht hinzuaddiert. Historische Logikwiedergabe, synthetische Dynamik, isolierte Ausgabepipeline und reale Kartenrekonstruktion haben unterschiedliche Beweisgrenzen.

Produkt-SHA256: `2DCE3375D186D74FDD169C560F8C618BED7E7008DB11C2AB218BE3925C9BE0EB`. Hostmanifest-SHA256: `5365FB49AAAE8711CD8118FD1C01C93F0C0C1E2A853761172DDC694B2174B374`. Der identische reale Kartenlauf wurde gegen diesen Produktpin wiederholt. Ein späterer Build benötigt einen neuen Paketnachweis und denselben Gegenlauf erneut. Für das Update werden vier ETS2LA-Dateien benötigt: Plugin, Game, State und LaneAssist. Die ACC-Datei und die bisherigen Spiel-DLLs bleiben unverändert. Pathfinding, Lane Assist und Mautassistent müssen mit den passenden Bibliotheken bereitstehen. ACC ist optional; sein alter Pedalkanal bleibt im neuen Host gesperrt.

## Noch erforderlich

- Exakte Paketkombination sichern, prüfen und erst nach autorisiertem geschlossenem ETS2LA austauschen; kein automatischer App-/Fahrtstart.
- Neue Standprüfung: Versions-/Quellenbereitschaft, F8, physische Bremsübernahme, F9 und tatsächlich wieder manuelle Lenkung/Pedale.
- Neue überwachte Fahrt: freie Strecke/Rollen/Kurven, Verkehr/Ampel, geschlossene und offene Mautschranke, automatische Abrechnung, vollständige Ausfahrt und Folgeanschluss.
- Alt+Tab-Gas/-Bremse in TruckersMP bleiben offen. Ein eigener Regler behebt den Fokus-/Spielausgabeweg nicht automatisch.
- Keine Abdeckung aller Länder, Hindernisse, Baustellen, Sonderladungen oder aller historischen Vorfälle behaupten.
