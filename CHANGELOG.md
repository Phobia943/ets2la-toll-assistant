# Änderungen

## 0.4.11 – kurzer Mautanschluss, Grenzschranken und Fokus-Rückkehr

- Nutzt für nachgewiesene kurze benachbarte Ausfahrtswechsel den bestehenden langsamen 45-m/20-km/h-Vertrag. Die Begrenzung überlebt Routenpruning und neue Wrapper bis zum tatsächlichen Ende; nach Bremse/F9 benötigt sie einen frischen Nachweis. Andere native Spurwechsel erhalten dadurch keine neue globale Beschränkung.
- Erfasst eigenständige Grenzschranken über ihre gerichtete Aktivierungsfläche und den passenden aktuellen SDK-Pfosten. Fehlende oder widersprüchliche Öffnungsdaten halten normal; eine bereits beobachtete Zufahrt bleibt auch nach kurzem Quellenverlust überwacht. Automatische Grenzabfertigung wird nicht behauptet.
- Trennt belegten Fokusverlust von echten Pedalfehlern, prüft frische Gefahren nochmals vor positiver Ausgabe und erhält die ursprünglichen Quellen-/Ausgabefristen. TruckersMP-Hintergrundpedale sind damit noch nicht behoben.
- Ergänzt begrenzte Prüfung des nachlaufenden einzelnen Aufliegers bis 20 km/h sowie ausdrücklich statische Sign-Kollisionsmodelle. Keine allgemeine Hindernisumfahrung oder automatisch weiter ausholende Kurve.
- Verhindert einen neuen Fehlerhalt allein aufgrund einer noch pausierten Planung bei frisch bestätigtem Stillstand. Reine Routenereignisdiagnose ergänzt; die technische 175-ms-Ausgabepause und eine separat protokollierte native Planerexception bleiben offen.
- 109 Logikprüfungen, 280 Integrationsprüfungen und 36 Installationssimulationen bestehen. Die endgültigen Dateien bestehen außerdem 351 Hostprüfungen sowie einen teilweise überlappenden separaten Lauf mit 278 Hostprüfungen. Unabhängiger Gegenreview abgeschlossen; kein Ersatz für die noch offene Fahrtprüfung.
- Am 18. November um 17:05 MEZ mit geprüfter Sicherung installiert; alle 18 Dateihashes bestätigt. Anwendungen nicht gestartet, keine Spielabnahme behauptet.
- [Gegenprüfung, Installationsstand und offene Fahrabnahme](REVIEW-0.4.11.md).

## 0.4.10 – Katalogwiederverwendung geladen, Fahrabnahme offen

- Behält vollständig geladene Modell- und Hookup-Kataloge über leere Arbeitswarteschlangen hinweg, gebunden an die jeweilige Dateisystemgeneration. Archive und Modellgeometrie bleiben getrennt verwaltet.
- Korrigiert einen teilweise übernommenen Quellenwechsel bei fehlgeschlagener Archivabfrage. Unlesbare Hookup-Dateien dürfen keinen Teilkatalog mit möglicherweise falscher Zahlart freigeben.
- 260 Integrationsprüfungen und 307 isolierte Host-/Plugin-Assertions bestehen. Keine Änderung von Ausgabealtergrenzen oder Fahrregelung. Die Ursache der zusätzlich aufgezeichneten 164-ms-Ausgabepausen ist noch offen. [Befund und Grenzen](REVIEW-0.4.10.md).
- Nach Nutzerstart ist die geladene Fassung bestätigt. Bewusstes F8, 26 Sekunden Parkneutralisierung mit Spielpedalen 0/0 und anschließendes Anfahren sind aufgezeichnet. Die erneute manuelle Übernahme und vollständige Fahrabnahme stehen aus; kein Nachweis einer allgemeinen Fehlerfreiheit oder funktionierender Hintergrundpedale.

Ein späterer Fahrbefund unter 0.4.10 um 14:39 MEZ belegt erneut einen technischen Fehlerhalt durch 174,95 ms alte Pedalausgabe nach bereits aufgehobenem Ampelhalt. Die Katalogoptimierung ist damit keine vollständige Behebung der Ausgabepausen; Laufzeitursache weiter offen.

## 0.4.9 – Kreuzungsfix installiert, Spielabnahme offen

- Korrigiert die native Prefab-Graphsuche: nur gerichtete Nachfolgeverbindungen, keine Mischung mit Rückverbindungen. Gültige Alternativen bleiben erhalten; reale fehlerhafte Kreuzungsroute im Gegenlauf ausgeschlossen.
- Interpolation, Richtungsprüfung und Ausgabefristen unverändert. Zusammengehöriges Komponentenpaket `0.4.9-host-1`; Routenberichte enthalten zusätzlich die native Kurvenrichtung.
- Auflieger-Nachlauf an Pfosten und TruckersMP-Hintergrundpedale bleiben offen. [Befund und Gegenprüfungen](REVIEW-0.4.9.md).

## 0.4.8 – Kandidat, Abnahme offen

- Ergänzt positive Hinderniserkennung aus tatsächlichen aktiven Kollisionsmodellen der Straßenkarte. Normale Bremsgrenze statt künstlicher Notbremsverriegelung; unbekannte Daten sind keine Hindernis- oder Freiraumbehauptung.
- Verlegt Karten-, Modell- und Kollisionsarbeit in einen eigenen geprüften Hilfsprozess. Ergebnisse behalten ihre ursprüngliche 200-ms-Frist und werden bei anderer Spur, Kartenquelle oder manueller Übernahme verworfen. Neu veröffentlichte identische Routen und entfernte Routenschwänze bleiben korrekt zugeordnet.
- Sichert Pedalfehler samt ursprünglichem Tickstadium dauerhaft. Ausgabegrenzen bleiben unverändert.
- Optionaler Messbaustein beobachtet vorhandene moderne Pedalschreibvorgänge vor/nach der Ausgabe; keine behauptete Behebung der Hintergrundsteuerung.
- [Gegenprüfungen, Live-Befunde und verbleibende Abnahme](REVIEW-0.4.8.md).

## 0.4.7 – installiert, zusätzliche Livefehler untersucht

- Vereinheitlicht die am Fahrzeugpunkt geprüfte Route für Geschwindigkeit, offene Mautschranken, Halteboden und Rollen, ohne Sensor- oder Ausgabegrenzen zu verlängern.
- Unterscheidet eine geprüfte gerade Strecke von fehlender Kurvenvorschau; Wiederherstellung berücksichtigt neu verfügbare Kurven- und Szenengrenzen.
- Erhält noch nie ausgestellte Zahlungsreservierungen passiv über kurze Unterbrechungen; bereits ausgestellte Versuche bleiben gesperrt.
- Entfernt wiederholte Vollserialisierung alter Fehlerberichte aus dem laufenden Status.
- Separater InputBridge korrigiert den nachgewiesenen Callback-Kontextfehler und ergänzt messbare Eingabediagnose; nach eigenem Gegenreview ebenfalls installiert.
- Unabhängiger Endreview abgeschlossen; am 18. November um 11:04 MEZ mit geprüfter Sicherung installiert. Anschließend vom Nutzer gestartet und aufgezeichnet. Die Aufnahme belegt weiterhin Hintergrund- und einzelne Ausgabeprobleme. [Prüfergebnisse und offene Produktgrenzen](REVIEW-0.4.7.md).

## 0.4.6 – gebundene Schranken an Bahnübergängen

- Ordnet die sichtbaren Schranken über die tatsächlichen Semaphore-Profile und deren `id_map` dem Routencontroller zu. Der zentrale Kartenpunkt wird nicht mehr als fehlende Ampel behandelt, wenn alle zugehörigen Schranken aktuell offen gemeldet werden.
- Liest Profile einschließlich Vererbung und kompatibler Ländervarianten auf eigenen Hintergrund-Dateiströmen. Fehlende oder widersprüchliche Definitionen bleiben unbekannt.
- Verlangt alle erwarteten Schranken an ihren transformierten Kartenpositionen. Fehlende, bewegte, geschlossene oder widersprüchliche Mitglieder und ausdrücklich erfasste rote Signale verhindern die Freigabe. Wiederholte Kurvenreferenzen erzeugen keine zusätzlichen Haltepunkte innerhalb der Gleise.
- Keine Änderung der Routenfristen, Lenküberwachung, Pedalausgabe oder Mautzahlung. [Vorfall, Gegenprüfungen und Grenzen](REVIEW-0.4.6.md).

