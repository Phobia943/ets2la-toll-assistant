# Review 0.3.19 – geprüft und vorbereitet, noch nicht installiert

Aktuell ist 0.3.18 installiert und wird beobachtet. 0.3.19 ist als begrenzte Korrektur fertig vorbereitet und kritisch geprüft: **155 Integrations-, 82 Kernlogik-, 120 isolierte Host-/Pluginprüfungen und 63 Aufnahmetests** sind bestanden. Auch die sechs schnellen frühen Kartenfälle unten sind grün. Plugin-SHA256: `766EC8EEBF337E493A8E8EAF218B42A1BB3A74182FC85DB8D3E5F9D806547A3F`, MVID: `e270c69c-2524-4731-9e72-a6980fe14f8d`. Das eingebettete Hostmanifest ist unverändert. Paketmanifest-SHA256: `B06D04EAD4830F8FB2991DF86865F0F7BC3DA9059853600210E586E84C0D6327`. Die lokale Versiegelung liegt privat in `maut-0319-prepared-validation.json`. Installation, Standtest und tatsächliche Durchfahrt sind noch nicht erfolgt. Die Installation und Siegel der .18 sind im [vorherigen Review](REVIEW-0.3.18.md) festgehalten.

## Geplantes Plugin-Update auf bestehender Baseline

Die .19 ersetzt ausschließlich `Plugins/MautAssistent.dll` auf der bereits verifizierten .18-Kombination. Die vier Host3-Bibliotheken mit Manifest-SHA256 `7E812E68BC769C2A546E6360E6927DC1EB474F95D835E65E402365381DA046A9`, das Szenen-SDK `BC87…FDBA9` und der Geometriebeobachter `A91E…E5262` müssen unverändert mit ihren vollständigen gepinnten Hashes passen. Die sieben Dateien bilden die Kompatibilitätsbasis; daraus folgt keine Pflicht, sechs unveränderte Dateien erneut zu ersetzen. Eine unbekannte oder abweichende Baseline wird nicht still weiterbenutzt.

Für diesen Plugin-Austausch muss ETS2LA geschlossen sein; das Spiel kann geöffnet bleiben, da keine Spiel-DLL geändert wird. Ein neuer, genau gebundener Update-Nachweis ist nötig: Das alte .18-Runtimegate autorisiert keinen .19-Build. Eine Installation ist noch nicht erfolgt.

## Problem und geänderte Suche

Die bisherige Suche konnte zunächst die kürzeste topologische Verbindung zu einer Schranke bestimmen und erst danach deren blockierte Kurven feststellen. War diese Verbindung gesperrt, konnte eine längere freie Verbindung zum selben Tor unberücksichtigt bleiben; eine manuelle Alternative erschien dann als einzige geeignete Spur.

Der Kandidat verlagert die Sperrkurvenmaske in die gerichtete Routensuche. Dafür muss ein abgeschlossener, zum aktuellen Modell/Profil/Descriptor gehörender Nachweis vorliegen. Eine Durchfahrt durch ein anderes Tor darf nicht als Umweg benutzt werden. Vor dem Anwenden bleiben die aktuellen Freiraum-, Anschluss- und Anfahrtsprüfungen erforderlich. Ein theoretisch freier Weg wird dadurch nicht automatisch von der aktuellen Fahrzeugposition erreichbar.

### Konkreter Befund und Bedeutung von AllowedVehicles

Im untersuchten gerichteten Fall benötigen sämtliche Verbindungen zum automatischen Tor 15 die als `SmallVehicles` markierte Kurve 1; die übrigen Kurven sind `AllVehicles`. Die vollständige physische Maske für das geprüfte Profil 4,4 × 3,4 m ist dort frei. Die bisherige harte Interpretation des Metadatums schloss die Verbindung trotzdem aus. Zum anderen automatischen Tor 16 sind Kurven 27/32 ebenfalls als `SmallVehicles` markiert; zusätzlich blockiert bewegte Modellgeometrie Kurve 72. Eine pauschale Freigabe beider Tore wäre daher falsch.

