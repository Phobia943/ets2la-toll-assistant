# Änderungen

## 0.3.5 – Start hinter bereits passierten Signalabschnitten

- Die Standprüfung nach Installation von 0.3.4 fand einen unabhängigen Startfehler: Ein etwa 20 m langer Kartenverbinder wurde wegen einer Signalmarkierung ganz am Anfang vollständig ausgeschlossen, obwohl der Lkw bereits rund 15 m dahinter stand.
- Nur für den ersten Startverbinder darf ein vollständig passierter Signalabschnitt mit mindestens 2 m Abstand hinter dem Lkw liegen. Signalmetadaten bleiben erhalten. Signale voraus, spätere Verbinder, Pkw- und Eskorte-Beschränkungen werden weiterhin abgelehnt; Richtung, Anschluss und nutzbare Strecke werden unverändert geprüft.
- 59 Integrationstests und weiterhin 41 unabhängige Logiktests. Die Offline-Kartenprobe liefert am unveränderten Haltepunkt acht zusammenhängende Planungseinträge statt eines Startabbruchs. Im Spiel geladen und automatisch vorbereitet. F8 aktiviert FSD; eine anschließende Minute Straßenfahrt mit bis zu rund 77 km/h enthält 59 frische Statusbilder ohne Fehlerbremsung oder Routenfehler. Eine vollständige Mautdurchfahrt bleibt offen.

## 0.3.4 – Korrektur falscher Tracking- und Mautstopps

- SDK-Eingaben werden als zusammengehörige, zeitgestempelte Bilder übernommen, ohne den synchronen Telemetrieverteiler hinter einer Mautberechnung warten zu lassen. Nach aufwendiger Routenarbeit wird die neueste Position geprüft. Echte veraltete Telemetrie löst weiterhin die Fehlerbremsung aus; der Grenzwert wurde nicht erhöht.
- Physische Bremsübernahme deaktiviert die Assistenz und neutralisiert Ausgaben vor dem Warten auf die Routenverwaltung. Ein Test hält diese Verwaltung länger als den Telemetriegrenzwert beschäftigt und prüft die sofortige Übernahme.
- Tracking misst den seitlichen Abstand unabhängig von der Kartenhöhe. Die Auswahl des nächstgelegenen Routenabschnitts bleibt dreidimensional, ebenso die Prüfung von Kartenanschlüssen. Tatsächliche seitliche Abweichung über 3 m und Gegenrichtung bleiben Fehler. Diagnose trennt seitlichen Abstand, Höhe und Bearbeitungszeit.
- Mehrere Signalobjekte derselben physischen Mautdurchfahrt erhalten eine gemeinsame Identität. Abweichende Zustandsmeldungen bleiben unbekannt. Die Modellzuordnung akzeptiert identische Kurven derselben Durchfahrt, aber keine unbestätigte oder widersprüchliche Wiederverwendung einer Signalnummer an anderen Durchfahrten.
- AutoPASS-Markierungen für normale Aufträge und Sondertransporte werden anhand ihrer jeweiligen Spieldefinition unterschieden. Die gespeicherte betroffene Anlage bietet dadurch eine erreichbare, automatisch abrechnende Lkw-Spur statt eines Halts vor einer unbekannten Spur.
- 58 vollständige und 41 unabhängige lokale Prüfungen. Kartenwiederholung: Die betroffene Straßenposition liegt seitlich unter 0,3 m neben der Kartenlinie, deren Höhe rund 3 m abweicht. Die betroffene Mautanfahrt wählt automatisch die erreichbare Schranke 7 ohne unbekannte Haltegrenze. Diese Kartenproben ersetzen keinen Fahrtest; vollständige Mautdurchfahrt mit 0.3.4 noch offen.

## 0.3.3 – Start während der Fahrt und Bremsübernahme

