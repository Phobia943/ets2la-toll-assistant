# Review 0.3.20 – Standard-Spurführung

## Problem und Umfang

Ein unbekanntes Haupt-Ladungsmodell konnte bisher bereits die Erkennung der Zahlungsart verhindern. Selbst mit gefundenem Weg konnte die spätere Gespannvorhersage wegen fehlender Ladungs- oder Achsdaten einen zusätzlichen Halt verlangen. Die neue Standard-Spurführung entkoppelt beide Stellen von dieser optionalen Detailprüfung.

Die gespeicherte Option `DetailedRigProtection` ist standardmäßig `false`, auch beim Laden älterer Einstellungen ohne dieses Feld. Der ausdrücklich benannte Standard-Planungskorridor ist 4,5 m hoch und 3,4 m breit. Das sind Planungsannahmen, keine gemessenen Yacht-/Ladungsmaße und kein vollständiger Nachweis der Aufliegerbewegung. `CargoCoverageKnown` bleibt unverändert `false`.

## Getrennte Entscheidungen

- Zahlungsart und Modell-Anbauteile werden unabhängig von einem verfügbaren detaillierten Fahrzeugprofil gelesen. Unbekannte Varianten oder nicht prüfbare Modellstruktur erteilen weiter keine Freigabe. Ein erkannter Zahlungsmarker kann einen gescheiterten Geometrienachweis nicht überstimmen.
- Die gerichtete Spurplanung prüft den Standardkorridor gegen statische Modellflächen und begrenzt bewegte Teile. Bekannte niedrige Durchfahrten bleiben ausgeschlossen. Die bisherigen Regeln für automatische Zahlungsart, andere Tore, Gegenrichtung, Anfahrt und Ausfahrt bleiben erhalten.
- Der lokale Standardkorridor verwendet frische rohe Fahrzeugmessungen und aktuelle örtliche Szenendaten. Der Spielprozess wird direkt aus der nativen Messung bezogen, nicht aus einem ladungsabhängigen Nachweis. Fehlende Yacht- oder Achsdaten erzeugen deshalb keinen versteckten Szenenhalt. Fehlende tatsächliche Szenenabdeckung kann weiterhin einen normalen Halt erfordern.
- Die gesonderte Notbremsprüfung verwendet weiterhin einen aktuellen gemessenen Lkw-Körper. Ein vorsorglicher Standardabstand allein gilt nicht als unmittelbar gemessene Kollision. Ein real geschlossenes oder nicht zugeordnetes Tor bleibt ein eigener Haltegrund.
- Der optionale detaillierte Gespannschutz behält seine bisherigen strengeren Anforderungen und Grenzen. Umschalten ist nur im Stand bei ausgeschalteter Assistenz erlaubt; alte Detail-Haltezustände werden verworfen und eine neue Vorbereitung verlangt. Unabhängige Tor- und Routenbedingungen werden danach erneut ausgewertet.

Originale Zeitstempel bleiben maßgeblich. Eine neue frische Probe am Ende der Prüfung darf eine während der Arbeit veraltete Anfangsprobe nicht erneuern. Geometrie und Szene haben getrennte Produzenten-Sitzungen; diese werden jeweils mit ihrer eigenen Folgeprobe verglichen, nicht miteinander gleichgesetzt.

## Prüfung und Bereitstellung

Der vorbereitete Build besteht **166 Integrations-, 82 Kernlogik-, 120 isolierte Host-/Plugin- und 63 Aufnahmeprüfungen**. Alle elf neuen Integrationsgruppen sind enthalten. Das kritische Review hat die anfänglich geerbte NoSlip-Steigungsgrenze und den pro Aufruf verlorenen Bewegungsspeicher beanstandet; beide wurden korrigiert und mit tatsächlichen Plugin-Aufrufen nachgeprüft. Die unabhängigen Gegenprüfungen decken zusätzlich ursprüngliche Quellfrische, getrennte Produzenten-Epochen, veraltete Quellen, F9, alte Detail-Verriegelungen und den Erhalt unabhängiger Haltegründe ab.

Die Gegenprobe mit dem finalen Build auf echten französischen Modell-/Kartendaten liefert bereits bei fehlendem Detailprofil alle acht Zahlungsarten, ohne damit die strikte Detailfreigabe zu erteilen. Mit dem ausdrücklich gesetzten Standardprofil 4,5 × 3,4 m ist Auto-Tor 15 über `[1,36,30,9,10,11,12,13]` frei; Tor 16 bleibt wegen Kurve 72 ausgeschlossen. Sechs rekonstruierte frühe Fälle mit 200/300 m Zufahrtsrest und 50/85/100 km/h wählen Auto 15, wenden die Route ohne Fehler an und behalten Einfahrt −1 und Ausfahrt 2→1. Erster Routing-Aufruf 71,47 ms, weitere 10,99–21,30 ms. Dies sind keine gefahrenen Passagen oder vollständigen historischen Replays.

Plugin-Version `0.3.20.0`, SHA256 `87050FC31939A896C83288E76E8C91046818B6D399374E2078D4F6D0F87B12FE`, MVID `566ab16f-aebe-4a21-b322-490ffc485c61`. Das eingebettete Hostmanifest bleibt `7E812E68BC769C2A546E6360E6927DC1EB474F95D835E65E402365381DA046A9`. Der finale Kartenlauf ist privat als `fixtures-new-fr-0320-final-summary.json` festgehalten. Die sechs Host-/SDK-/Messdateien bleiben unverändert.

**Noch nicht installiert:** ETS2LA und das Spiel laufen bei der Abschlussprüfung weiterhin; die vorhandene Plugin-Datei bleibt 0.3.19. Für den Austausch nur dieser DLL muss ETS2LA bei sicher abgestelltem Lkw geschlossen werden. Das Spiel kann geöffnet bleiben. Eine tatsächliche Aktivierung, Bremsübernahme und Durchfahrt mit .20 ist noch nicht geprüft.

## Verbleibende Grenzen

Eine automatisch abrechnende, von der aktuellen Position erreichbare Lkw-Spur wird nicht für jede Anlage vorausgesetzt. Manuelle Zahlung wird nicht automatisch als AutoPASS behandelt. Standardführung bestätigt weder ausschwenkende Sonderladung noch vollständige Gespannfreiheit in engen Kurven. Die gemeldete Linkskurvenkollision ist ohne Aufnahme ihres Beginns weiterhin nicht geklärt. Hintergrundpedale unter TruckersMP, allgemeine Baustellen-/Schildvermeidung und zuverlässige Durchfahrt in allen Ländern sind mit diesem Patch nicht nachgewiesen. Eine echte Mautdurchfahrt sowie Aktivierung und manuelle Übernahme bleiben nach dem Einbau zu prüfen.
