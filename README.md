# ETS2LA Mautassistent

**0.3.21 ist installiert und korrigiert einen reproduzierten unnötigen Halt nach der Mautausfahrt.** Identische oder numerisch leicht abweichende Endpunkte zwischen zwei Straßenabschnitten werden bei der Standardführung nicht mehr als verlorene Route behandelt. Die vorherige .20-Datei ist gesichert; die Live-Fahrprüfung der .21 steht noch aus. [Befund, Prüfungen und Grenzen](REVIEW-0.3.21.md), [Standardmodus 0.3.20](REVIEW-0.3.20.md), [Baseline 0.3.18](REVIEW-0.3.18.md).

Standardmäßig wird ein benannter Planungskorridor von 4,5 m Höhe und 3,4 m Breite verwendet. Eine unbekannte Ladung blockiert allein weder Zahlungsarterkennung noch lokale Fahrentscheidung. Die zusätzliche genaue Gespannprüfung bleibt optional. Dieser Standardkorridor bestätigt keine vollständige Ladungs- oder Aufliegerfreiheit.

Die Standardwerte sind Annahmen für die Spurplanung, keine gemessenen Ladungsmaße. Bekannte niedrige Durchfahrten, Hindernisse, aktuelle Schranken und die gerichtete An-/Ausfahrt bleiben geprüft. Vollständige Freiheit für lange oder überstehende Ladung wird damit nicht nachgewiesen. Geladen ist nicht gleich fahrbereit; nach dem Einbau bleiben Standprüfung und reale Mautpassage erforderlich.

Die Erweiterung ergänzt ETS2LA Pathfinding, Lane Assist und Adaptive Cruise Control in Euro Truck Simulator 2. Sie plant eine erreichbare Lkw-Mautspur, begrenzt die Anfahrt und wartet auf die tatsächlich geöffnete Schranke. Oberfläche und Meldungen sind auf Deutsch.

## Umgesetzt und geprüft für 0.3.19