- Diagnosewerkzeuge für einen zusammenhängenden Maut-Fahrnachweis ergänzt: rein lesende Aufnahme von SDK-Telemetrie, rohen Schranken und Assistentenstatus; Auswertung benennt Datenlücken und bleibt ohne Fahrnachweis ausdrücklich unvollständig. 12 Prüfungen der Aufzeichnung und Auswertung sowie eine kurze reale Leseprobe bestanden. Keine Änderung der installierten Plugin-DLL dafür erforderlich.
- F8 fordert bei Vorwärtsfahrt eine Routenprüfung an. Bis zur geprüften Übergabe bleibt die Fahrt manuell. Geprüft werden aktuelle Position, Ausrichtung, Navi-Knoten, Fahrmodus, frische SDK-Daten und geschwindigkeitsabhängige Strecke voraus. Bereits überfahrene Planungseinträge werden vor der Übergabe entfernt. Die Route wird nur auf dem eigenen Tick eines exakt unterstützten Planers übernommen.
- F9 verwirft offene Startprüfungen ohne unbeabsichtigten Moduswechsel. Timeout, fehlende Route und abgelehnte Starts greifen nicht in die manuelle Steuerung ein.
- Neue physische Eingabe „Bremse – manuelle Übernahme“ in Controls. Ab 12 % gehen Lenkung und Tempomat aus, offene Prüfungen werden verworfen und verbleibende Steuerbefehle neutralisiert. Die zugeordnete Bremse kann auch eine Fehlerbremsung ausdrücklich zur manuellen Übernahme quittieren. Loslassen aktiviert nichts; F8 ist während gehaltener Bremse gesperrt. Keine Auswertung der automatischen Bremsung als Benutzereingabe.
- 53 vollständige und 39 unabhängige lokale Prüfungen bestanden. Im Spiel bestätigt: physische Bremse beendet die Assistenz, Gas löst keine Abschaltung aus, Loslassen startet nicht neu und manuelle Lenkung funktioniert. Start während der Fahrt bei rund 69 km/h mit aufgezeichneter Routenprüfung und anschließender gültiger Fahrentscheidung bestätigt. Eine vollständige Mautdurchfahrt mit 0.3.3 bleibt ausstehend.

## 0.3.2 – Startfreigabe nach bestätigter Route

- Die automatische Vorbereitung prüft die Startroute bei ausgeschalteter Fahrassistenz. Eine fehlgeschlagene Routenprobe sperrt F8 und zeigt den Grund an. Die Fahrmodule bleiben eingeschaltet.
- Die Freigabe gilt für die geprüfte Position, Richtung und Navi-Knoten. Manuelles Versetzen oder ein geändertes Ziel verlangt eine neue Prüfung. F9 löst die Fehlerbremsung und fordert eine neue Vorbereitung an.
- Die Verbindersuche berücksichtigt Übergangsgrenzen und kurze verbleibende Teilstücke. Die gesamte bestätigte Route muss weiterhin mindestens 25 Meter nutzbare Strecke enthalten.
- Normale Verbinder können eine eindeutig anschließende Straßenspur vor der Trackingprüfung wiederherstellen. Die Prüfung verlangt denselben Routenknoten, passende Richtung und Geometrie. Bereits belegte Fahrspuren und unklare Anschlüsse werden abgelehnt.
- Die Freigabe beim Rollen und die Erholung nach kurzen Datenlücken sind getrennt von tatsächlichem Trackingverlust. Eine entfernte Schranke allein beendet die Erholung nicht. Die Mautanfahrt verwendet den verbleibenden Bremsweg.
- Reale Fahrprüfungen der neuen Fassung stehen aus. Eine allgemeine Erkennung von Verkehrsschildern und die Messung der Durchfahrtshöhe sind weiterhin offen.
- 49 vollständige lokale Prüfungen und 39 unabhängige Logikprüfungen bestanden. Bei der Wiedergabe des aufgezeichneten Entscheidungsverlaufs mit 512 Schritten ändert sich die beanstandete Notbremsung zur regulären Fahrentscheidung.

## 0.2.11 – Richtungsprüfung an kurzen Abschnittsübergängen

- Anlass: Gegenrichtungsbremsung bei rund 56,6 km/h, laut Fahrer auf gerader Straße in korrekter Fahrtrichtung. Die gespeicherte native Projektion liegt an einer Abschnittsgrenze; die folgenden Routenproben zeigen eine gültige Spur. Die damaligen Richtungsvektoren fehlen, deshalb ist die konkrete Ursache nicht abschließend nachgewiesen.
- Reproduzierbarer Fehler: Zwei vorwärts verlaufende Straßen können durch einen kurzen, geometrisch rückwärts zeigenden Versatz verbunden sein. Bisher konnte genau dieser künstliche Übergang die Gegenrichtungsprüfung auslösen, obwohl beide Fahrsegmente richtig orientiert waren.
- Markiert ausschließlich die bereits erlaubten Verbindungen bis 2 m zwischen separat abgetasteten Planungseinträgen. Die Verbindungen bleiben für Abstand und Weglänge erhalten; für die Richtung gilt das folgende echte Fahrsegment. Reale rückwärts laufende Fahrsegmente, Gegenrichtung des Lkw, gegenläufige Neuplanung und größere nahe Lücken bleiben geschützt. Keine Erhöhung des erlaubten Winkels oder Spurabstands.
- Richtungsfehler speichern jetzt Lkw-Richtung, Skalarprodukt, betroffene Routenpunkte und Übergangsmarkierungen. Routenproben enthalten denselben örtlichen Kontext. Diese Diagnose bleibt lokal und ist nicht im Repository enthalten.
- 42 vollständige lokale Prüfungen und 32 unabhängige Logikprüfungen bestanden. Reproduktion über echte PathLib-Straßenobjekte sowie Tests für erhaltene Gegenrichtungs- und Lückenprüfung. Bestätigung des konkreten Vorfalls während der Fahrt steht aus.