## 0.4.5 – Pedalempfangsstörung ohne technische Notbremsverriegelung

- Fehlender logischer Pedalempfang sperrt Gas und meldet die Störung, ohne allein einen Fehler-Vollbremskanal zu erzeugen oder gesunde Lenkung abzuschalten. Aktuelle Gefahren-, Kurven-, Verkehrs-, Tor- und Haltegrenzen bleiben erhalten.
- Trennt logischen Bremseneingang von effektiver Bremswirkung und verwirft während der Empfangssperre alte Integrator-/Bremszustände. Eine entfallene Anforderung wird beim Zurücktabben nicht nachgeholt.
- Pause und Parken quittieren die Sperre nicht; manueller Eingriff und bewusster Neustart bleiben erforderlich. Keine behauptete Behebung der Hintergrundübertragung, keine Lockerung der Ausgabe-Watchdogs.
- [Beobachtete Vorfälle, Gegenprüfungen und separater Lenkausfall](REVIEW-0.4.5.md).

## 0.4.4 – gebundene Zahlungsinteraktion und Eingabekontext

- Ergänzt eine einmalige automatische Bestätigung an unterstützten manuellen Mautspuren. Stillstand, gerichtete Modellzahlzone, exakte eigene Schranke, aktuelle Route und bestätigter Spielvordergrund sind Voraussetzung. Automatische Abrechnung bleibt die bevorzugte Spurwahl; unbekannte oder am sicheren Haltepunkt unerreichbare Zahlzonen bleiben manuell.
- Speichert die Versuchsreservierung vor der Ausgabe auf einem unabhängigen begrenzten Hintergrundschreiber. Abbruch, F9/F8 und Neustart am selben Tor erzeugen keinen blinden Wiederholungsversuch. Ein Speicherfehler blockiert nur die automatische Interaktion, nicht den Fahrzyklus.
- Bindet den semantischen Spieleingang an Versuch und ursprüngliche Zeitgrenzen. Der Host prüft den Kontext erneut; alte generische Toggle-Aufgaben dürfen diesen Eingang nicht übernehmen. Öffnung und Weiterfahrt benötigen weiterhin den Nachweis der eigenen Schranke.
- Zeichnet den verifizierten Spielprozess und Fensterkontext auf und trennt Befehle, spätere logische Spieleingänge und effektive Pedalwirkung. Keine behauptete Lösung für Hintergrundfahrt unter TruckersMP und keine Änderung der Pedaleinstellung.
- [Prüfstand, Paketstatus und Grenzen](REVIEW-0.4.4.md). Reale Zahlung und vollständige Durchfahrt mit dieser Fassung sind separat zu prüfen.

## 0.4.3 – Ampelinstanzen und aktuelle Fahrzeugdynamik, installiert

- Berücksichtigt das belegte Spawnflag der konkreten Karteninstanz. Ein ausgeschalteter Ampelkopf ohne passenden Runtime-Actor erzeugt keinen erfundenen Halt. Echte Signale, unbekannte Instanzen und widersprüchliche Actors bleiben konservativ behandelt.
- Ordnet die Ausnahme für Mautschranken derselben gerichteten kanonischen Torgruppe zu, einschließlich mehrerer Schrankenarme. Eine gleiche ID an einem anderen Pfosten, Ort oder Routenabschnitt genügt nicht.
- Bewahrt gültige Beschleunigungsableitungen bei identischen wiederholten Physikframes ausschließlich bis zu ihrem ursprünglichen Ablauf. Widersprüche und fehlende Quellen bleiben unbekannt. Die Gasausgabe prüft die Frist nochmals unmittelbar vor Übergabe und im Host.
- Zeichnet die konkrete Ampelbindung sowie rohe Ampel-/Schranken-Actors getrennt auf. Die frühere Aufnahme war dafür unvollständig.
- [Befunde, Gegenprüfungen und Grenzen](REVIEW-0.4.3.md). Keine zugesicherte Behebung ausbleibender Spieleingaben oder des Hintergrundbetriebs; automatisches Bezahlen an manuellen Mautspuren bleibt offen.

## 0.4.2 – Stillstand, Feststellbremse und tatsächliches Rollen unterscheiden, installiert

- Vereinheitlicht die kleine negative Standtoleranz für Kurvenprofil, Szenenzulassung, Fahrüberwachung und eigene Geschwindigkeitsregelung. Ein winziger negativer Messwert verwirft keine vorhandene Kurvenvorschau mehr.
- Eine frisch gemeldete Feststellbremse bei bestätigtem Stillstand hält Gas und Betriebsbremse neutral. Gewöhnliche alte Halteanforderungen werden aufgehoben; Hindernisse, geschlossene Schranken und Fehler bleiben erhalten und werden beim Lösen neu bewertet.
- Gerichtete Positionsänderungen erkennen langsames Rückrollen trotz kleiner Geschwindigkeit. Vorwärtskriechen entzieht ebenfalls die Parkfreigabe; erst eine über frische Messungen bestätigte Ruhephase gibt sie wieder frei. Doppelte oder ungültige Messungen beweisen keine Ruhe.
- Rückwärtsgang im Stand verlangt manuelle Übernahme, ohne allein daraus eine Notbremsung zu erzeugen. Eine spät eingehende Feststellbremsmeldung sperrt Gas vor der Ausgabe; eine erforderliche Betriebsbremse bleibt erhalten.
- [Aufgezeichneter Vorfall, Gegenprüfungen und Grenzen](REVIEW-0.4.2.md). Noch kein Nachweis einer erfolgreichen realen Anfahrt oder Mautdurchfahrt mit dieser Fassung.

## 0.4.1 – Gasempfang von simulierter Gaswirkung trennen, installiert

- Fehlendes effektives Gas allein löst keine verriegelte Vollbremsung mehr aus. Der Regler unterscheidet den vom Spiel gemeldeten Eingang von der anschließend verarbeiteten Gasstellung. Während einer Wirkungsabweichung steigen weder Gasintegrator noch Pedalanforderung weiter an.
- Ein tatsächlich fehlender Gaseingang sperrt die Beschleunigung mit Übernahmehinweis, erhält jedoch gültige Lenkführung und bestehende Bremsgrenzen. Die Gassperre bleibt auch über Pause/Fortsetzen bestehen und erfordert manuelle Übernahme beziehungsweise einen neuen Start. Kurven-, Verkehrs-, Maut- und explizite Notbremsgrenzen bleiben vorrangig.
- Diagnose und Aufnahmen ergänzen Eingangsgas/-bremse, effektive Kupplung, Motor-/Anzeigegang und Drehzahl. Der letzte historische Auslöser ist reproduzierbar; eine Schaltung oder ein Fensterwechsel als dessen Ursache ist mangels damaliger Zusatzdaten nicht bewiesen. [Prüfung und Grenzen](REVIEW-0.4.1.md).

## 0.4.0 – Eigene Geschwindigkeitsregelung und gerichteter Routenanschluss, installiert

