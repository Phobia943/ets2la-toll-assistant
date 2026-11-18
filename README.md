# ETS2LA Mautassistent

**Aktueller Arbeitsstand: 0.4.8-v2 ist nach unabhängigem Gegenreview mit Sicherung installiert; ihre reale Abnahme steht noch aus.** Die neue Fassung ergänzt statische Hindernisprüfung in einem getrennten Hilfsprozess und genaue Ausgabediagnose. 254 Integrationsfälle, 104 Logikfälle und 316 isolierte Hostprüfungen bestanden vor dem Paketreview; die danach korrigierte Shadowcopy-Auflösung wurde mit sieben Workergruppen und erneut 316 Hostprüfungen abgesichert. Hintergrundbetrieb und weitere reale Abnahme bleiben offen. [Nachweise und verbleibende Grenzen](REVIEW-0.4.8.md). Die folgenden Versionsnotizen beschreiben frühere Entwicklungsstände; sie sind keine aktuelle Installationsauskunft.

**0.4.6 korrigiert die Zuordnung offener Bahnübergangsschranken.** Die Schranken werden aus dem tatsächlichen Kartenprofil einer Gruppe zugeordnet; alle erwarteten Mitglieder müssen aktuell offen sein. [Befund und Grenzen](REVIEW-0.4.6.md).

**0.4.5 korrigiert die Behandlung fehlenden Pedalempfangs.** Ausbleibender Pedalempfang, etwa nach einem Fensterwechsel, sperrt Gas und zeigt eine Störung an, erzeugt aber allein keinen verriegelten Fehlerhalt mehr. Aktuelle Bremsgrenzen bleiben wirksam; ein entfallener Bremsgrund wird nicht nachgeholt. Dies behebt nicht die tatsächliche Hintergrundübertragung. [Prüfungen und Grenzen](REVIEW-0.4.5.md).

**Die Funktionen aus 0.4.4 bleiben enthalten: automatische Bestätigung an belegten manuellen Zahlspuren und genauere Eingabediagnose.** Der Helfer interagiert einmal im bestätigten Stillstand an einer eindeutig zugeordneten Zahlzone und wartet auf die eigene Schrankenöffnung. Nicht erreichbare oder unbekannte Zahlzonen bleiben manuell. Ein Nachweis der tatsächlichen Zahlung und vollständigen Durchfahrt steht aus. [Umfang und Grenzen](REVIEW-0.4.4.md).

Die [Ampel- und Physikframekorrekturen aus 0.4.3](REVIEW-0.4.3.md), die [Stillstandsbehandlung aus 0.4.2](REVIEW-0.4.2.md), die [Gasüberwachung aus 0.4.1](REVIEW-0.4.1.md) und die eigene Geschwindigkeitsregelung aus [0.4.0](REVIEW-0.4.0.md) bleiben erhalten. Für den geschützten Zahlungsimpuls benötigt 0.4.4 das zusammengehörige neue Hostpaket.

Die Erweiterung arbeitet mit ETS2LA Pathfinding und Lane Assist in Euro Truck Simulator 2/TruckersMP. Sie plant erreichbare Mautspuren, bremst vor der zugeordneten Schranke und wartet auf deren beobachtete Freigabe. Sie ist ein experimenteller Fahrassistent; allgemeine Hindernisvermeidung und fehlerfreie Fahrten in allen Ländern sind nicht nachgewiesen.

## Neue Regelung in 0.4.0