## 0.2.10 – automatische Vorbereitung im Stand

- Nach dem Einschalten bereitet sich der Mautassistent automatisch vor, sobald der Lkw mindestens eine Sekunde durchgehend steht, die Fahrassistenz ausgeschaltet ist und frische Spiel-, Schranken- und Pathfinding-Daten ankommen. Die bereits vorhandene Prüfung der ACC-Version und ihrer drei nativen Begrenzungen bleibt erhalten. Fehlende oder noch initialisierende Module werden mit begrenzter Wiederholrate erneut geprüft.
- Die Vorbereitung schaltet keine Fahrassistenz ein und erzeugt keine Fahrzeugausgabe. Erst F8 startet die Fahrt. Bekannte Routenfehler, pausierte oder veraltete Daten, Fahrt und eine aktive Fehlerbremsung verhindern die automatische Vorbereitung.
- Ein Fehler unterbricht auch die automatische Vorbereitung. F9 bleibt zur ausdrücklichen manuellen Übernahme und zum Lösen der Fehlerbremsung erforderlich. Nach F9 und erneutem Stillstand erfolgt die Vorbereitung ohne zusätzlichen Klick. Normales Ausschalten mit F9 behält eine bestehende Vorbereitung bei.
- „Nur beobachten“ pausiert die Automatik ausdrücklich; F9 gibt sie wieder frei. Die Routenprobe bleibt von automatischer Vorbereitung getrennt, solange sie läuft. Oberfläche und Status erklären Bereitschaft bzw. fehlende Voraussetzungen.
- 39 vollständige lokale Prüfungen und 30 unabhängige Logikprüfungen bestehen. Neue Tests prüfen Zeitfenster, Wiederholrate, Start ohne Gas/Lenkung, reale ACC-Anbindung ohne doppelte Begrenzung, verspätete Initialisierung, alte Daten, Pause, Fahrt, Fehlerbremsung und F9. Die neue Startfolge wurde danach in der laufenden Anwendung bestätigt: Während des Rollens blieb F8 gesperrt; nach dem Anhalten erfolgte die Vorbereitung ohne Schaltflächenklick. Die Fahrassistenz blieb aus.

## 0.2.9 – Wiederherstellung nach ETS2LA-Update

- Mit ETS2LA 2026.11.5251 und ACC 2.1.1 neu gebaut und geprüft. ACC 2.1.1 wird nach Prüfung der Begrenzungs- und Pedalschnittstelle ausdrücklich unterstützt; andere ungeprüfte Versionen bleiben ausgeschlossen.
- Das Update hatte die Plugin-Verzeichnisse im Installationsordner neu erstellt. Der Mautassistent muss nach solchen Updates aus einer externen Sicherung wieder installiert werden. Die Standardmodule und Bibliotheken werden separat über ETS2LA installiert.
- Einstellungen liegen künftig unter `%LOCALAPPDATA%\ETS2LA-Mautassistent\settings.json`, außerhalb des ersetzten Anwendungsordners. Vorhandene gültige Einstellungen am alten Ort werden einmalig kopiert. Bestehende Einstellungen am neuen Ort werden niemals durch die alte Kopie überschrieben.
- Keine rekonstruierten oder ungeprüften Mautspurfreigaben. Falls ein Update die alte Einstellungsdatei bereits gelöscht hat, können daraus keine Freigaben wiederhergestellt werden.
- 35 vollständige lokale Prüfungen gegen die neue Installation und 27 unabhängige Logikprüfungen bestehen. Die Ausgabeimplementierung der neuen ETS2LA-Version wurde mit dem zuvor geprüften Stand verglichen; F9- und Bremswirkung im aktualisierten laufenden Spiel müssen noch bestätigt werden.

## 0.2.8 – entfernte Planungslücken gezielt behandeln

