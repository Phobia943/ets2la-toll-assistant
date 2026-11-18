# 0.4.6 – Bahnübergangscontroller und sichtbare Schranken

## Konkreter Befund

Am 18. November 2026 gegen 09:43 Uhr MEZ hielt 0.4.5 an einem laut Nutzer offenen Bahnübergang ohne rotes Warnsignal. Die Aufnahme enthält zwei SDK-Actors vom Typ `GATE`, ID 0, Zustand 3 (offen), bei `(49255.3, 3.765625, -39110.594)` und `(49259.004, 3.765625, -39125.875)`. Der Helfer meldete gleichzeitig einen unbekannten Ampelzustand am zentralen Punkt `(49257.1, 4.015625, -39118.22)`. Es war ein geplanter Signalhalt, keine verriegelte Fehlerbremsung.

Die nachträgliche unabhängige Kartenabfrage findet Prefab `3005811766396519163`, Modell `dlc_balt_166`. Seine beiden sichtbaren Locators haben jeweils PPD-ID 1. Der unsichtbare Controller hat ID 0. Im Profil `crossing` sind Controller 0 als `barrier_distance` und die sichtbaren Mitglieder als `model_only` definiert; `id_map` ordnet sie alle der Steuerung 0 zu. Ihre transformierten PPD-Positionen stimmen mit den aufgezeichneten SDK-Positionen überein. Der alte Abgleich ID plus höchstens ein Meter um den zentralen Controller konnte diese Gruppe nicht finden.

## Änderung und Grenzen

Ein eigener Profilkatalog wird über unabhängige Archivleser im Hintergrund geladen. Er löst Vererbung und die dokumentierte Wiederholung des letzten Arrayeintrags auf. Ohne bestimmte Länderzuordnung werden nur Profile verwendet, deren Ländervarianten bei Typ, Modellvorhandensein und Controllerzuordnung übereinstimmen. Ungültige, doppelte, leere oder unauflösbare Definitionen erteilen keine Freigabe.

Die Freigabe bezieht sich auf die konkrete Karte und Route: Alle erwarteten sichtbaren Mitglieder müssen unter der umgesetzten Controller-ID an ihren transformierten Positionen aktuell als offen gemeldet werden. Fehlende Mitglieder, unzulässige Typen, widersprüchliche Zustände, überlappende Kartenpositionen und beobachtete zugehörige rote Warnlichter verhindern die Freigabe. Der vorhandene Signalquellen- und Sitzungsschutz bleibt vorgeschaltet. Mehrere Kurvenreferenzen derselben Steuerung verwenden nur den frühesten Routen-Haltepunkt, keinen neuen Haltepunkt mitten auf der Kreuzung.

Der SDK-Zustand 3 belegt den gemeldeten offenen Actor-Zustand. Er ist kein unabhängiger Nachweis ausgeschalteter Warnlampen: Der bisherige Export enthält bei diesen Actors nur den Schrankenstatus. Der genaue Zeitpunkt einer optischen Warnphase gegenüber dem Statuswechsel 3 auf 0 bleibt separat zu prüfen. Ein ausdrücklich gemeldetes rotes Signal bleibt ein Veto. Unbekannte Profile und nicht vollständig sichtbare Gruppen werden nicht ignoriert.

Keine Änderung an der 200-ms-Routenfrist, dem 150-ms-Lenkwächter, der Pedalübertragung, Mautspurwahl oder Zahlung. Die beobachteten kurzen Verarbeitungslücken sind damit nicht behoben.

## Prüfstand und Paket

Die unabhängige Rekonstruktion des tatsächlichen Karten-Prefabs, des installierten Definitionskatalogs und beider aufgezeichneten SDK-Actors erreicht mit der versiegelten 0.4.6 die Gruppenfreigabe für alle sechs Controllerreferenzen beider Straßenrichtungen. Dies ist eine gezielte Karten-/Zuordnungsprobe, keine vollständige historische Fahrwiedergabe oder neue Durchfahrt.

221 Integrationsgruppen, 100 Logikgruppen und 304 isolierte tatsächliche Plugin-/Hostassertionen bestehen. Drei neue unabhängige Integrationsgruppen prüfen Profilauflösung, Gruppenbindung und den Tick-Eingang der Geschwindigkeitsregelung. Gegenfälle umfassen fehlende/duplizierte/fremde Mitglieder, verschiedene Schrankenpositionen trotz gleicher Locator-ID, geschlossene/bewegte Zustände, Warnlichter, ungültige Daten, widersprüchliche Ländervarianten und zusätzliche nicht aufgelöste Signalrollen. Der geschlossene-zu-offenen Übergang im selben aktiven Tick-Prüfstand beendet die Bremse und nimmt den Vortrieb wieder auf. Kritischer Code- und Installerreview sowie Paketvorschau bestanden.

Das Update wird als einzelne Plugin-Datei für den unveränderten Host `0.4.4-host-1` vorbereitet. Noch nicht installiert; reale Durchfahrt und manuelle Übernahme stehen aus.

Paketversion `0.4.6.0`, SHA256 `AAF69631EE06881340EEB89F58F79D2B0F4382FEE35CCDFA6B11E241608D838F`, MVID `f1eaa793-6ce6-45bf-8797-5cfd63f294f8`. Erwartete Ausgangsfassung 0.4.5: SHA256 `39A3C7482E5791D1C6F67AB7F7F058BFB121EFD9F5D2AD70FCDD8C0138C77727`. Installer prüft geschlossene Anwendung, sichere Pfade, kompatible Hostdateien und gesicherte Ausgangsdatei vor einem Austausch.

## Quellenvertrag

Die [SCS-Dokumentation zu Semaphore-Profilen](https://modding.scssoft.com/wiki/Games/ETS2/Modding_guides/1.9#Changes_in_semaphores) beschreibt Länderfallback, Vererbung, Arrayindizierung und die Synchronisierung von `model_only` über `id_map`. Maßgeblich für den konkreten Befund sind zusätzlich die lokal gelesenen Spieldefinitionen, PPD-Positionen und SDK-Aufzeichnungen; eine bloß nahe oder symmetrische Schrankenanordnung genügt nicht.