- Ein eigener Regler erzeugt Gas und Bremse aus Zieltempo, gemessener Fahrzeugbewegung und den aktiven Begrenzungen. Die normale Pedalausgabe gehört ausschließlich diesem Kanal. Alte ACC-Befehle und fremde Pedalkanäle werden nicht beigemischt. Fehlerbremsung und manuelle Übernahme bleiben getrennt wirksam.
- Der eingebaute Spiel-Tempomat muss ausgeschaltet sein. Bei aktivem Spieltempomat übernimmt die eigene Regelung nicht und schaltet ihn nicht blind um. Ein unabhängiger Pedalwächter erkennt einen ausgefallenen eigenen Regelzyklus auch dann, wenn die Lane-Assist-Lenkausgabe weiter frisch ist. Seine 150-ms-Ursprungsfrist bleibt unverändert; ein aktueller Quellenwiderruf allein wird über den normalen Brems-/Haltepfad behandelt und nicht pauschal sofort zur Notbremsung erklärt.
- Ein fehlendes Straßenlimit mit dem Wert 0 bedeutet nicht Tempo 0. Als vorsichtige Ersatzgrenze gelten dann höchstens 30 km/h; engere lokale Kurven-/Mautgrenzen bleiben stärker. 100 km/h sind nur bei belegter freier Strecke vorgesehen.
- Das gewünschte Tempo auf freier Strecke beträgt in der vorgesehenen Konfiguration bis zu 100 km/h. Kurvenvorschau, erkannter Verkehr, Ampeln, Mautschranken, aktuelle Streckenbelege und konfigurierte Geschwindigkeitsgrenzen können es senken. Leichte Überschreitung eines weichen Tempowunschs darf bei freier Strecke durch Rollen abgebaut werden; ein harter Gefahren- oder Haltgrund bleibt bremswirksam.
- Verkehrsobjekte werden aus den vorhandenen SDK-Quellen mit geprüfter Provider-Orientierung und Leerplatzbehandlung übernommen. Die Quelle ist begrenzt: 39 Verkehrsslots, 40 Parkslots und begrenzte Aufliegerfelder sind keine vollständige Erfassung aller Verkehrsteilnehmer, Hindernisse oder künftigen Spawns.
- Mauttore erhalten ihre Freigabe aus dem zugeordneten gerichteten Tor-/Routenbeleg. Ein räumlich naher SDK-Pfosten aus der Gegenrichtung wird nicht als Ersatz einer anderen Mautschranke verwendet. Ampelbegrenzungen bleiben getrennt.
- Eine fehlende frühe Spurbestätigung kann einen eigenen normalen Halt mit Wechselreserve auf der belegten Zufahrt erzeugen. Der Anker liegt 60 m gerichtete Straßenlänge vor dem tatsächlichen Zufahrtsende. Die bisherige physische Grenze bleibt unverändert. Ein bereits zu später Haltepunkt wird nicht nachträglich vor den Lkw verschoben.
- Eine vorläufig gewählte manuelle Spur beendet die Automatiksuche nicht. Ist eine automatische Alternative aktuell physisch belegt und nur wegen zu hohen Tempos noch nicht erreichbar, bleibt bei nachgewiesener Zufahrt und möglichem Bremsweg der eigene normale Vorhalteanker aktiv. Der manuelle Pfad bleibt bis zum tatsächlich zulässigen Wechsel erhalten. Bei fehlendem Beleg, fehlender Wechselberechtigung oder unmöglicher neuer Vorbereitung wird kein endloser Vorbereitungshalt erzeugt.
- Hinter der Maut wird auch der Anschluss der folgenden Straße an das nächste Verbindungsstück geprüft. Eine echte passende Alternative mit denselben gerichteten Kontrollknoten kann den falschen Anschluss ersetzen. Andernfalls wird nur ein nachweisbar möglicher späterer Spurwechsel geplant. Kartenlücken werden nicht durch größere Toleranzen oder seitliche Sprünge geschlossen.
- Unbekannte Zahlungsart bleibt unbekannt. Eine gewählte manuelle Spur bei noch nicht freigegebener Automatikzufahrt wird nicht als ausschließlich manuelle Mautanlage bezeichnet.

Die neue Regelung behebt nicht automatisch einen im Spiel unwirksamen Pedalweg. **Gas und Bremse nach Alt+Tab in TruckersMP sind weiterhin nicht zuverlässig geklärt. Das Spiel muss für den Fahrtest im Vordergrund bleiben.** [Fokusbefunde](BACKGROUND_INPUT.md).

## Planung und Geometrie