- Die gerichtete Suche läuft innerhalb des belegten freien Navigationsgraphen. Eine gesperrte kürzeste Verbindung verwirft nicht automatisch dieselbe Mautspur, wenn eine längere belegte Verbindung zum selben Tor existiert. `AllowedVehicles` beschreibt laut [SCS eine Eignung für KI-Verkehr](https://modding.scssoft.com/wiki/Documentation/Tools/SCS_Blender_Tools/Locators/Prefab_Locators#Allowed_Vehicles), keinen geometrischen Lkw-Freiraumnachweis. Eine `SmallVehicles`-Präferenz ist ausschließlich für belegte automatische Tore mit abgeschlossener vollständiger physischer Kurvenprüfung überstimmbar. `PlayerOnly`, fehlende Belege, Gegenrichtung, fremde Tore und die aktuelle Anfahrtsprüfung werden dadurch nicht allgemein freigegeben.
- Eine zunächst gewählte manuelle Spur kann nur dann auf eine inzwischen belegte automatische Spur wechseln, wenn die gemeinsame Zufahrt noch reicht und keine laufende Spuränderung stört. Fehlender Nachweis oder ein zu später Wechsel lässt die bisherige Auswahl bestehen. Manuelle Zahlung wird dadurch nicht als automatische Zahlung ausgegeben.
- Diagnose hält je Spur Auswahl-/Ausschlussgrund, blockierte Kurven, Modell-/Profilrevision und den tatsächlichen Routen-/Anfahrtskontext fest. Spätere Suchversuche haben einen eigenen Kontext; ein gescheiterter Versuch überschreibt nicht den Kontext der tatsächlich gewählten Spur. Der Aufnahmehelfer bewahrt bei verworfenen Rohschranken zusätzlich Slot, ID, Originalzustand beziehungsweise Positionsbits; die gültigen Zustände und Fail-Closed-Regeln bleiben unverändert.

Die konkrete Kartenanalyse fand eine physisch freie Verbindung zu Auto-Tor 15, die am bisherigen harten `SmallVehicles`-Filter scheiterte. Sechs Produktions-API-Gegenfälle bei 50/85/100 km/h und jeweils 200/300 m Zufahrtsrest wählen diese Spur erfolgreich. Sie verwenden rekonstruierte Positionen auf echten Kartendaten und ein ausdrücklich festgelegtes Profil; sie sind keine gefahrenen Passagen. Auto-Tor 16 bleibt physisch gesperrt. Keine Durchfahrt in allen Ländern ist belegt.

Für das abgeschlossene .19-Update wurde nur die Plugin-Datei ersetzt. Die sechs unveränderten Host-/SDK-/Geometriedateien der geprüften .18-Baseline müssen weiterhin exakt passen; sie müssen deshalb nicht pauschal erneut ausgetauscht werden. Dafür muss nur ETS2LA geschlossen sein, das Spiel kann geöffnet bleiben.

## Übernommene Laufzeitfunktionen der .18-Basis

- Eine modellseitig belegte und geometrisch passende Spur kann bereits geplant werden, bevor das kurze SDK-Sichtfeld ihre Schranke meldet. Fehlende Modellbelege führen zunächst nur zum Vorladen.
- **Geplante Spur und live zugeordnete Schranke sind getrennte Zustände.** Ohne passende frische Torprobe bleibt innerhalb des geschwindigkeitsabhängigen Bremshorizonts ein normaler Halt vor dem Kartenanker erforderlich. SDK-Typ, Nummer, Weltposition und gerichteter Routenabschnitt müssen zusammenpassen; die Nummer allein genügt nicht.
- Standardmäßig muss die vollständige Öffnung weiterhin zwei Sekunden beobachtet sein. Bei breiten Durchfahrten müssen alle getrennten Schrankenarme offen sein. Nach normaler Freigabe gilt standardmäßig höchstens 10 km/h bis 30 m hinter dem Tor.
- Ein erreichter geplanter Halt bekommt eine physische Bremshalteausgabe. Mehrere Haltegründe werden gemeinsam begrenzt; das Ende eines Grundes darf einen anderen nicht lösen. Eine stärkere Fehler- oder native Bremsung bleibt erhalten.
- Fehlende obligatorische lokale Szenen- oder statische Streckenbelege können weiterhin einen geplanten Halt verlangen. Nur im optionalen detaillierten Modus ist zusätzlich ein vollständiger Fahrzeug-/Ladungsnachweis erforderlich. Bereits bekannte Routentopologie allein ist keine Durchfahrtfreigabe.
- Die vier angepassten Hostkomponenten `0.3.18-host-3` binden native Ausgaben und Haltebefehle an ihre Sitzung und ursprüngliche Berechnungszeit. Ergebnisse über 200 ms erhalten keine neue Ausgabeautorität. Die Halteleihe endet spätestens 250 ms nach ihrem Ursprung.
- Bewegte Modellteile erhalten einen gemeinsamen Mittelpunkt für ihre Rotationshülle. Generische Lichtklassen werden nur nach vollständigem Render-/Schema-/Geometrienachweis als reine Lichtdarstellung behandelt. Diese lokalen Korrekturen beweisen keine allgemein freie Durchfahrt.
- Im optionalen detaillierten Gespannschutz verwendet die Fahrregelung getrennte Vorhersagen von Zugmaschine und Auflieger mit gemessenen Kupplungspunkten/Radachsen und geprüften Ladungsboxen. Im Standardmodus folgt der lokale Prüfkorridor dem bestätigten Straßenverlauf; er verspricht keine gesamte Aufliegerbewegungsfläche. Die frühere Kugel für alle Gespannwinkel ist kein Kollisionskorridor mehr.
- Unbekannte Bewegung wird lokal zum jeweiligen Körper/Korridor behandelt. Modellabweichung und Radkontakt werden überwacht; ein unveränderter Kontaktzustandswechsel erhält höchstens 600 ms Übergangszeit. Unbekannte Bewegung gilt weiterhin nicht als gemessene Physik oder als Stillstand.
- Die zusätzliche Notbremsprüfung nutzt aktuelle gemessene, ungepolsterte Zugmaschinenboxen. Sie verwendet nicht den früheren pauschal mehrfach verbreiterten Korridor. Der geprüfte Nachbarspurfall mit 46 cm Abstand bleibt frei, eine zentrale Box bei 20 km/h löst weiter die Notbremsung aus. Das sind abgegrenzte Offline-Gegenproben.

Standardwerte sind 20 km/h Anfahrt und 10 km/h Passage. Das sind Begrenzungen, keine gemessene Bremswegzusage für jedes Gespann. Ein passend eingeordneter Lkw kann automatisch abrechnende Spuren bevorzugen. Die Zahlung in einer manuell abrechnenden Spur bleibt beim Spieler.

## Fahrzeughülle und lokale Szene

Der frühere pauschale 6 × 3-m-Prüfkorridor wird nicht wieder eingeführt. Standardführung plant ausdrücklich mit 4,5 × 3,4 m und benötigt für lokale Szenenaussagen aktuelle rohe TruckersMP-Messungen. Die optionale genaue Hülle benötigt darüber hinaus zusammenpassende Fahrzeug-/Aufliegerposen, Kupplungs-/Achsdaten, Ladungsdefinitionen und Navi-Bezug. Die native Eigenschaft `CargoCoverageKnown` bleibt **false**; aus einer Modellbox oder dem Standardkorridor allein wird keine Ladungsfreigabe.

Im optionalen detaillierten Modus erhalten unbekannte Haupt-Ladungsmodelle, Mehrfachauflieger und nicht belegte Kopplungen keine automatische Hüllenfreigabe. Unterstützt ist dort das ausdrücklich gemeldete SCS-Konfigurationsmodell `chain_type=single` mit passenden nativen Messungen und festen gemessenen Achsen. Das Bewegungsmodell gilt vorwärts auf nahezu ebenem Grund im langsamen Mautbereich bis 20 km/h. Im Standardmodus werden diese fehlenden Detailnachweise nicht zur Start- oder Haltebedingung; die grundsätzliche Eignung für Sonderladung ist damit nicht bestätigt.

Die zusätzliche [spielseitige Szenenerfassung](Native/ParkedScene/README.md) meldet aktuell erfasste geparkte Fahrzeugkörper und die gelesenen Bereiche geladener Mautanlagen. Sie ersetzt keine vollständige Verkehrs- oder Hinderniserkennung. `Complete` und ein vollständiger gelesener Bereich sind **kein `SpawnFinished`-Nachweis**. Noch nicht erschienene Fahrzeuge, aktive Fahrzeuge außerhalb dieser Quelle und zukünftige Änderungen bleiben außerhalb dieser Aussage. Fehlende oder unbekannte Bewegung kann weiterhin einen vorsichtigen normalen Halt auslösen.

Die bisherige [TruckersMP-Geometriequelle](Native/TruckersMPGeometry/README.md) ist für diese automatischen Mautnachweise erforderlich. Sie betätigt selbst keine Lenkung oder Pedale.

## Voraussetzungen und Paket

Die lokale Grundlage ist ETS2LA `2026.11.5297`, ETS2/SDK `1.62`, Pathfinding `1.2.9`, Lane Assist `1.6.2`, ACC `2.1.1`, PathLib `1.2.8`, PIDLib `1.0.4` und .NET 10. Andere Builds benötigen eine eigene Kompatibilitätsprüfung.

Die geprüfte **0.3.18-Baseline umfasst sieben zusammengehörige Dateien**:

| ETS2LA-Anwendung | Spiel `bin/win_x64/plugins` |
| --- | --- |
| `Plugins/MautAssistent.dll` | `ets2la_plugin.dll` mit zusätzlicher Szenenerfassung |
| `ETS2LA.Game.dll`, `ETS2LA.State.dll` | `MautGeometry.dll` mit 16-ms-Beobachtung |
| `Plugins/tumppi066.adaptivecruisecontrol/AdaptiveCruiseControl.dll` | |
| `Plugins/tumppi066.laneassist/LaneAssist.dll` | |

Nur die neue Plugin-DLL zu kopieren ergibt keine unterstützte Kombination. Die Geometriequelle gehört jetzt zum gemeinsamen Austausch, weil ihr Beobachtungsintervall und ihre Zeitstempel Bestandteil der Laufzeitkorrelation sind. [Quellen der Hostanpassung](Tools/HostCompatibility) und [Review 0.3.18](REVIEW-0.3.18.md) beschreiben die Zuordnung.

Beim damaligen vollständigen Sieben-Datei-Austausch mussten ETS2LA **und ETS2/TruckersMP geschlossen bleiben**, da auch Spiel-DLLs ersetzt wurden. Der Ablauf prüfte Quellen, Ziele, Modulkennungen, Hostmanifest und Runtimegate, sicherte alle sieben Originale und staged alle Ersatzdateien vor dem ersten Austausch. Jeder einzelne Austausch war atomisch; sieben Dateien in zwei Wurzeln bilden keinen gemeinsamen atomischen Dateisystemwechsel. Das geplante .19-Plugin-Update prüft diese Baseline weiter, ersetzt aber keine Spiel-DLL und benötigt deshalb nur geschlossenes ETS2LA. Der Installer startet weder Apps noch Assistenz oder Fahrtests.

**Niemals den gesamten Host-Vorbereitungsordner oder `isolated-replay-*` installieren oder verteilen.** Die Replay-Unterordner enthalten absichtlich umgeschriebene Test-DLLs mit anderen Prüfsummen. Nur die ausdrücklich im versiegelten Sieben-Datei-Paket aufgeführten Produktionspfade dürfen ausgetauscht werden.

Der installierte .21-Plugin-Build besteht 170 Integrations-, 84 Kernlogik-, 120 Host-/Plugin- und 63 Aufnahmetests ohne Fehler sowie den geometrischen Vergleich aller 480 gespeicherten Bewegungsproben des Ausfahrtstopps. Prüfsummen, Review und genaue Grenzen stehen im [aktuellen Prüfbericht](REVIEW-0.3.21.md). Die unveränderten Geometriequellen haben zuvor 31 isolierte Beobachterprüfungen und 33 + 18 SDK-Szenenprüfungen bestanden. Die Live-Fahrprüfung der .21 steht noch aus. ETS2LA-Updates können die Kombination ersetzen; danach sind Fassungen und Prüfsummen erneut zu prüfen.

Die Initialisierung stößt reine Modellarbeit im Hintergrund an, und der F8-Wartezustand hält bis zur Bereitschaft. Ein vorbereiteter Korridor wird einmal pro Auswertung erzeugt und zwischen den lokalen Prüfungen geteilt. Die Laufzeitbelege der .18-Basis und die separaten .19-Kartenproben sind in den Reviews abgegrenzt; sie garantieren keine entsprechende Laufzeit für alle Modelle oder den gesamten Ausgabepfad. `Supported` des Bewegungsmodells allein erteilt keine Fahrberechtigung.

## Bedienung nach kontrollierter Einrichtung

| Eingabe | Funktion |
| --- | --- |
| F8 – Increase/RES | Start/erneute Routenprüfung, während der Fahrt zunächst selbst steuern |
| F9 – Cancel/MODE | Manuell übernehmen; im ausgeschalteten Zustand außerdem Modus wechseln |
| F10 – Decrease/SET | Tempo verringern/setzen |
| F11 – Next/OK | Weiter/bestätigen |
| Physische Bremsachse | Ab 12 % übernehmen; Loslassen startet die Assistenz nicht erneut |

Die Belegungen werden in ETS2LA Controls eingerichtet; ein Stream-Deck-Hotkey kann dieselbe Taste senden. F12 bleibt frei. Ein Overlay ist nicht erforderlich. Einstellungen liegen unter `%LOCALAPPDATA%\ETS2LA-Mautassistent\settings.json`.

Vor dem Fahrtest sind Stillstand, normaler Luftdruck, geladene Gespann-/Szenennachweise, Aktivierung und die Rückgabe von Lenkung/Pedalen mit Bremse oder F9 zu prüfen. Für die neue Kombination fehlen diese Standprüfung und eine vollständige reale Mautdurchfahrt noch.

## Bauen und offline prüfen

Im Projektordner mit PowerShell 7 und .NET 10:

```powershell
$env:EtsRoot = 'C:\Pfad\zu\ETS2LA\current'
./Prepare-References.ps1
dotnet run --project CoreTests -c Release
dotnet build Plugin/MautAssistent.csproj -c Release
dotnet run --project Tests -c Release
```

`Prepare-References.ps1` prüft Herstellerbibliotheken und legt sie ausschließlich im ignorierten lokalen Ordner `References` ab. Es verändert keine Installation. Die Plugin-Ausgabe ist `Plugin/bin/Release/net10.0/MautAssistent.dll`; für den Kandidaten müssen zusätzlich das passende Hostpaket und die spielseitigen Quellen gebaut und geprüft werden.

Die Logik- und Integrationstests verwenden synthetische Daten und echte Bibliothekstypen. Der isolierte Hosttest führt echte native Tick-/Publikationswege und optional die echten Plugin-Methoden `CommitPlannedHolds`/`ClearSceneHold` gegen umgeleitete Ausgabesenken aus. Er öffnet keine Spielspeicher und startet keine Ausgabe-Threads. Die Quellen und Skripte liegen unter [Tools/HostCompatibility](Tools/HostCompatibility) und [Native/ParkedScene](Native/ParkedScene). Die optional getestete Plugin-Datei und ihre Prüfsumme werden gesondert protokolliert; ein späterer Build benötigt eine neue Prüfung.

## Noch offen

- .19 ist installiert und geladen; Startfreigabe, vollständiger Standtest und echte Durchfahrt sind noch offen. Aktuelle Spurlage und Auflieger-/Ladungsdefinition müssen zuerst geklärt werden. Die FR-Gegenproben bleiben abgegrenzte Offlinefälle.
- Der französische Vorfall ist statisch untersucht. Die damalige Position hatte nur etwa 14,44 m auf der Zufahrt übrig und war für den erforderlichen Spurwechsel zu spät. Eine frühere virtuelle Position mit ungefähr 67 m Rest ist eine Offline-Hypothese, kein beobachteter Fahrnachweis. Eine weitere gespeicherte Niedrigtempo-Gegenprobe im v16-Zwischenstand ohne SDK-Tore plante rund 198 m vor dem Tor die passende automatische Spur. Eine Live-Schrankenzuordnung oder reale Passage belegt sie nicht; die reale Wiederholung steht noch aus.
- Die französische v20-Breitenprobe erlaubt statisch 3,4 m auf Spur 10; 3,5 m und breiter bleiben an fester Modellgeometrie abgelehnt. Die Mover-Mittelpunktkorrektur behebt diese getrennte Breitenfrage nicht.
- Die neue Hüllen- und Szenenerfassung wurde noch nicht gemeinsam im laufenden Spiel als Grundlage einer Mautdurchfahrt bestätigt. Unbekannte Hüllen oder Bewegungen können weiterhin zu Halten führen.
- Mehrfachauflieger, unbekannte Haupt-Ladungsmodelle, lenkbare Auflieger und Fahrten außerhalb der expliziten Modellannahmen sind nicht automatisch freigegeben. Eine endliche Modellvorhersage ist keine vollständige zukünftige physische Schleppkurve.
- Allgemeine Baustellen-, Verkehrsschild- und Hindernisvermeidung ist nicht gelöst. Kurvenvorschau und lokale Schrankenprüfung ersetzen sie nicht.
- Der Vollgespannsweep gilt für den Mautbereich, nicht für jede normale Straßenkurve. Für die berichtete Kurvenkollision fehlt eine Aufnahme des Ablaufs davor; ein späterer Snapshot beweist keinen Lenkausfall und die .19-Spurwahlkorrektur keine Behebung dieses Unfalls.
- Hintergrund-Gas und -Bremse nach Alt+Tab in TruckersMP bleiben ungelöst. Für die Prüfung muss das Spiel im Vordergrund bleiben. [Bekannter Fokus-/Pedalnachweis](BACKGROUND_INPUT.md).

Keine Abdeckung aller Länder und keine vollständige Behebung aller bisherigen Fahrprobleme ist nachgewiesen. Historische Beobachtungen stehen in [CHANGELOG.md](CHANGELOG.md), [TOLL_COVERAGE.md](TOLL_COVERAGE.md) und [VALIDATION.md](VALIDATION.md).

## Diagnose und Quelldaten

Rein lesende Aufnahmen sind mit `Tools/Record-TollDrive.ps1` und die Auswertung mit `Tools/Measure-TollDrive.ps1` möglich. Aufnahmen, private Wiederholungsfälle und extrahierte Spielmodelle gehören außerhalb des Repositorys. Diagnose enthält Spielpositionen; `*.jsonl`, Hersteller-DLLs, Referenzen und Buildausgaben werden nicht veröffentlicht. Das Projekt verteilt keine SCS-Spieldateien, fremden SDK-Quellbäume oder vorgefertigten Spurfreigaben.