- Eigene Gas-/Bremsregelung statt des nativen ACC-Reglers. Das neue Hostpaket lässt ausschließlich den normalen Plugin-Längskanal zu; alte ACC- und fremde Pedalbefehle werden nicht beigemischt. Fehlerbremsung, Sitzung/Ursprungszeit, manuelle Bremse und F9 bleiben wirksam. Pathfinding und Lane Assist bleiben erforderlich; ACC ist für den Betrieb nicht mehr nötig.
- Eingebauter Spieltempomat muss vor Übernahme aus sein; kein blinder Toggle. Unabhängiger Pedalwächter erkennt einen ausgefallenen eigenen Reglerzyklus auch bei weiter frischer Lenkausgabe. Fehlendes Straßenlimit 0 verwendet vorsichtig höchstens 30 km/h statt Solltempo 0; engere lokale Grenzen bleiben wirksam.
- Ziel bis 100 km/h auf freier Strecke, begrenzt durch konfigurierte Vorgaben, Kurven, Verkehr, Ampeln und Mautbelege. Kleine Abweichungen von einem weichen Tempowunsch können durch Rollen abgebaut werden. Harte Begrenzungen und Haltegründe werden dadurch nicht ausgehebelt.
- Eingelesene Verkehrs-/Parkdaten verwenden die geprüfte WXYZ-/Providerkonvention, streng erkannte leere Slots und frische Quellen. Die Quelle deckt nur ihre begrenzten SDK-Slots ab; keine vollständige Verkehrs- oder Hinderniserkennung.
- Maut-GATE-Freigabe und Ampelbewertung sind getrennt. Ein naher gegenläufiger Torpfosten darf die gerichtete Zuordnung nicht als gewöhnliche Ampel überschreiben.
- Separater normaler Planungshalt auf der belegten Zufahrt: 60 m echte gerichtete Bogenlänge vor dem tatsächlichen Straßenende, damit die bestehende 45 m-Wechselanforderung erreichbar bleibt. Die physische Halte-/Notgrenze und deren 15 m-Puffer bleiben unverändert. Kein neuer Anker hinter dem Lkw und kein Mitwandern bei wiederholten Pending-Frames.
- Vorläufige Manualwahl verwirft eine weiterhin belegbare spätere Autoeinordnung nicht: streng gebundene Topologiekandidaten bleiben nur als Suchraum erhalten, aktuelle Zahlung/Geometrie/Entry werden neu geprüft. Bei allein tempobedingter Ablehnung bleibt eine zulässige normale Vorbereitung aktiv. Geschlossene beladene Geschwindigkeitsplantprobe: 130 m/77 km/h → zulässiger Wechselbeginn bei 61,995 m/19,481 km/h, mit 150 ms Pedalantwort und unveränderten 45-/160-m-Grenzen. Keine vollständige reale Spuränderung oder Durchfahrt belegt.
- Der Ausfahrthandoff prüft den weiteren Anschluss Road→Prefab. Er behält eine bereits exakt verbundene Wahl, sucht andernfalls eine eindeutig passende reale Alternative unter denselben Kontrollknoten oder plant einen zulässigen zukünftigen Nachbarspurwechsel mit genügend Strecke. Änderungen werden vorbereitet und bei geänderter Originalbasis verworfen. Kein Überbrücken von Kartenlücken oder Lenkteleport.
- Unbekannte Zahlungsart wird nicht mehr als manuelle Zahlung beschriftet. Eine tatsächlich gewählte manuelle Spur bei noch nicht freigegebener Automatikzufahrt ist keine Aussage, dass die Anlage nur manuell abrechnet.
- Echte Offline-Kartenprobe: Die 4,499 m-Lücke 357,613 m vor dem Lkw wird aus frischen Kartendaten rekonstruiert. Eine andere vorhandene Folgekurve schließt die Route ohne Spurwechsel. Der vollständige historische Nachfolgeplan war nicht gespeichert; weder diese Rekonstruktion noch die damalige Passage mit manuellem Gas ist ein autonomer Fahrnachweis.
- Am 17. November 2026 um 16:46 Uhr MEZ installiert; die vier Dateien und ihre Sicherungskopien sind unabhängig geprüft. Offline bestanden: 200 Integration, 93 Kernlogik, 223 isolierte Host-/Plugin-Pipeline, 64 Aufnahme und 3 historische Wiedergabeprüfungen. Der zusätzliche Routingrunner besteht 255 teilweise überlappende Assertions; keine Summe daraus bilden. Vier ETS2LA-Dateien gehören zum Update: Plugin, Game, State und LaneAssist. ACC-Datei und Spiel-DLLs bleiben unverändert. Kein automatischer App- oder Assistenzstart. Stand- und Fahrprüfung bleiben offen. Details und Grenzen: [REVIEW-0.4.0.md](REVIEW-0.4.0.md).

## 0.3.22 – Standrauschen, Befehlszeit und zusätzliche Bremsdiagnose, installiert

- 177 Integrations-, 87 Kernlogik-, 146 isolierte Host-/Plugin- und 63 Aufnahmeprüfungen bestanden; kritisches Review und Code-CI abgeschlossen. Am 17. November um 12:12 Uhr MEZ nach bestätigtem Halt und geschlossenem ETS2LA installiert. Vorversion .21 gesichert, Plugin, Backup und sechs Begleitdateien unabhängig geprüft. Start- und Fahrprüfung auf Nutzerwunsch später; kein automatischer App- oder Assistenzstart. Finaler SHA256 `A8F789D79C9CCE3B49F6B273749C104617AA77140E7641E0148A9D570CD8BC96`.
- Winzige negative Geschwindigkeitswerte bis −0,01 m/s erzeugen in der Standard-Szenenprüfung keinen falschen Routenverlust mehr. Echte Rückwärtsfahrt, Hindernisse und fehlende Quellen bleiben gesperrt. Der erste Verzögerungsbeginn vor dem offenen Tor bleibt ungeklärt.
- Lenkbefehle werden mit ihren ursprünglichen monotonen Zeitstempeln bewertet. Speichern und letzte Prüfung vor der Fehlerverriegelung verwenden dieselbe kurze Ausgabesperre; fremde Sitzungsepochen bleiben ausgeschlossen. 150-ms-Lenkfrist, 200-ms-Ausgabezulassung und 350-ms-Anlaufzeit bleiben unverändert.
- Die Diagnose trennt normale und szenenbedingte Halteausgaben, native Tempomatbegrenzungen und rohe Ausgabe-/Empfangs-/Prüfzeitstempel. Fremdbeobachtung mit Versionsprüfung erfolgt ausschließlich auf dem begrenzten Diagnosearbeiter.
- Live-Beobachtung mit .21: erste Maut nach Halt manuell übernommen; separater Straßenstopp der Lenküberwachung; zweite automatische Maut und anschließende Spurführung vom Nutzer ohne Eingriff bestätigt. Keine universelle Durchfahrtfreigabe. [.22-Prüfbericht und Grenzen](REVIEW-0.3.22.md).

## 0.3.21 – Korrektur der numerischen Mautausfahrt-Übergänge, installiert

- 170 Integrations-, 84 Kernlogik-, 120 Host-/Plugin- und 63 Aufnahmeprüfungen bestanden; unabhängiges kritisches Review mit 26 gezielten Assertions abgeschlossen. Finaler Build besteht alle 480 gespeicherten Bewegungsproben und acht zusätzliche Nahtfenster. SHA256 `B6657F0639F59FC1F1F2FB215AA048406042D1C4DAA5DB7C414028C071165CB3`.
- Aufgezeichneten Halt nach der Durchfahrt auf einen identischen Prefab-/Straßen-Endpunkt zurückgeführt: erster Korridorfehler und erster ACC-Bremsbefehl liegen im selben Bewegungsframe. Gerichtete Übergänge erhalten eine begrenzte, koordinatenabhängige Float-Toleranz; echte Lücken und Gegenrichtung bleiben ausgeschlossen.
- Die Standard-Abtastung behandelt numerische Übergänge auch bei der Tangentenbildung. Originalgeometrie und Itemidentitäten bleiben erhalten; der optionale strenge Gespannnachweis wird nicht gelockert.
- Getrennter früher Planer-Pausefehler erhält nur bei soeben bestätigter, weiterhin passender Route und frischen Lenksignalen eine höchstens 200 ms alte Übergangsgrundlage ohne Beschleunigung. Bestätigte SDK-Pause verwirft alte kurze Wiederherstellungstimer. Keine allgemeine Toleranz für verlorene Routen oder lange Pause ohne neue Route.
- Szenendiagnose erhält den konkreten Abtastfehler und zeigt ohne aktuelle Begrenzung keinen alten Haltegrund mehr. Am 17. November um 11:13 Uhr MEZ nach bestätigtem Halt und geschlossenem ETS2LA installiert; Backup der .20, Plugin und sechs Begleitdateien unabhängig geprüft. Kein automatischer Start. Die spätere zehnminütige Fahrt enthielt eine bestätigte erfolgreiche Mautpassage und zwei getrennte Stopps; Einzelbefunde stehen im [.22-Prüfbericht](REVIEW-0.3.22.md). [Übergangsprüfung .21](REVIEW-0.3.21.md).

