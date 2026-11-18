# 0.4.9 – gerichtete Kreuzungsrouten

Stand: 18. November 2026. Offline vorbereiteter Kandidat; nicht installiert oder im Spiel abgenommen. Die laufende Installation bleibt 0.4.8. Der Gesamtauftrag ist offen.

## Belegter Auslöser

Um 13:17:24 MEZ löste 0.4.8 beim Überqueren einer inzwischen grünen Kreuzung die Gegenrichtungsprüfung aus. Die Ampelfreigabe war bereits verarbeitet; weder fehlender Pedalempfang noch eine ausbleibende Lenkausgabe löste diesen Halt aus. Der Lkw fuhr mit etwa 11,6 km/h in der bisherigen Richtung.

Die neu angebotene Route enthielt innerhalb eines Prefabs einen rückwärts verlaufenden Sprung von 10,509 m. Die Projektion darauf ergab ein Richtungsprodukt von −0,99958. Die vorherige akzeptierte Route lag dagegen nur etwa 6,7 cm neben dem Fahrzeug und zeigte weiterhin vorwärts. Die getrennt gespeicherten neuen und vorherigen Routen dürfen nicht miteinander verwechselt werden.

Die tatsächliche gepinnte native Graphsuche besuchte sowohl `PreviousLines` als auch `NextLines`. Dadurch konnte sie gegenläufige Verbindungen in eine anschließend ausschließlich Start-zu-End interpolierte Kurvenfolge aufnehmen. Die neue falsche Folge hatte 25 Kurven; die vorherige richtige Folge fünf. Eine nachträgliche Änderung von Kurvenlängen durch wiederholtes Interpolieren wurde in der Gegenprobe nicht beobachtet.

## Gezielte Änderung

Nur der Suchrumpf von `ParsedPrefab.TraverseCurveUntilTarget` wird im genau geprüften Host ersetzt. Er folgt `NextLines`, prüft Indizes und endliche Geometrie und verwirft getrennte Endpunkte. Die 1,5-m-Verbindungsgrenze entspricht bestehenden Anschlussprüfungen; sie ist keine Behauptung mathematisch exakter Kontinuität.

Die Änderung gilt für alle erzeugten Alternativen, auch diejenigen, die `EnsureContinuity` später auswählt. `CurveDirection`, Interpolation, Auswahlreihenfolge, Richtungsprüfung und Ausgabefristen bleiben unverändert. Insbesondere bedeutet `Backward` in der tatsächlichen Interpolation keine Erlaubnis, `PreviousLines` als vorwärts befahrbare Verbindungen anzuhängen. Auch beide richtigen realen Kreuzungspfade verwenden dieses Metadatum.

Die Pluginfassung bindet das zugehörige Paket `0.4.9-host-1` und speichert künftig zusätzlich `curveDirection` im Routenbericht. Es gibt keinen neuen Wiederanlauf aus veralteten Routendaten und keine Ausnahme von der Gegenrichtungsprüfung.

## Gegenprüfungen

- Die tatsächliche Karte und die gepinnten Bibliotheken reproduzieren beide gespeicherten Routen exakt: 651,901811 m mit dem aufgezeichneten Fehler beziehungsweise 150,916086 m ohne Fehler.
- Beide gültigen nativen Hauptwege bleiben nach der Korrektur identisch. Die zwei falschen Alternativen entfallen.
- Ein isolierter Lauf des tatsächlichen `GetPathsFromNodeToNode` und `EnsureContinuity` reproduziert mit einer ausdrücklich rekonstruierten Alternativreihenfolge den gesamten falschen Plan. Derselbe Lauf mit korrigierter Bibliothek erzeugt eine gültige Anschlussroute von 194,013277 m; die unveränderte Richtungsprüfung akzeptiert sie. Die damalige vollständige Alternativreihenfolge und ihr ursprünglicher Aufruf wurden nicht aufgezeichnet; es handelt sich nicht um eine vollständige Wiederholung aller historischen Zustände.
- Neue Tests am tatsächlich umgeschriebenen Host prüfen falsche Rückverbindungen, getrennte Fahrtrichtungen, Zyklen, ungültige Indizes, fehlende Anschlüsse, getrennte Endpunkte, ungültige Längen und fremde Kurven. Innere Interpolationspunkte bei ungleichen Kurvenlängen stimmen für beide nativen Richtungsmetadaten überein.
- Der neue Gegenfall scheitert mit dem bisherigen Host genau an der falschen Rückverbindung und besteht mit dem Kandidaten. 255 vollständige Pluginprüfungen gegen die Originalhost-Fixture bestehen. Der abschließende isolierte Kandidatenlauf inklusive echter Plugin-/Pedalausgabe besteht mit 307 Assertions; einige stammen aus zeitabhängigen Schleifen und sind keine 307 unabhängigen Fahrfälle.