Die [offizielle SCS-Dokumentation zu Allowed Vehicles](https://modding.scssoft.com/wiki/Documentation/Tools/SCS_Blender_Tools/Locators/Prefab_Locators#Allowed_Vehicles) beschreibt die Eigenschaft als Eignung für KI-Verkehr und dessen bevorzugte Kurvenwahl. Sie belegt weder eine Höhen-/Breitenbeschränkung noch Lkw-Kollisionsfreiheit. Der Kandidat darf die `SmallVehicles`-Präferenz nur bei abgeschlossenem Modellbeleg, tatsächlich bestätigter automatischer Zahlungsart und vollständig freier physischer Kurvenmaske überstimmen. Die allgemeine `ViaGate`-Suche bleibt ohne diesen beleggebundenen Ausnahmeweg restriktiv. `PlayerOnly`, Escort-Sonderfälle, Gegenrichtung und andere Tore werden von der Ausnahme nicht freigegeben. Die laufenden realen Freiraum-, Bewegungs-, Tracking- und Ausgabeschutzregeln werden dafür nicht gelockert.

Geprüfte Negativfälle sind `PlayerOnly`, unbekannte Zahlungsart und eine physisch blockierte automatische `SmallVehicles`-Spur.

## Vorsichtiger Wechsel von manuell auf automatisch

Eine zunächst ausgewählte manuelle Spur wird erneut geprüft, wenn später eine automatische Alternative belegt wird. Ein Wechsel erfolgt nur auf dem noch ausreichend langen gemeinsamen Zufahrtsstück, ohne laufende Spuränderung und nach erneuter Prüfung des tatsächlich anzuwendenden Pfads. Bei fehlenden Belegen, geänderter Route oder zu spätem Start bleibt die bisherige Auswahl erhalten. Das ist kein Wechselauftrag mitten zwischen den Mautspuren und keine automatische Bezahlung einer manuellen Spur.

## Entscheidungsnachweis

`laneAttempts` enthält je Versuch Tor, Zahlungsart, Start-/Endkurve, Ausschluss- beziehungsweise Erreichbarkeitsgrund und blockierte Kurven. Der zugehörige Modellbeleg nennt seine Revision, Modellvariante, Teile, Profilhöhe/-breite und Geometriestempel. `laneAttemptContext` hält Zeit und Route des jeweiligen Suchversuchs fest. `laneDecisionContext` gehört dagegen zur tatsächlich gewählten Spur. Ein später gescheiterter Alternativversuch behält den gewählten Entscheidungskontext bei und darf ihn nicht mit der neuen erfolglosen Suchroute überschreiben. Ein älterer Entscheidungskontext ersetzt keine aktuelle Quell- oder Routenfreigabe. Diese privaten Diagnosen enthalten Spielpositionen und gehören nicht als Aufnahmen ins öffentliche Repository.

Mögliche Ursachen bleiben getrennt: noch ausstehender oder abgelehnter Modellbeleg, Metadatenpräferenz ohne zulässigen Ausnahmebeleg, keine gerichtete Verbindung, nur physisch gesperrte Verbindungen, fehlende aktuelle Freiraumfreigabe oder nicht mehr erreichbare Anfahrt. Der Produktions-API-Gegenlauf besteht bei 20 km/h und in den sechs frühen schnellen Fällen unten. Dieser Fall erklärt nicht nachgewiesenermaßen sämtliche bisherigen Länderprobleme.

## Frühe gerichtete Kartenmatrix

Die Gegenprobe verwendet die tatsächliche 325,55 m lange `RoadList`, rekonstruierte Fahrzeugpositionen auf Lane −2, erlaubten Spurwechsel und das ausdrücklich geprüfte Profil **4,4 m hoch × 3,4 m breit**. Die echte Produktionssuche und Routenanwendung liefern:

| Zufahrtsrest | 50 km/h | 85 km/h | 100 km/h |
| --- | --- | --- | --- |
| 200 m | Auto 15, frei, angewandt | Auto 15, frei, angewandt | Auto 15, frei, angewandt |
| 300 m | Auto 15, frei, angewandt | Auto 15, frei, angewandt | Auto 15, frei, angewandt |

In allen sechs Fällen sind `Apply=true`, `error=null` und `clear=true`, mit Eingangszielspur −1 und Ausgang 1. Der erste kalte Aufruf dauerte 71,31 ms, die übrigen 15,00–18,24 ms. Auto-Tor 16 bleibt wegen des physischen Hindernisses ausgeschlossen. Die spätere historische Konfiguration bei 49,86 km/h kann weiterhin die manuelle Spur 6 wählen; ein geheilter historischer Durchlauf wird nicht behauptet.

Dies sind Offlinefälle auf echten gerichteten Kartendaten, keine Livepassagen und kein Beleg für beliebige Profile, andere Anfahrtspositionen oder alle Länder. Das tatsächliche Gespann und die laufenden physischen/Trackingprüfungen bleiben erforderlich.

## Aufnahmehelfer: präzisere Fehler, unveränderte Gültigkeit

Der gepinnte SDK-Vertrag bleibt 40 Einträge mit je 48 Byte, Typ bei Offset 32, Zustand bei 40 und ID bei 44. Typ 2 erlaubt unverändert 0 schließend, 1 geschlossen, 2 öffnend und 3 offen. Ampelbits von Typ 1, etwa Grün 8 oder Blinklicht 32, sind keine zusätzlichen Schrankenzustände.

Der generische Marker `InvalidGateData` bleibt erhalten. Zusätzlich bewahrt der Helfer Slot/ID/ursprünglichen Zustand oder rohe XYZ-Floatbits und Zelloffsets als JSON-sichere Zeichenketten. Ungültige Daten werden weiterhin verworfen und nicht als offen ausgegeben. 63 Aufnahmetests, darunter 19 neue Bytefälle, bestehen; das ist ein Nachweis des Aufnahmeformats, kein Fahr- oder Spurauswahlnachweis. Die alte Aufnahme enthält 5.404 Zeilen, davon 1.463 mit insgesamt 8.196 verworfenen Schrankenslots. Deren ursprüngliche Rohwerte wurden damals nicht gespeichert; der genaue Grund ist rückwirkend nicht belegbar.

## Noch ausstehende Schritte

- Installation des genau versiegelten Plugin-Updates auf der überprüften Baseline.
- Standtest mit Aktivierung sowie Rückgabe der Steuerung durch Controller-Bremse/F9.
- Eine spätere reale Anfahrt und vollständige Durchfahrt; reine Such- oder Aufnahmeprüfungen belegen diese nicht.
- Ursachenklärung der nicht vorab aufgezeichneten Kurvenkollision. Ein weiterer Plugin-Build benötigt eigene Prüfungen.

Die bisherigen Grenzen bleiben bestehen: begrenzte geladene Szenenabdeckung, explizite Fahrzeug-/Achskonfiguration, lokale Modellannahmen sowie keine allgemeine Baustellen-/Schildvermeidung oder gelöste Hintergrundpedale unter TruckersMP. Eine zuverlässige Mautdurchfahrt in allen Ländern ist nicht nachgewiesen.

## Grenze der Kurvenunfallanalyse

Für die berichtete Kurvenkollision fehlt eine Aufnahme des Ablaufs vor dem Ereignis. Ein späterer Snapshot beweist weder einen Lenkausfall noch die konkrete Unfallursache. Der Vollgespannsweep wird im Mautbereich angewandt und ist kein allgemeiner Schutz für sämtliche normalen Straßenkurven. Die .19-Spurwahländerung wird deshalb nicht als Behebung dieses Unfalls ausgegeben.

Die anschließende Nachaufnahme umfasst 1.625 Zeilen über 179,93 Sekunden. Kabinen- und Chassisschaden änderten sich darin nicht; es wurde keine Luftdruckwarnung beobachtet. In 176 frischen Statusständen gab es keine Faultmeldung; 13 zeigten Assistenz an und 163 Assistenz aus. Die überwiegend manuelle Nachbeobachtung ist weder ein Langzeitnachweis der Assistenz noch ein Beleg für den nicht aufgezeichneten Unfallablauf davor.