## 0.3.20 – Standard-Spurführung installiert und bereit, Fahrprüfung ausstehend

- 166 Integrations-, 82 Kernlogik-, 120 isolierte Host-/Plugin- und 63 Aufnahmetests sowie sechs frühe FR-Kartenfälle bestanden; unabhängiges kritisches Review abgeschlossen. Kein Live-Fahrnachweis. Finaler Plugin-SHA256: `87050FC31939A896C83288E76E8C91046818B6D399374E2078D4F6D0F87B12FE`.
- Am 17. November um 10:35 Uhr MEZ mit bestätigtem ETS2LA-Ende installiert; alte .19-Datei gesichert, neues Plugin und alle sechs unveränderten Begleitdateien unabhängig hashverifiziert. Code-CI für `8f11deb` erfolgreich. Nach Nutzerstart und Einschalten der vier Module sind geladene .20, Standardmodus und Startbereitschaft bestätigt; Assistenz und Fehlerbremsung aus. Aktive Fahrprüfung bleibt offen.
- Normale Spurwahl und lokale Fahrentscheidung benötigen keine genaue Ladungs-/Achsfreigabe mehr. Benannter Standardkorridor 4,5 m hoch und 3,4 m breit, ausdrücklich kein gemessenes Ladungsmaß. Detaillierter Gespannschutz bleibt optional; bestehende Einstellungen wechseln standardmäßig zur Standardführung.
- Zahlungs-/Anbauteilmetadaten bleiben unabhängig von einem fehlenden Detailprofil lesbar. Nachgelagerte Geometriefehler werden nicht als freie Spur interpretiert.
- Lokale Hindernisprüfung bezieht Spielprozess und Fahrzeugpose direkt aus der rohen nativen Messung. Frische/Abdeckung und tatsächliche Hindernisse bleiben erforderlich; eine unbekannte Yacht löst allein keinen Gespannhalt aus.
- Schranken-, Tracking-, Ausfahrts- und Übernahmeschutz bleiben getrennte Bedingungen. Grenzen und Nachweisumfang: [Review 0.3.20](REVIEW-0.3.20.md).

## 0.3.19 – installiert und geladen, Startfreigabe offen

