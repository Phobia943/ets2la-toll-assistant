# Review 0.3.21 – Übergang von der Mautausfahrt zur Straße

## Befund

Der Nutzer meldete am 17. November eine gelungene Mautdurchfahrt, gefolgt von Bremsen bis zum Stillstand. In der gespeicherten Bewegung bleiben Routenabstand und Lenksignale zunächst unauffällig. Die beiden gespeicherten Streckenpläne wurden aus ihren tatsächlichen Straßen, Prefabkurven und Fahrtrichtungen rekonstruiert.

Der erste Fehler der Standardkorridor-Abtastung tritt um **09:49:17.733783 UTC** auf, exakt im ersten aufgezeichneten ACC-Bremsframe bei etwa 31,76 km/h. Die Vorausschau erreicht dann erstmals die identischen End-/Startpunkte eines Prefab-Straßen-Übergangs. `TryStandardSlice` behandelt das Nullsegment fälschlich als ungültige Route. Die erste gemeldete Spielbremswirkung folgt um 09:49:17.795266 UTC. Über alle 240 Bewegungsframes je gespeichertem Plan scheitern vorher jeweils 23 Frames an dieser Naht; 217 passieren. Dies stützt die Ursache des Ausfahrtstopps, ohne einen nicht aufgezeichneten Lenkausfall zu behaupten.

Ein späterer, getrennter Fehler entsteht um 09:49:21.554 UTC: Der Planer meldet bereits „Game is paused.“, die Spieltelemetrie noch keine Pause. Das wurde als fehlende Route behandelt und verriegelte die Fehlerbremsung bei etwa 0,51 km/h. Dieser spätere Übergang erklärt nicht den Beginn des Halts.

## Änderung

- Markierte Abschnittsgrenzen dürfen geometrisch identische Punkte enthalten. Winzige Rundungsabweichungen sind nur innerhalb eines aus den tatsächlichen Weltkoordinaten berechneten Float-Präzisionsbudgets zugelassen: höchstens vier Float-Schritte, zusätzlich auf 25 mm begrenzt, mit übereinstimmenden gerichteten Nachbarabschnitten. Es gibt keine pauschale Freigabe seitlicher Sprünge.
- Originalpunkte und Abschnittsidentitäten bleiben erhalten. Echte Lücken, gegenläufige Nachbarn, unmarkierte degenerierte Geometrie und Extrapolation bleiben ausgeschlossen. Die gesonderte strenge Gespann-Abtastung ist unverändert.
- Ein ausdrücklich gemeldeter Planer-Pauseübergang darf nur mit erneut zur Fahrzeugpose geprüftem, nichtkritischem Korridor und frischen Lenksignalen kurz kontrolliert verzögern. Maßgeblich sind höchstens 200 ms seit der letzten akzeptierten Route, nicht das Empfangsdatum eines neuen Pausefehlers. Wiederholte Pausemeldungen erneuern diese Frist nicht. Gewöhnliche Routenfehler erhalten keine neue Toleranz.
- Bestätigte Spielpause verwirft den alten kurzen Wiederherstellungstimer. Eine anschließend neu bestätigte Route scheitert deshalb nicht an der während der Pause verstrichenen Zeit. Eine lange Pause mit weiterhin fehlender neuer Route wird dadurch ausdrücklich nicht freigegeben; die unabhängige Lenkausgabe-Überwachung bleibt unverändert.
- Die Szenendiagnose nennt den konkreten Abtastfehler und entfernt eine alte Haltemeldung, wenn keine aktuelle Maut-Szenenbegrenzung mehr besteht.

## Prüfung und Bereitstellung

Der finale Build besteht **170 Integrations-, 84 Kernlogik-, 120 isolierte Host-/Plugin- und 63 Aufnahmeprüfungen**. Das unabhängige kritische Review besteht zusätzlich alle 26 gezielten Übergangsassertionen. Die Übergangsregressionen laufen auch in der plattformunabhängigen Kernlogik-CI. Im Review wurden die zunächst fehlende Tangentenbehandlung sowie die Randfenster direkt vor/nach einer numerischen Naht gefunden und vor der abschließenden Prüfung korrigiert.

Mit genau diesem finalen Build bestehen alle **480 Bewegungsproben** (240 je gespeichertem Plan) und **acht zusätzlich konstruierte 10-m-Fenster** um die vier tatsächlichen Nähte. Vorher scheiterten 46 Bewegungsproben und sechs dieser acht Fenster. Die Pläne wurden mit gespeicherten Kurven, Richtungen und Spurzuständen neu abgetastet; die neu berechnete Gesamtlänge weicht um etwa 40 µm von der historischen Diagnose ab. Das ist eine reproduzierte geometrische Prüfung, keine Simulation der vollständigen neuen Fahrzeugbewegung und kein Live-Fahrnachweis.

Plugin-Version `0.3.21.0`, SHA256 `B6657F0639F59FC1F1F2FB215AA048406042D1C4DAA5DB7C414028C071165CB3`, MVID `f215b974-8ece-4041-8a63-68d3db278ba8`. Das Hostmanifest und alle sechs Host-/SDK-/Messdateien bleiben unverändert. Die private Vergleichsaufnahme liegt unter `incident-exit-0320-20261117-0952/replay-after-seam-fix-0321.jsonl`; vollständige Aufzeichnungen werden nicht veröffentlicht.

**Installiert am 17. November 2026, 11:13 Uhr MEZ:** Nach bestätigtem Stillstand, ausgeschalteter Assistenz und geschlossenem ETS2LA bestand die erneute Installervorschau. Nur die Plugin-DLL wurde atomar ausgetauscht; die vorherige .20-Datei ist gesichert. Eine unabhängige Nachprüfung bestätigt Pluginversion/Hash, Backup-Hash und alle sechs unveränderten Begleitdateien. Manifest-SHA256 `71F90E518A649CF831DBBDA6D74B4C55B4A8F5B4E402B819079974210E07009D`. Private Nachweise: `maut-0321-install-result.json`, `maut-0321-installed-verified.json`. Während der vorherigen Fahrt gab es keine Installationsänderung. Der Installer hat ETS2LA und Assistenz nicht gestartet; die Live-Fahrprüfung steht aus.

Auch alle drei [GitHub-Prüfungen des Code-Commits 9f1f09b](https://github.com/Phobia943/ets2la-toll-assistant/actions/runs/44062434726) sind erfolgreich abgeschlossen. Das umfasst Kernlogik, Aufnahmetests und die unveränderten nativen Protokollprüfungen. Die örtliche Integrations-/Hostprüfung und die privaten historischen Kartenproben bleiben davon getrennte Nachweise.

## Grenzen

Diese Korrektur betrifft nachweisbare Abschnittsübergänge und den eng begrenzten Pause-Nachlauf. Sie belegt weder alle Mautanlagen noch vollständige Ladungsfreiheit, Baustellenvermeidung oder eine allgemein gelöste Hintergrund-Pedalausgabe. Die aufgezeichneten Karten-/Bewegungsproben bleiben privat; der öffentliche Test verwendet die minimal erforderlichen geometrischen Gegenfälle.