- Behebt eine überstrenge Prüfung der gesamten geplanten Strecke: Bei sauberer aktueller Spur können entfernte, noch nicht verbundene Routenabschnitte zunächst offen bleiben. Anlass war ein Stopp bei 52,8 km/h mit nur 1,5 cm aktuellem Spurabstand aufgrund eines späteren 4,5-m-Übergangs. Dessen genaue Entfernung war in der alten Diagnose nicht enthalten.
- Verwendet ausschließlich den zusammenhängenden Routenanfang bis zur ersten Lücke. Es wird keine Verbindung über die Lücke erfunden. Schranken hinter diesem Ende werden nicht auf den letzten gültigen Punkt projiziert.
- Sobald die offene Verbindung innerhalb der geschwindigkeitsabhängigen Planungsreserve liegt, bleibt die Fehlerbremsung aktiv. Reserve: mindestens 25 m, sonst 15 m plus eine Sekunde Fahrweg plus v²/(2×2 m/s²). Das ist eine Planungsannahme, kein zugesicherter Bremsweg.
- Derselbe Abstand wird bei Routenempfang, laufender Positionsprüfung und Schrankensuche verwendet. Echte Spurabweichung, Gegenrichtung und Datenverlust bleiben unverändert geschützt.
- Diagnose nennt nun auch die offene Verbindung, verbleibende zusammenhängende Strecke und erforderliche Reserve. 34 vollständige lokale Prüfungen und 26 unabhängige Logikprüfungen; Fahrtest dieser Anpassung ausstehend.

## 0.2.7 – Fehlerbremsung und Richtungsprüfung, Fahrtest ausstehend

- Routenfehler während eingeschalteter, vorbereiteter Assistenz fordern unmittelbar im Routen-Callback starkes Bremsen an und schalten die Lenkassistenz aus. Ein eigener Pedalkanal bleibt auch bei ausgeschaltetem ACC wirksam. Zuvor endete mit dem Ausschalten der Assistenz auch die ACC-Bremsausgabe.
- Bremsanforderung bleibt bis F9 oder ausdrücklicher manueller Übernahme bestehen, auch bei wieder verfügbarer Route und im Stand. F8 löst die Bremse nicht und kann ohne erneute Vorbereitung keine Fahrt fortsetzen.
- Überwachung im Tick, Telemetrie-Callback und ACC-Aufruf: fehlende Telemetrie ab 0,5 s; verlorene zuvor verfolgte Route ab 1 s. Beim Erststart bleiben bis zu 2 s für den Aufbau einer Route. Gemeldete Routenfehler warten diese Fristen nicht ab.
- Prüft den Abstand zur Route und bei Lenkassistenz die gerichtete lokale Fahrspur statt einer um 180° gefalteten Straßenachse. Gegenläufige Lkw-Ausrichtung, Rückwärtsfahrt und die Umkehr einer zuvor akzeptierten Route führen zur Fehlerbremsung, auch außerhalb einer Mautstelle.
- Eigener Ausgabekanal mit Gewicht 100 und 0,2-s-Ablauffrist. Bremsen erfolgt über die vorhandene öffentliche Ausgabe-API. Eine ausdrückliche neutrale Lenkausgabe verhindert, dass nach Ablauf des abgeschalteten Lane-Assist-Kanals dessen letzter Lenkwert im Ausgabespeicher stehen bleibt. F9, manuelle Übernahme und Plugin-Abschaltung senden neutrale Pedale und Lenkung; bloßes Ablaufen eines Kanals genügt nicht in jeder Ausgabekonfiguration.
- 32 vollständige lokale Prüfungen und 25 unabhängige Logikprüfungen bestanden. Tests nutzen isolierten Hostzustand und die installierte Kanalschnittstelle ohne Fahrzeugausgabe. Tatsächlicher Bremsweg, Ausgabeverbindung und der konkrete Vorfall sind noch nicht im Fahrtest bestätigt. Keine garantierte Notbremsfunktion bei ausgefallener App oder Spielanbindung.

## 0.2.6 – Ausfahrtkorrektur, Fahrtest ausstehend