- Gerichtete Suche im belegten freien Navigationsgraphen statt ausschließlich kürzeste Verbindung suchen und anschließend verwerfen. Eine längere freie Verbindung zum selben Tor kann berücksichtigt werden. Der konkrete Fall zu Auto-Tor 15 scheiterte an `SmallVehicles` trotz freier physischer Profilmaske; SCS beschreibt dieses Metadatum als [KI-Eignung](https://modding.scssoft.com/wiki/Documentation/Tools/SCS_Blender_Tools/Locators/Prefab_Locators#Allowed_Vehicles). Eine Ausnahme ist nur für bestätigte automatische Tore mit abgeschlossenem vollständigem physischem Nachweis vorgesehen. Geprüfte Negativfälle sind `PlayerOnly`, unbekannte Zahlungsart und eine physisch blockierte automatische `SmallVehicles`-Spur; Gegenrichtung, fremde Tore und ungeklärte Modellmasken bleiben ausgeschlossen.
- Erneute Prüfung einer zunächst manuellen Auswahl für einen sicheren Wechsel auf eine später belegte automatische Spur, nur bei ausreichendem gemeinsamem Zufahrtsrest und ohne laufende Spuränderung. Ein fehlender oder zu später Nachweis lässt die bisherige Auswahl bestehen.
- Je Spur werden Auswahl-/Ausschlussgrund, blockierte Kurven und Modell-/Profilrevision mit dem tatsächlichen Routen-/Fahrzeugkontext diagnostizierbar. `laneAttemptContext` enthält den späteren Suchversuch; ein gescheiterter Versuch überschreibt `laneDecisionContext` der gewählten Spur nicht. Die Recorder-Statusauswahl nimmt die drei neuen Diagnosefelder auf.
- Aufnahmehelfer behält `InvalidGateData`, ergänzt aber Slot, ID, Originalzustand beziehungsweise rohe Positionsbits. Keine Erweiterung der gültigen Schrankenzustände oder Änderung des Spiel-/Assistenzcodes durch diese Werkzeugkorrektur. 63 Aufnahmetests bestehen. Die 1.463 betroffenen Zeilen der bisherigen Aufnahme erlauben ohne gespeicherte Rohwerte keine rückwirkende Ursachenbestimmung.
- .19 ist installiert, unabhängig hashverifiziert und nach Start durch den Nutzer geladen: 155 Integration, 82 Kernlogik, 120 isolierte Host-/Plugin- und 63 Aufnahmetests bestanden; SHA256 `766EC8EEBF337E493A8E8EAF218B42A1BB3A74182FC85DB8D3E5F9D806547A3F`, MVID `e270c69c-2524-4731-9e72-a6980fe14f8d`. Paketmanifest-SHA256 `B06D04EAD4830F8FB2991DF86865F0F7BC3DA9059853600210E586E84C0D6327`; Code-CI `b2df1f6` grün. .18-Plugin gesichert, sechs Baseline-Dateien unverändert. [Umfang und offene Schritte](REVIEW-0.3.19.md).
- Sechs frühe FR-Produktfälle auf der echten 325,55-m-`RoadList` mit rekonstruierten Positionen auf Lane −2, erlaubtem Spurwechsel und Profil 4,4 × 3,4 m bestehen: 50/85/100 km/h bei jeweils 200/300 m Rest wählen Auto 15, wenden einen freien Pfad mit Eingangsziel −1 und Ausgang 1 an. 71,31 ms kalt, danach 15,00–18,24 ms. Auto 16 bleibt physisch gesperrt; die historische späte 49,86-km/h-Konfiguration kann weiter Manual 6 wählen. Keine geheilte historische Durchfahrt oder Zusage für sämtliche Länder.
- Das Update ersetzte nur das Plugin auf der exakt geprüften .18-Host3-/SDK-/Geometriebaseline; alle sechs unveränderten Dateien bleiben hashgebunden und mussten nicht erneut ausgetauscht werden. Nur ETS2LA musste geschlossen sein, das Spiel konnte offen bleiben.
- Frischer Status: .19, vier Module geladen/eingeschaltet, Stillstand mit Feststellbremse, Assistenz und Fehlerbremsung aus. Der Nutzer bestätigt einen Halt abseits der geplanten Fahrspur; die Routen-Startblockierung passt dazu und belegt keine .19-Regression. Die separate Aufnahme eines Tieflader-/Yachtgespanns benötigt noch einen belegten Auflieger-/Ladungsnachweis. Keine Stand-F8-Probe oder Fahrfreigabe; geladen wird nicht als fahrbereit ausgegeben.
- Die berichtete Kurvenkollision ist ohne Aufnahme davor nicht ursächlich belegt; ein späterer Snapshot beweist keinen Lenkausfall. Der Vollgespannsweep gilt für Mautbereiche, nicht allgemein für Straßenkurven. In der Nachaufnahme (1.625 Zeilen/179,93 s) gab es keine Kabinen-/Chassisschadensänderung, keine Luftwarnung und keine Faultmeldung in 176 frischen Statusständen (13 Assistenz an, 163 aus). Dies ist keine Rekonstruktion des vorangegangenen Unfalls oder lange Assistenzprüfung.

## 0.3.18 – lokal installiert, neue Liveprüfung ausstehend

- Der installierte Laufzeitbuild hat SHA256 `35542A74D0786DD459C686E0CE1D1B6EEAED8BAE59C8C1698A9235EAEB087CE5`, MVID `4c065272-6d4e-4c65-afff-cb931b24a0bb`. Alle sieben Dateien wurden mit geprüfter Sicherung ausgetauscht und ihre Zielhashes anschließend bestätigt. Einstellungen/Eingabebelegungen blieben unverändert; keine Apps wurden gestartet. Frühere Sechs-Datei-Pakete einschließlich v20/v22 bleiben historisch gesperrt. Neue Stand-/Fahrprüfung beim nächsten Start; eine neue Livefahrt ist nicht erfolgt.
- Die echte Pluginregelung nutzt jetzt getrennte lokale Truck-/Trailer-Hüllen auf der finalen gerichteten Route, mit echten Hooks und gemessener fester Achsgruppe. Dieselben Zellen gehen in statische BVH-/begrenzte Mover- und dynamische Körperprüfungen ein. Die große All-Artikulationskugel wird nicht mehr als Kollisionskorridor verwendet. Nominale lokale Planungsmaße ersetzen globale momentane Artikulationsbreite; sie sind kein Durchfahrtsrecht.
- Das Vorwärts-No-Slip-Modell gilt nahezu eben bis 20 km/h unter der gemeldeten primären SCS-Konfiguration `chain_type=single` und einem beobachteten Auflieger mit passenden nativen Messungen. Fünf von zehn möglichen Slots beweisen keine universelle Abwesenheit weiterer Anhänger. Radgeometrie wird unabhängig vom Provider kopiert; Kontakt-/Liftvertrauen und Modellabweichung werden getrennt überwacht. Bei unveränderter Achsgeometrie gilt höchstens 600 ms Kontaktübergangszeit. Unbekannte Bewegung wird lokal behandelt und nicht zu gemessener Physik erklärt.
- Die akute Notbremsprüfung verwendet aktuelle ungepolsterte gemessene Truckboxen statt des pauschal mehrfach verbreiterten Korridors. Der isolierte Nachbarspurfall mit 46 cm Abstand bleibt frei, eine zentrale Box bei 20 km/h löst weiter AEB aus. Der Modell-Init läuft als reine Hintergrundaufgabe; F8 wartet haltend auf Bereitschaft.
- Ein vorbereiteter Korridor wird einmal pro `Evaluate` zwischen Prüfungen geteilt. 18 synthetische Assertions mit 714 Samples und 3.072 Abfragen messen 3,256 ms und 0 zusätzliche Bytes nur in der Abfrageschleife; Erzeugung 0,989 ms. Die neue finale FR-Produktgegenprobe desselben Siegels misst Warmup 572,76 ms bis Ready, erstes Modellprüfpaar 111,19 ms / 2,279 MB / kein GC, warme Paare maximal 120,13 ms. Das ist gegenüber dem alten 206,763-MB-Fall deutlich kleiner, aber kein Null-Allokations- oder Laufzeitversprechen für das ganze Plugin oder alle Modelle. Quelle/gesamter Ausgabepfad und Livefahrt sind nicht Bestandteil dieses Offlinefalls.
- Native `MautGeometry.dll` 0.3.18: SHA256 `A91E2C5354D03DA0E07E79E9AB732443EE277B995297293FB65D93D3A96E5262`, unveränderte 256-Byte-ABI und zwei ursprüngliche Exporte. QPC steuert mindestens 16 ms Abstand ohne Catch-up; präzise UTC bewahrt den Capturebeginn trotz langsamer Getter. 31 isolierte Fake-Host-Prüfungen bestehen. Quellenphasen werden als gerichtete Translationssegmente je Körper abgedeckt, nicht als isotrope Zusatzbreite.
- Der neue getrennte Installationsweg umfasst sieben Dateien: vier Hostbibliotheken, Plugin, Szenen-SDK und Geometriebeobachter. Ein separat versiegeltes Runtimegate, alle sieben geprüften Backups und alle sieben Stages vor dem ersten Commit sowie unmittelbare Prozessguards sind verpflichtend. Die alte Sechs-Datei-Sperre wird nicht gelöscht. Installer starten weder Apps noch Assistenz oder Fahrtests.
- Frühe Maut-Spurplanung trennt belegte Karten-/Zahlungs-/Freiraumtopologie von der tatsächlichen SDK-Schrankenzuordnung. Fehlende Torproben im kurzen SDK-Sichtfeld begründen im Bremshorizont einen normalen Halt vor dem Kartenanker. Eine globale Tor-ID, alte Nutzer-Zahlarten oder bloße Nähe erteilen keine Bindung.
- Native Modellboxen von Zugmaschine und einzeln positioniertem Auflieger werden mit Telemetrie-Identität, aktuellem Navi-Bezug, echten Kupplungspunkten und gesondert geprüften unterstützten Ladungs-/Zubehördefinitionen verbunden. Kein pauschales 6 × 3-m-Fahrzeugprofil; unbekannte Haupt-Ladungsmodelle, Mehrfachauflieger und nicht belegte Kopplungen bleiben ohne Freigabe. Die native CargoCoverageKnown-Eigenschaft wird nicht auf true gesetzt.
- Statische Kindmodelle, Varianten, geprüfte reine Lichtdarstellungen und begrenzte bewegte Modellteile werden getrennt behandelt. Eine ergänzende spielseitige Quelle liefert tatsächlich gelesene geparkte Körper samt Aufliegern, Bereichsstatus, Quellepoche und Capture-Zeit. Sie ist keine vollständige Verkehrs- oder SpawnFinished-Erkennung; unbekannte Bewegung kann weiter zum vorsichtigen Halt führen.
- Bewegte Modellteile verwenden einen gemeinsamen Mittelpunkt für die Rotationshülle; 237 lokale Prüfungen und der verstärkte LOD-Gegenfall bestehen. Generische `class: flare` nutzt denselben vollständigen Render-/Schema-/Geometrienachweis wie andere reine Lichtdarstellungen. Das ist kein pauschaler Namensfilter und keine Freigabe unabhängig von den lokalen Laufzeitprüfungen.
- Ein erreichter normaler Halt bekommt eine gemeinsame physische Haltebremse. Branch-, Scene-, Schrankenbindungs- und Freiraumgründe dürfen einander beim Wegfall eines Grundes nicht freigeben. Eine stärkere Fehler- oder native Bremsung bleibt erhalten. Eine neu ausstehende Freiraumbescheinigung hält die Ausgabe an, ohne allein bereits bewiesene Routentopologie in einen späten neuen Spurwechsel umzubauen.
- Hostrevision `0.3.18-host-3` bindet alle drei nativen Publisher und den Pause-Helfer an Sitzungskennung und Tickbeginn. Einlieferung und Ausgabe verwerfen über 200 ms alte Ergebnisse. Die gemeinsame Halteleihe ist an ihren Ursprung gebunden, nicht an eine verspätete Veröffentlichung; F9 entzieht alte Berechtigungen.
- Ein manueller Modell-Zahlhaltebereich richtet sich nach nachgewiesener Nav-Kette, Weltgeometrie und Triggerintervall. Das Host-Metadatum Backward allein verwirft eine tatsächlich vorwärts verbundene Kette nicht mehr. Echte Umkehrung, fehlende Next-Verbindung oder unpassende Naht bleiben abgelehnt.
- Der erneute volle Prüflauf besteht 152 Integrations-, 82 Kernlogik- und 120 Host-/Pluginprüfungen ohne Fehler; darin sind 37 echte Plugin-Commit-Prüfungen gegen genau das aktuelle Siegel enthalten. Historische v22- und 151/81/120-Zwischenstände werden nicht umetikettiert. Die SDK-Standalone-Suiten haben unverändert 33 + 18 Prüfungen. Die gemeinsame reale Stand-/Fahrprüfung bleibt offen.
- Die französische Anlage ist statisch untersucht. Die aufgezeichnete Position mit etwa 14,44 m Zufahrtsrest war zu spät für den benötigten Spurwechsel. Eine frühere virtuelle Position mit ungefähr 67 m Rest bleibt eine Offline-Hypothese. Eine weitere Niedrigtempo-Gegenprobe im v16-Zwischenstand mit null SDK-Toren plante etwa 198 m vor dem Tor die passende automatische Spur; daraus folgt weder Live-Bindung noch ein realer Durchfahrtsnachweis. Hintergrundpedale in TruckersMP, allgemeine Baustellen-/Schildvermeidung und vollständige zukünftige Schleppkurven bleiben offen. [Umfang und kritischer Review](REVIEW-0.3.18.md).
- Die v20-Frankreichprobe erlaubt statisch 3,4 m Breite auf automatischer Spur 10; bei 3,5 / 3,6 / 4,0 m bleibt feste Modellgeometrie an Kurven 51/52/69/71 blockierend. Dies ist kein Mover-Mittelpunktfehler; eine vorübergehend positive Zwischenmeldung wurde korrigiert.

## 0.3.17 – lokal installiert, Gesamtfahrverhalten nicht freigegeben

- Kleine Wunschtempo-Korrekturen können auf belegter freier Strecke rollen. Tatsächliche Pedalneutralität wird im nativen Tick geprüft; erkannte Fahrzeug-, Kurven-, Semaphoren- und Mautgrenzen bleiben vorrangig. Fehlende Daten und unsichere Routen erteilen keine Freigabe.
- Separate lokale Ausgabekorrektur für zwei nachgewiesene native Fehler: ausgelassene Pedalveröffentlichung bei sehr langsamer Fahrt und stehengebliebene Achsen bei abgelaufenem Besitzer. Sitzungskennungen verhindern verspätete Befehle nach manueller Übernahme.
- Weiche Mauthaltepunkte werden von der geschützten Grenze getrennt. Kurvenvorschau bleibt bei passenden Routenupdates verfügbar und verlangt tatsächlich abgedeckte Reichweite. Wunschtempo beachtet Begrenzungen und spätere manuelle Eingaben. Speichern erhält übrige Optionen.
- 104 Integrationsprüfungen, 57 Logikprüfungen und 51 isolierte native Prüfungen bestehen. Sie ersetzen keine reale Gesamtprüfung. Vollständige französische Kindobjektgeometrie, ein gespeicherter Anschluss und Hintergrundpedale bleiben offen. [Nachweise und Review](REVIEW_0317.md).

## 0.3.14 – Vorabprüfung, einheitliche Vorschau und entkoppelte Statusgeometrie

- Vorhandene Routensignale lösen eine begrenzte Modellvorbereitung aus, bevor SDK-Schranken eintreffen. Das wählt keine Spur und erteilt keine Öffnungsfreigabe. Fehler der optionalen Vorbereitung werden diagnostiziert, statt allein eine Fehlerbremsung auszulösen.
- Die tatsächliche Startvorschau verwendet denselben Router samt Geometrieprüfung wie die aktive Assistenz. Die periodische Statusgeometrie läuft mit unveränderlichen Punktdaten auf dem Diagnosearbeiter; native Projektionen werden dort nicht vorgetäuscht. Gehaltene Statusgeometrie darf ACC, Routenempfang und F9 nicht aufhalten.
- Eindeutige gerade manuelle Modelltrigger können einen früheren Zahlhaltepunkt liefern. Der bestehende sichere Schrankenabstand wird nicht verkleinert; fehlende Metadaten starten kein automatisches Vorrollen. Komplexe, skalierte oder mehrdeutige Trigger bleiben ohne Ziel. Keine automatische Zahlung und keine allgemeine Behebung aller Zahlzonen.
- 84 Integrations- und 46 Logikprüfungen bestehen; beide griechischen Offline-Spurwahlen bleiben gültig. Einzelheiten und Grenzen: `VERSION_0314.md`. Baustellen-Vorausschau und Hintergrund-Pedale bleiben offen; noch keine Fahrprüfung mit dieser Fassung.

## 0.3.13 – Länderübergreifende Modellauflösung und Kollisionsabschaltung

- Der Prefab-Definitionskatalog ersetzt den festen Modellordner. Die Bestandsaufnahme findet 16 zuvor ausgeschlossene Definitionen unter 81 Mautmodellen. Ältere manuelle Start-/End-/Einmal-Zahlpunkte und automatische Auslösung werden aus geprüften `tollgate`-Units ausgewertet, einschließlich SII-Token-/Linkdarstellung und Spezialauftragsstatus. Unbekannte Attribute oder Kindmodelle, fehlende Definitionen und widersprüchliche Doppeldefinitionen geben keine Spur frei. Manuelle Spuren werden nicht zu AutoPASS erklärt.
- Eine Kollisionssignatur aus abruptem Geschwindigkeitsverlust und gleichzeitig neuem Kabinen-/Fahrgestellschaden schaltet Assistenz aus und fordert unabhängig von der Routensperre Bremsung an. Physische Bremse und F9 lösen sie und verwerfen wartende Meldungen. Dies verhindert keinen ersten Aufprall und ist keine Baustellen-Hinderniserkennung. Die gesicherte alte Unfallaufnahme enthält keinen Schadenverlauf; die neue Diagnose bewahrt ihn mit tatsächlichen Spielpedalen und Simulationszeit.
- Ein lokales Inventurwerkzeug erfasst vorhandene SDK-Aufzeichnungen und Fehlerberichte, ohne veralteten Status, nahe Nachbarspuren oder fehlende Länderangaben als erfolgreiche Mautfahrt auszugeben. Übersicht und Grenzen stehen in `TOLL_COVERAGE.md`.
- 81 Integrationstests, 45 unabhängige Logiktests und drei Inventurprüfungen bestehen; die GitHub-Prüfungen einschließlich der 44 Aufnahmeprüfungen sind ebenfalls erfolgreich. Die beiden griechischen SDK-Fälle wählen wieder eine verbundene automatische Spur. Am 14. November bei geschlossenen Anwendungen mit Sicherung der 0.3.11 installiert und per Version/Prüfsumme bestätigt. Noch nicht gestartet oder im Spiel geprüft; keine Änderung an Pedalmodus, globalen Einstellungen oder Abschaltschwellen.

## 0.3.12 – Modellvarianten und breite Mautdurchgänge

- Aktivierte Modellteile werden zusätzlich zur Variantenmaske ausgewertet. Sichtbare statische Dreiecke sperren einen betroffenen Fahrkorridor; Zahlungsart und Durchfahrtsgeometrie bleiben getrennt. Der Korridor ist 3 m breit und 6 m hoch, kein gemessenes Fahrzeugprofil. Unbekannte Kindmodelle, animierte Geometrie und unpassende Archivquellen bleiben ohne Freigabe.
- Zwei Flügel einer breiten Schranke werden anhand der physischen Spur zu einem Durchgang zugeordnet. Beide getrennten Komponenten müssen offen gemeldet werden. Eine fehlende Komponente, widersprüchliche Repliken oder verlorene Zuordnungsdaten können keine Öffnung vortäuschen; der Zusammenhang bleibt beim Routenübergang erhalten.
- Modellarbeit läuft begrenzt außerhalb des Fahrzyklus und mit eigenen Archivlesern. Gespeicherte Spuren und bereits angewendete Routen umgehen die geometrische Prüfung nicht. Zwei griechische Kartenfälle wählen mit aufgezeichneten SDK-Eingaben jetzt selbst eine verbundene automatische Zahlspur. Ein Fall ist nur bis zum Straßenstück nach der Anlage rekonstruiert; eine echte Durchfahrt bleibt offen.
- 77 Integrationstests, 44 unabhängige Logiktests und 44 Aufnahmeprüfungen bestehen. Details und Einschränkungen stehen in `VALIDATION.md`. Keine feste griechische Spur- oder Stationsnummer im Auswahlcode.

## Beobachtungen und Konfiguration vom 14. November

- Die Aufnahme bewahrt endliche negative Luftdruck-Rohwerte getrennt von der weiterhin ungültigen Druckmessung. Warn-/Notfallflags bleiben unabhängig erhalten; ein neuer Test prüft die vollständige Speicherung. 44 Aufnahmeprüfungen bestehen. Der spielinterne Tempomat-Sollwert wird künftig ebenfalls erfasst.
- Laufzeitbeobachtung von 0.3.11, wiederholter Luftdruckverlust bei automatischer Bremsung und die Ablehnung einer griechischen Mautanlage mit aktivierten Zusatzteilen sind dokumentiert. Der Teilenname „bar3“ allein belegt keinen Höhenbalken; diese anfängliche Einordnung wurde durch die tatsächliche Geometrieprüfung ersetzt.
- Die lokale Konfiguration nutzt nach Sicherung und Neustart wieder die Standard-Pedalausgabe. Der beobachtende Vergleich zeigt wesentlich geringeren Luftverbrauch bei automatischer Bremsung, vier Minuten oberhalb der Warnschwelle und bestätigte manuelle Übernahme. Eine beim Heraustabben wirkungslose Standard-Bremsausgabe ist ebenfalls aufgezeichnet und vom Fahrer zeitlich zugeordnet. Damit ist nur eine vorläufige Abhilfe für Vordergrundfahrt belegt; Hintergrundfahrt bleibt offen.

## 0.3.11 – Zeitbezug der Diagnose und getrennte SDK-Ausfahrtmessung

- Ein während des Fahrzyklus eintreffender Steuerbefehl konnte einen negativen Diagnose-Alterswert erzeugen, weil der Bezugszeitpunkt vom Zyklusbeginn stammte. Die Status- und Bewegungsaufzeichnung lesen jetzt ihre Befehlsreferenzen vor dem zugehörigen Zeitbezug. Der neue deterministische Integrationstest scheitert vor und besteht nach der Korrektur. Keine Änderung der Lenk-/Bremsentscheidungen oder Abschaltschwellen.
- Die Auswertung kann eine Weiterfahrt nach normaler Freigabe des Schrankenschlüssels separat aus SDK-Positionen nachweisen. Sie verlangt beobachtete Anfahrt und Passage, aktuelle gesunde Status-/Quellwerte und kontinuierliche positive Bewegung. Gegenrichtung, Rückwärtsfahrt, Ebenenwechsel, Positionssprünge und Datenlücken beenden die Zuordnung. Eine eingefrorene Tor-Ebenenprojektion wird weder als Routenentfernung noch als vollständige Fahrfreigabe ausgegeben.
- 73 Integrationstests, 44 unabhängige Logiktests und 43 Aufnahmeprüfungen bestehen. Alte Aufnahmen mit ungültigen Diagnose-Alterswerten werden nicht geglättet, um einen Nachweis zu erzwingen. Die strenge Ausfahrtprüfung bleibt für die betroffene 0.3.9-Aufnahme ohne Treffer; Fahrerbestätigung und unabhängige Bewegung sind getrennte Nachweise.
- Bei beendetem Spiel und ETS2LA mit Rücksicherung der 0.3.10 installiert und Prüfsumme verifiziert. Noch nicht neu gestartet oder im Spiel geprüft. Die zuvor gesicherte lokale Einstellung `WidenTurnsWithTrailer = false` bleibt erhalten.

## 0.3.10 – Diagnoseausgabe vom Steuerungspfad getrennt

- Ein blockierter Diagnoseschreibzugriff hielt bisher die gemeinsame Sperre für Fahrzyklus, ACC-Aufruf und Routenzustellung. Der gezielte Test reproduziert das Scheitern vor der Korrektur. JSON-Serialisierung und Schreibzugriffe für Status, Routenfehler, Startprobe und Fehlerbericht laufen jetzt auf einem separaten Schreiber; auch der einmalige Kartenbericht wird dort erzeugt.
- Fünf feste Berichtplätze begrenzen die Warteschlange. Pro Bericht bleibt nur die neueste wartende Momentaufnahme erhalten; es gibt einen aktiven Schreiber. Abschalten wartet nicht auf eine blockierte Datei. Wiederanschalten verwendet dieselbe Warteschlange, damit ein alter Schreibauftrag keinen neueren überholen kann. Diagnosefehler verändern die Fahrfreigabe nicht. Geometrieprüfungen für Momentaufnahmen bleiben im Steuerungspfad; diese Änderung beseitigt nicht jede mögliche Ausführungspause.
- 72 vollständige Integrationstests, 44 unabhängige Logiktests und 27 Aufnahmeprüfungen bestehen. Der Blockadetest prüft Fahrzyklus, ACC, Routenzustellung und F9-Übernahme, bevor der Diagnosespeicher wieder freigegeben wird. Weitere Tests prüfen Zusammenfassen, Schreibfehler und Deaktivieren/Wiederaktivieren.
- Rein lesende Fahraufnahmen enthalten zusätzlich SDK-Simulations-/Renderzeit und fünf Schadenswerte. Nicht endliche oder unzulässige Schadenswerte werden ausdrücklich markiert. Die Felder wurden mit dem installierten 1.62-Telemetrieleser abgeglichen und am laufenden Spiel gelesen.
- Am 13. November nach Ende der Beobachtung bei beendetem Spiel und ETS2LA installiert und per Dateiversion und Prüfsumme verifiziert; noch nicht neu gestartet oder im Spiel als 0.3.10 geprüft. Die Bremsung vom 13. November bei etwa 47 km/h folgt auf 0,204 s ohne frischen Lenkbefehl. Die unabhängige Aufnahme zeigt auch stagnierende Spielwerte und eine eigene Beobachtungslücke; die Ursache ist damit nicht eindeutig. Vorhandene Lkw-Schäden wurden vom Fahrer bestätigt, waren in der damaligen Aufnahme aber nicht enthalten. Keine Änderung der Abschaltschwellen und kein Nachweis einer vollständigen Mautdurchfahrt.

- Die unabhängige Aufnahme erfasst jetzt auch Luftdruck, fahrzeugspezifische Warn-/Notfallschwelle und die tatsächlichen Warnflags in der ursprünglichen SDK-Skala. Einheiten werden nicht geraten. Vor Beginn dieser erweiterten Aufnahme liegende Stopps können damit nicht rückwirkend erklärt werden.
- Ein gesonderter Fehler nach der Reparatur meldet 0,167 s ohne Lenkbefehl und gleichzeitig die native Abstandsablehnung. Eine lokale Rekonstruktion der verifizierten vorderen Kurvengeometrie überschreitet mit dem aufgezeichneten Zusatzversatz die native Grenze, ohne Zusatzversatz nicht. Bei beendetem ETS2LA wurde nur `WidenTurnsWithTrailer` in der lokalen Spurhilfe-Einstellung ausgeschaltet und die vorherige Datei gesichert. Das ist eine lokale Konfigurationskorrektur mit noch ausstehender Fahrprüfung, keine pauschale Änderung fremder Einstellungen durch das Plugin.

## 0.3.9 – Wiederherstellung nach ETS2LA- und Spielupdate

- Die Routenprobe akzeptiert zusätzlich den geprüften Ereignisbus von ETS2LA 2026.11.5297. Pathfinding und PathLib müssen weiterhin exakt den bereits geprüften Fassungen entsprechen; unbekannte Host-Builds bleiben gesperrt.
- Ein neuer Integrationstest verwendet den tatsächlichen Host-Ereignisbus und prüft synchrone Zustellung, Aufrufreihenfolge, Abonnementänderungen während eines Rückrufs und die konkrete Kombination mit Pathfinding. 69 vollständige lokale Prüfungen bestehen.
- Am 13. November mit dem offiziellen SDK 1.62 wiederhergestellt: Plugin 0.3.9 geladen, Spielverbindung aktiv und alle vier Module eingeschaltet. Die erste Live-Startprobe wurde am Kartenanschluss abgelehnt. Nach manuellem Wechsel auf den nächsten Straßenabschnitt erfolgte die automatische Startfreigabe; F8 aktivierte Full Self-Driving mit frischen Lenksignalen und etwa 365 m verbundener Route. Eine vollständige Mautdurchfahrt ist noch nicht bestätigt. Keine Änderung an Brems- oder Trackinggrenzen.

## 0.3.8 – Bremsausgabe vor nativer Diagnose

- Bei fehlender Lenkausgabe werden Assistenzabschaltung und Fehlerbremsung jetzt vor dem nativen Diagnoseaufruf ausgeführt. Der lesende Aufruf hält die Sperre der Lenküberwachung nicht mehr; weitere Bremsauffrischungen und die physische Übernahme bleiben während einer langsamen Diagnose möglich.
- Eine währenddessen quittierte, beendete oder neu gestartete Überwachung verwirft das verspätete Diagnoseergebnis. Es darf keine bereits gelöste Fehlerbremsung erneut auslösen.
- Ein gezielter Integrationstest hält die Diagnose an, prüft bereits ausgegebene Bremsung, unabhängige Auffrischung und physische Bremsübernahme und lässt den alten Aufruf erst danach zurückkehren. Vor der Korrektur scheiterte der Test an der fehlenden sofortigen Bremsung; danach bestehen alle 68 vollständigen lokalen Prüfungen und 42 unabhängigen Logikprüfungen.
- Die Diagnose bleibt eine nicht atomare Beobachtung und erfolgt nun nach dem Abschalten. Sie beweist keinen exakt gleichzeitigen Zustand des nativen Reglers. Die spontane Lenk-/ACC-Aufzeichnungslücke vom 6. November ist dadurch nicht als behoben nachgewiesen. Abschaltschwellen und Mautregelung wurden nicht gelockert.
- Am 6. November installiert und im Stand geprüft: gezielter Lenkausfall führt zu voller SDK-Bremsausgabe auch bei Spiel im Hintergrund; physische Bremsübernahme und normale Controller-Lenkung bestätigt. Bremsweg unter Beladung und vollständige Mautdurchfahrt bleiben offen.
- Rein lesende Aufnahme kann mit `-IncludePedalOutput` zusätzlich moderne Pedalbefehle und deren Spiel-Plugin-Bestätigung erfassen; Motordrehzahl ist ebenfalls enthalten. 21 Prüfungen der Aufnahmeauswertung und Datenformate. Der Hintergrund-Gasfehler unter TruckersMP wurde damit reproduziert und bleibt ausdrücklich ungelöst.

## 0.3.7 – Pausenzustand und native Lenkdiagnose

- Ein weiterer gesicherter Abbruch zeigt bei rund 51 km/h nur etwa 4 cm Abstand zur zuletzt gültigen Linie und frische Lenksignale. Der native Planer verwarf dennoch die Route; das Protokoll enthält zuvor einen abgeschlossenen Spurwechsel und 4,5 m gemeldeten Abstand. Der konkrete Spurwechselzustand vor dem Leeren der Route fehlt in 0.3.6. Die vorbereitete Fehleraufzeichnung behält deshalb eine getrennte Kopie der zuletzt akzeptierten Route mit Alter, ausgewählten Kurven und versionsgeprüftem Spurwechselzustand. Diese Kopie dient nur dem Nachweis und gibt keine Fahrt bei Routenverlust frei.
- 0.3.6 zeichnete während der Fahrt ausbleibende Lenksignale auf. In einem gesicherten Fall bei rund 35 km/h blieb die Route gültig, während der seitliche Abstand bis auf rund 0,8 m abnahm. Die Überwachung schaltete nach rund 151 ms ohne neuen Befehl ab. Das belegt das Ausbleiben der Signale, aber nicht dessen ursprünglichen Auslöser.
- Die passende Kurve des gespeicherten Kartenverbinders wurde offline rekonstruiert. Der native Lenkpunkt liegt dort ebenfalls rund 0,8 m vom Lkw entfernt. Ein großer Projektionsfehler ist an diesem Punkt damit nicht nachgestellt. Die spätere Routenfortsetzung ist mangels gespeicherter Auswahl aller Kurven nicht vollständig rekonstruierbar.
- Ein davon unabhängiger Fehlalarm wurde vor der Korrektur reproduziert: Bereits gelieferte Pause war im noch beschäftigten Routenthread nicht übernommen. Der unabhängige Lenkmonitor liest Pause und Wiederaufnahme nun aus dem zuletzt gelieferten zusammengehörigen SDK-Bild. Er wartet nicht auf die Routenverwaltung. Wiederaufnahme stellt die Überwachung wieder her; die Grenzwerte für echte Ausfälle bleiben erhalten.
- Bei einem Lenkausfall wird zusätzlich ein rein lesender Diagnoseauszug der exakt geprüften Lane-Assist-Bibliothek erfasst: native Fehlermeldung, letzte Regleranteile, Kurvenversatz, Fehlerstatus der nativen Route und bereits geladene Lenkparameter. Die Diagnose initialisiert keine Einstellungsinstanz; sie liest keine veraltete Dateikopie. Das Vorzeichen des nativen Anzeige-Schiebereglers wird an die protokollierten Steuerbefehle angeglichen. Unbekannte Bibliotheksstände werden nicht mit geratenen Feldnamen interpretiert; Fehler beim Lesen beeinflussen die Schutzentscheidung nicht. Kein Aufruf nativer Steuerzyklen durch die Diagnose.
- Eine ältere Lenkausfallmeldung wird bei einem späteren unabhängigen Routenfehler nicht mehr als aktuelle Ursache weitergeführt.
- 67 vollständige Prüfungen und weiterhin 42 unabhängige Logiktests. Die Diagnose ist gegen den installierten Bibliothekstyp ohne Konstruktor oder Fahrzeugausgabe geprüft. Am Abend bei beendetem Spiel und ETS2LA installiert, Dateiversion und Hash geprüft, 0.3.6 rückgesichert. Noch nicht im Spiel gestartet oder geprüft. Ursprünglicher Kurvenunfall und vollständige Mautdurchfahrt bleiben offen; nächste Prüfschritte in `NEXT_SESSION.md`.

## 0.3.6 – Überwachung der tatsächlichen Lenkausgabe

- Anlass ist ein weiterer Unfall in einer Kurve mit 0.3.5. Der gespeicherte Fehler zeigt knapp 7 m tatsächlichen seitlichen Abstand bei rund 40 km/h; die Fehlerbremsung wurde angefordert. Die vorherige Lenkausgabe wurde noch nicht aufgezeichnet. Ob sie ausfiel oder trotz vorhandener Befehle die Kurve verfehlte, ist nicht abschließend belegt.
- Die Schutzlogik überwacht zusätzlich die frischen Steuerbefehle des nativen Lane Assist. Nach einem empfangenen Lenksignal lösen über 150 ms ohne gültigen Befehl Abschaltung und eine gehaltene Bremsanforderung aus; eine separate 50-ms-Prüfung wartet dafür nicht auf Routenberechnungen. Geradeausfahrt mit Lenkbefehl 0 gilt als gültig. Andere Kanäle und ungültige Befehle halten die Überwachung nicht am Leben.
- Beim Start gilt eine begrenzte Anlaufzeit von 350 ms. Bis zum ersten frischen Lenksignal fordert die ACC-Anbindung Halt an. Pause und reiner ACC-Modus erfordern keine Lenkausgabe. Ein erneutes Lenksignal löst eine Fehlerbremsung nicht selbstständig.
- Physische Bremse und F9 geben eine Fehlerbremsung auch vor dem Ende laufender Routenarbeit frei. Nach manueller Übernahme verworfene Fehlermeldungen dürfen sie nicht erneut anfordern.
- Fehleraufzeichnungen enthalten nun einen begrenzten Verlauf von Position, Richtung, Tempo, Spurabstand, Lenkausgabe und ACC-Befehlen sowie die betroffenen Routenabschnitte. Das soll zwischen ausbleibender Lenkung, falschen Befehlen und einer fehlerhaften Route unterscheiden helfen.
- 63 Integrationstests und 42 unabhängige Logiktests. Neue Fälle prüfen echte und falsche Ausgabeverluste bei beschäftigter Routenverwaltung, dauerhafte Bremsanforderung und manuelle Freigabe. Reale Prüfung der neuen Überwachung sowie Wiederholung des Kurvenfalls und vollständige Mautdurchfahrt noch offen. Keine allgemeine Kollisionsvermeidung oder nachgewiesene Korrektur der ursprünglichen Lenkursache.

## 0.3.5 – Start hinter bereits passierten Signalabschnitten

- Rein lesende Messauswertung an doppelte Schrankenobjekte angepasst: beobachteten 3D-Anker beibehalten, widersprüchliche Zustände und andere Höhenlagen ablehnen. 18 Werkzeugprüfungen bestehen. Keine Änderung der installierten Plugin-DLL dafür erforderlich. Weitere drei Minuten normale Fahrt bis rund 80 km/h ohne aufgezeichneten Trackingfehler oder Fehlerbremsung beobachtet; keine Mautschranke zugeordnet. Fahrnachweise und offene Anforderungen in `VALIDATION.md` getrennt dokumentiert.
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