Kandidat-Plugin SHA-256: `00F6280BF75BB93F05A6D805E4A4BA5DBAA30F822F89FA8AE0840BD02BC2DEB0`.
Geprüfter Game-Kandidat SHA-256: `4276FE0CC041422FFCE0CD30561508FB78FA127B745B89EA06F6BC35010C029A`.
Paketmanifest SHA-256: `29FBD1DF1D5A69C777F13B699E9807125E279FC33CEBCE13EDED129D6252B976`.

Der unabhängig geprüfte Installer tauscht ausschließlich fünf Appdateien nach vollständiger Sicherung aus. 17 isolierte Installations-/Rollback-Simulationen bestehen, einschließlich aller fünf Abbruchpositionen, Prozessneustart, Fremdbytes und umgeleiteter Pfade. Der bestehende Szenenworker wird nur auf unveränderte Identität geprüft; Spieldateien bleiben außerhalb der Schreibmenge. Ein laufendes Spiel ist zulässig, ETS2LA samt Worker muss geschlossen sein. Installer SHA-256: `4F0480ED2D4CFC4D55B72F6E5EEE5D1EBC102FA4942A5A36278E2A5100AB4CCA`. Noch keine Installation durchgeführt.

## Weitere Beobachtungen und Grenzen

Der 0.4.8-Parktest hielt die Pedale über mehr als 40 Sekunden neutral. In der anschließenden Vordergrundfahrt sind sowohl Gas- als auch Bremswirkung im Spiel belegt. Die gemeldete manuelle Übernahme unmittelbar nach dem Parktest ist in der betreffenden Aufnahme nicht eindeutig enthalten und wird deshalb nicht als unabhängig bestätigter Testabschluss gezählt. Der Nutzer bestätigte den späteren bewussten Start und beauftragte die weitere Beobachtung.

Die vollständige Aufnahme 13:09:36–13:19:36 enthält den Kreuzungsfehler; die Bremse kam dabei tatsächlich im Spiel an. Der Luftdruck blieb über der Warnschwelle, Kabinen- und Chassisschäden stiegen nicht an. Die moderne Pedalausgabe blieb ausgeschaltet. Dies ist kein Nachweis für Hintergrundfahrt.

Der Nutzer übernahm anschließend wegen eines drohenden Pfostenkontakts des Aufliegers innen beim Abbiegen. Zwischen zwei Aufnahmefenstern fehlen rund 38 Sekunden; zeitgleiche Auflieger- und Pfostengeometrie des kritischen Moments liegt nicht vor. Die Standard-Hindernisprüfung bildet einen 3,4-m-Korridor um die Zugmaschinenroute ab, keine innere Nachlaufkurve des Aufliegers. Diese Grenze ist im Code bestätigt. 0.4.9 beansprucht deshalb keine Behebung dieses zweiten Problems. Eine beliebige Verbreiterung des Korridors oder bloß geringere Geschwindigkeit wäre kein belastbarer Ersatz.

Offen bleiben Aufliegerführung beim Abbiegen, TruckersMP-Hintergrundpedale, die getrennt beobachteten nativen Lenkausfälle sowie vollständige reale Maut-/Zahlungs-/Bahnübergangsabnahme. Die neue Kreuzungskorrektur wird erst nach geschlossenem ETS2LA mit Sicherung eingebaut und danach im Spiel geprüft; während der aufgezeichneten Fahrt wurden keine Dateien oder Einstellungen verändert.