Standardmäßig wird ein ausdrücklich angenommener Planungskorridor von **4,5 m Höhe und 3,4 m Breite** verwendet. Fehlende genaue Ladungsdefinitionen verhindern allein weder die Zahlungsarterkennung noch jede lokale Fahrentscheidung. Der Standard bestätigt aber keine vollständige Ladungs- oder Aufliegerfreiheit.

Bekannte niedrige Durchfahrten, blockierte Modellkurven, aktuelle Schranken und die gerichtete An-/Ausfahrt bleiben geprüft. Eine `SmallVehicles`-KI-Präferenz darf ausschließlich für ein belegtes automatisches Tor mit abgeschlossener physischer Modellprüfung überstimmt werden. `PlayerOnly`, fremde Tore, unbekannte Geometrie oder eine unerreichbare Zufahrt erhalten dadurch keine Freigabe.

Die optionale genaue Gespannprüfung benötigt zusammenpassende Fahrzeug-/Aufliegerposen, Kupplungs- und feste Achsdaten, auflösbare Ladungsdefinitionen und den gerichteten Navi-Bezug. Mehrfachauflieger, lenkbare Auflieger und unbekannte Haupt-Ladungsmodelle sind dort nicht allgemein unterstützt. Das langsame Bewegungsmodell gilt vorwärts auf nahezu ebenem Grund im Mautbereich bis 20 km/h. Die native Eigenschaft `CargoCoverageKnown` bleibt false.

Die [spielseitige Szenenerfassung](Native/ParkedScene/README.md) und die [TruckersMP-Geometriequelle](Native/TruckersMPGeometry/README.md) sind zusätzliche lokale Belege. `Complete` ist kein Nachweis abgeschlossener Spawns. Unbekannte Bewegung gilt weder als gemessene Physik noch als gesicherter Stillstand.

## Voraussetzungen und Paket

Die geprüfte lokale Grundlage ist ETS2LA `2026.11.5297`, ETS2/SDK `1.62`, Pathfinding `1.2.9`, Lane Assist `1.6.2`, PathLib `1.2.8`, PIDLib `1.0.4` und .NET 10. Andere Builds benötigen eine eigene Kompatibilitätsprüfung.

Für die Fahrt werden **Pathfinding, Lane Assist und Mautassistent** sowie deren Bibliotheken benötigt. Der native Adaptive Cruise Control ist optional und darf ausgeschaltet bleiben; sein alter normaler Pedalkanal ist im neuen Host unabhängig davon gesperrt.

Das 0.4.0-Update ersetzt vier ETS2LA-Dateien:

| Datei | Aufgabe |
| --- | --- |
| `Plugins/MautAssistent.dll` | Eigene Regelung, Maut- und Routenplanung |
| `ETS2LA.Game.dll` | Exklusive, zeit- und sitzungsgebundene Ausgabe und Quelldiagnose |
| `ETS2LA.State.dll` | Zusammenpassende Zustands-/Ausgabeschnittstelle |
| `Plugins/tumppi066.laneassist/LaneAssist.dll` | Geschützter Lenk- und Pausepfad |

Die vorhandene ACC-Datei wird bei diesem Update nicht ausgetauscht. Die bereits geprüften Spiel-DLLs `ets2la_plugin.dll` und `MautGeometry.dll` bleiben ebenfalls unverändert; sie müssen zur unterstützten Baseline passen. Das Host-Vorbereitungswerkzeug kann weitere Komponenten isoliert prüfen: daraus folgt keine pauschale Installationsanweisung.

Für den Austausch muss ETS2LA geschlossen sein. Der kontrollierte Installer prüft die konkrete Kombination, sichert die Ausgangsdateien und startet weder Apps noch Assistenz. Niemals gesamte Vorbereitungsordner oder `isolated-replay-*` installieren oder verteilen: diese enthalten absichtlich veränderte Testkopien. Ein installiertes Paket ist noch kein bestandener Stand- oder Fahrtest.

## Bedienung

| Eingabe | Funktion |
| --- | --- |
| F8 – Increase/RES | Start/erneute Routenprüfung; während der Prüfung selbst steuern |
| F9 – Cancel/MODE | Manuell übernehmen; ausgeschaltet außerdem Modus wechseln |
| F10 – Decrease/SET | Tempo verringern/setzen |
| F11 – Next/OK | Weiter/bestätigen |
| Physische Bremsachse | Ab 12 % manuell übernehmen; Loslassen startet nicht erneut |