- Verbindet die bestätigte Mautspur mit der geometrisch anschließenden Spur der bereits geplanten Straße. Ein korrekter Zielspurindex allein genügt nicht, wenn die Straße noch auf einer anderen Eingangsspur beginnt.
- Prüft gemeinsamen Routenknoten, Fahrtrichtung, Abstand und Eindeutigkeit. Kein Wechsel auf die Gegenseite, keine erfundene Anschlussstraße und keine nachträgliche Korrektur, wenn der Lkw bereits auf der anderen Spur fährt. Unklare Übergaben führen zur manuellen Übernahme.
- Ändert den von Pathfinding intern weiterverwendeten Planungseintrag und setzt dessen Spurwechselzustand über die öffentliche PathLib-Schnittstelle zurück. Die zugrunde liegenden Kartendaten bleiben unverändert.
- Unterteilt ausgewählte Mautkurven für die native Positionsprojektion. Ein synthetischer Kurvenfall erzeugte zuvor mehr als 2,2 m scheinbare Abweichung trotz korrekter Position. Die gespeicherte Mautgeometrie zeigte diesen starken Fehler nicht; dies ist kein Nachweis der Ursache des gemeldeten Vorfalls.
- Exakte Segmentlängensummen vermeiden Rundungslücken an den Kurvenendpunkten. Rückwärts durchlaufene einzelne Straßen werden auch in der Diagnose korrekt abgetastet.
- 29 lokale Prüfungen bestehen, darunter Anschluss-Spuren in beiden Richtungen, einzelne und gruppierte Straßen, native Folgeabfragen, Gegenfahrbahn-Ausschluss und mehrdeutige Übergänge. Fahrtest auf Wunsch verschoben; die betroffene Ausfahrt ist noch nicht praktisch bestätigt.

## 0.2.5 – in Erprobung

- Schließt den Wiederanlauf ohne Mautbegrenzung nach einer Störung: Bei eingeschaltetem Plugin werden Startversuche ohne erfolgreiche Vorbereitung zurückgewiesen und erklärt.
- Prüfung beim Plugin-Tick, beim Empfang von Route und Telemetrie sowie in der ACC-Abfrage. Keine globale atomare Eingabesperre des Hostprogramms.
- Normaler F9-Stopp behält die Vorbereitung bei. Beobachtungsmodus stoppt die Fahrassistenz; reine Routenproben bleiben möglich.
- Wegfall des benötigten ACC-Moduls beendet die Fahrassistenz statt unbemerkt in Beobachtung weiterzulaufen.
- Diagnosen enthalten Vorbereitungsgrund, Startfreigabe, gewählte Spur und Mautanlagenkennung.
- Regressionstest reproduziert Fehlerabschaltung und wiederholte Starts über die echten Plugin-Einstiegspunkte mit isoliertem Hostzustand. 27 vollständige lokale Prüfungen bestehen. Keine automatische Erkennung der Höhenfreiheit hinzugefügt; Fahrtest bleibt offen.

## 0.2.4 – in Erprobung

- Routenmessung und Diagnose kopieren die veränderlichen Planungsdaten, bevor sie PathLib abfragen. Auch eine Abfrage mit ausgeschalteter Spurwechselbestätigung kann dort den Spurwechselzustand zurücksetzen oder initialisieren. Die Messung soll den Zustand der eigentlichen Lenkroute nicht ändern.
- Regressionstest gegen PathLib 1.2.8 reproduziert das Zurücksetzen des Zielspurindex und prüft die Trennung vom Original.
- Bei einer Routenunterbrechung werden Übergangsindex, Abstand, Elementtypen, Richtung und Spurindex gemeldet. Die bestehende 2-m-Prüfung bleibt erhalten.
- 26 lokale Prüfungen bestanden. Ein Fahrtest dieser Version und die erneute Prüfung direkt hinter der Schranke stehen aus.

## 0.2.3 – erste öffentliche Quellfassung

- Begrenzte Anfahrt und Durchfahrt mit Wartezeit nach Schrankenöffnung.
- Auswahl bestätigter automatischer Lkw-Mautspuren; manuelle und unbekannte Zahlarten führen zur Übernahme.
- Schrankenverfolgung bei manueller Abschaltung zurücksetzen.
- Wegmessung hinter einer freigegebenen Schranke nur bei weiterhin gültiger aktueller Navigation fortsetzen.
- Eine bereits passierte, wieder schließende Schranke löst keinen neuen Halt aus.
- Routendiagnose bei Fehlern an der Mautausfahrt.
- Öffentliche Bauanleitung mit frei konfigurierbarem Installationspfad.
- Eigene synthetische Testgeometrie und unabhängige Logiktests.
- Keine vorgegebenen Freigaben für einzelne Mautanlagen.

Die öffentliche Fassung basiert auf dem lokal geprüften Stand 0.2.3. Entfernte lokale Spurvorgaben und ersetzte Testdaten sind Anpassungen für die Veröffentlichung. Die erneute praktische Prüfung der Mautausfahrt bleibt offen.
