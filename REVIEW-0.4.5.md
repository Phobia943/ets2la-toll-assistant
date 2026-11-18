# 0.4.5 – Pedalempfang ohne technische Notbremsverriegelung

## Beobachtung am 18. November

Die normale Testfahrt mit 0.4.4 zeigte zwei getrennte Pedalempfangsausfälle, während ein anderes Fenster im Vordergrund war. Um 09:09:42 MEZ wurden etwa 0,241 Bremse angefordert und anschließend sowohl logischer als auch effektiver Bremswert 0 beobachtet. Um 09:10:48 waren es etwa 0,181, wieder bei Bremswert 0 und gleichzeitig noch etwa 0,290 logischem Gas. Der Nutzer bestätigte das Auftreten nach längerem Heraustabben. Wiederholte alte Diagnoseschnappschüsse nach der Abschaltung zählen nicht als weitere Ereignisse.

Der bisherige Regler machte aus ausbleibender effektiver Bremswirkung nach 0,35 Sekunden einen gelatchten `Fault`. Das Plugin rief `Disarm` auf; der separate Fehlerkanal forderte dauerhaft Vollbremsung und nahm die Lenkfreigabe zurück. Die Vollbremsung beim Zurücktabben ging damit über die ursprüngliche normale Bremsanforderung hinaus.

## Änderung

- Fehlender logischer Pedalempfang sperrt den Vortrieb und bleibt sichtbar, erzeugt allein aber keinen `Fault` und keine Notbremsverriegelung. Auch bereits aktive Halte-, Vollbrems- und Rollzweige schleusen einen Empfangsausfall nicht mehr als technischen Notfall weiter.
- Logischer Bremseneingang und effektive Bremswirkung sind getrennt. Eine fehlende oder nicht endliche Empfangsmessung wird nicht aus effektiver Bremse 0 erfunden. Begrenzte Wirkung friert die Anpassung ein, statt allein daraus einen Empfangsausfall zu erklären.
- Während der Empfangssperre werden Integratoren und gespeicherte Brems-/Beschleunigungswerte vor der aktuellen Planung verworfen. Endet der aktuelle Bremsgrund, bleibt keine alte Bremsanforderung für die Rückkehr ins Spiel bestehen.
- Aktuelle Kurven-, Verkehrs-, Tor-, Stopp- und explizite Gefahrenanforderungen bleiben bremswirksam. Auch Vollbremsung kann aus einer aktuellen solchen Grenze folgen. Regelmäßige normale Pedalbefehle werden weiter veröffentlicht; der unveränderte Ausgabe-Watchdog sieht deshalb keinen künstlichen Reglerausfall.
- Pause und Feststellbremse quittieren die Empfangsstörung nicht. Nach Rückkehr ins Spiel mit Controllerbremse oder F9 übernehmen und erst dann bewusst mit F8 starten. Fokuswiederkehr allein beweist keinen wiederhergestellten Empfang und führt nicht zum automatischen Wiederanfahren.

Damit ist die tatsächliche Pedalübertragung im Hintergrund weiterhin nicht behoben. Der Patch behandelt deren beobachtete Folge. Lenk-/Reglerausfälle, fehlende sichere Streckendaten und bestehende echte Fehlerkanäle werden nicht pauschal abgeschaltet.

## Separater Lenkausfall

Um 09:19:37 MEZ meldete der Lenk-Watchdog etwa 158 ms ohne akzeptierten frischen Lenkbefehl bei weiter aktueller Spieltelemetrie. Vorher waren Assistenz und Route aktiv, die Spielpause aus. Erst nach der Abschaltung wurde im nicht atomaren Native-Schnappschuss „Assists are paused in the application state.“ erfasst. Der exakt installierte Lane-Assist-Code verwendet diesen Text bei `EnableAssists == false`, das unser Watchdog selbst zuvor setzt. Die Meldung belegt daher keine vorherige App-Pause und keinen bestimmten Lane-Assist-Defekt. Ursache der Ausgabelücke bleibt offen; die 150-ms-Grenze wurde nicht geändert.

## Prüfung und Paket

100 unabhängige Logikgruppen und 218 Integrationsgruppen prüfen die neue Trennung. Der tatsächliche Plugin-/Hostlauf besteht mit 304 Assertions. Ein neuer Fall veröffentlicht über mehr als 1,7 Sekunden echte Tick-Befehle durch den geschützten Eingang und prüft den laufenden 150-ms-Watchdog sowie beide Pedalsenken. Fehlender Empfang hält die Lenkung verfügbar; ein entfallener Bremsgrund wird neutral, eine neue echte Gefahr erreicht weiterhin Vollbremsung, deren Ende gibt sie wieder frei.

Zusätzliche synthetische Fälle beginnen mit den beiden gemessenen Bremsanforderungsgrößen und wiederholen den Empfangsausfall über vier Sekunden. Sie sind gezielte Gegenfälle, keine vollständige Rekonstruktion der Fahrt. Die Core-Fälle prüfen außerdem Empfang gegenüber Wirkung, fehlende/ungültige Werte, Pause, Parken und ausdrücklich bestätigten Neustart. Kritischer Gegenreview und rein lesende Installerprüfung bestanden.

Installiertes Plugin: Version `0.4.5.0`, SHA256 `39A3C7482E5791D1C6F67AB7F7F058BFB121EFD9F5D2AD70FCDD8C0138C77727`. Am 18. November wurde nach bestätigtem Prozessende nur die Plugin-Datei ersetzt; das geprüfte Hostpaket `0.4.4-host-1` bleibt unverändert. Neue Datei, Sicherung der 0.4.4 und alle vier Hostdateien wurden anschließend unabhängig per SHA256 geprüft. Keine Anwendung wurde gestartet, das laufende Spiel nicht bedient. Stand-/Fahrprüfung der neuen Fassung und manuelle Zahlung bleiben offen.