Die Belegungen werden in ETS2LA Controls eingerichtet. Das Stream Deck kann dieselben Tasten senden. F12 bleibt frei, ein Overlay ist nicht erforderlich. Einstellungen liegen unter `%LOCALAPPDATA%\ETS2LA-Mautassistent\settings.json`.

Nach dem Update zuerst Version, Modulbereitschaft und frische Quellen im Stand mit Feststellbremse prüfen. Eingebauten Spieltempomat selbst ausschalten. Dann F8 und die tatsächliche Rückgabe von Lenkung/Pedalen durch Bremse und F9 testen. Erst danach folgt eine überwachte Fahrt mit geschlossener und geöffneter Mautschranke, Ausfahrt und Anschlussstraße. Kein unbeaufsichtigter Fahrbetrieb.

## Offline prüfen

Im Projektordner mit PowerShell 7 und .NET 10:

```powershell
$env:EtsRoot = 'C:\Pfad\zu\ETS2LA\current'
./Prepare-References.ps1
dotnet run --project CoreTests -c Release
dotnet build Plugin/MautAssistent.csproj -c Release
dotnet run --project Tests -c Release
```

`Prepare-References.ps1` prüft lokale Herstellerbibliotheken und verändert keine Installation. Das Plugin muss das Manifest des genau passenden vorbereiteten Hostpakets einbetten. Die [Hostprüfungen](Tools/HostCompatibility) laufen gegen isolierte Kopien und öffnen keine Spielspeicher oder realen Ausgabekanäle.

Die geschlossene synthetische Geschwindigkeitsprobe koppelt echten Router und eigenen Regler an ein unabhängiges beladenes Fahrzeugmodell mit 150 ms Pedalantwort. Aus 130 m Zufahrtsrest bei 77 km/h erreicht sie den tatsächlich zulässigen Wechselbeginn bei etwa 62 m und 19,5 km/h. Das beweist weder eine ganze reale Spuränderung noch Spielphysik oder eine autonome Mautpassage.

Die private Kartenprobe für 0.4.0 reproduziert eine 4,499-m-Routenlücke hinter der Maut und findet einen realen passenden Anschluss ohne Spurwechsel oder Geometrieveränderung. Die vollständigen damaligen Nachfolge-UIDs fehlen; das ist eine geometrische Rekonstruktion des gespeicherten Befunds, kein historischer oder neuer Fahrnachweis. Die damalige Passage mit manuellem Gas und anschließend gemeldetem Trackingverlust zählt nicht als autonome Durchfahrt.

## Grenzen und Diagnose

- Keine nachgewiesene Abdeckung aller Mautanlagen/Länder oder aller bisherigen Fahrprobleme.
- Baustellen, Verkehrsschilder, Sonderladung und sämtliche Hindernisse werden nicht allgemein zuverlässig umfahren.
- Die Verkehrs-/Parkquellen sind begrenzt und können unbekannt, verspätet oder unvollständig sein. Die Regelung ersetzt keine vollständige Wahrnehmung.
- Ein geplanter Halt kann bei fehlenden Belegen weiterhin notwendig sein. Notbremsung bleibt für konkrete Fehler-/Gefahrenfälle erhalten.
- Hintergrund-Pedale in TruckersMP und eine vollständige reale Prüfung der neuen Regelung bleiben offen.

Rein lesende Aufnahmen sind mit `Tools/Record-TollDrive.ps1`, Auswertungen mit `Tools/Measure-TollDrive.ps1` möglich. Private Aufnahmen, extrahierte Spielmodelle, Hersteller-DLLs und Buildausgaben werden nicht veröffentlicht. Ältere Nachweise stehen in [CHANGELOG.md](CHANGELOG.md), [TOLL_COVERAGE.md](TOLL_COVERAGE.md), [VALIDATION.md](VALIDATION.md) und den versionsbezogenen Reviews.
