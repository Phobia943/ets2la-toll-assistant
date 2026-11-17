# Lokale Hostanpassung 0.4.0-host-1

Das Werkzeug bereitet geprüfte Kopien von ETS2LA-Komponenten vor. Es installiert und startet nichts. Herstellerbibliotheken, vorbereitete DLLs und private Aufzeichnungen bleiben außerhalb des öffentlichen Repositorys. Die hier beschriebene Prüfung ist isoliert; sie ersetzt keine Fahrt im Spiel.

## Exklusive normale Pedalquelle

Nur `local.mautassistent.longitudinal` darf normale Gas-/Bremsbefehle liefern. Die eigene Pluginregelung arbeitet ohne nativen ACC-/PID-Regler. `AdaptiveCruiseControl.Acceleration` wird dauerhaft abgewiesen, auch mit aktueller Sitzung und Ursprungszeit. Fremde Pedalwerte werden nicht gewichtet oder eingemischt.

Der Kanal erwartet `aforward` zwischen 0 und 1 und `abackward` zwischen −1 und 0. Beide dürfen nicht gleichzeitig aktiv sein. Ungültige oder nicht endliche Werte verwerfen den gesamten Pedalbefehl. Gewichte ändern die exklusive Achse nicht. Der Kanal kann keine Lenkung einschleusen; die geschützte Lane-/Pauseausgabe bleibt getrennt.

`local.mautassistent.fault-brake` ist eine unabhängige Ausnahme ausschließlich für Bremsung. Ein gültiger negativer Wert dominiert die normale Ausgabe und setzt die Lenkachse neutral. Positive oder nicht endliche Faultwerte erzeugen kein Gas und ersetzen keinen gültigen Bremsbefehl. Die Fehlerbremsung bleibt bei ausgeschalteter Assistenz verfügbar; die echte manuelle Übernahme gibt sie frei.

## Ursprung, Quellen und Widerruf

`ControlChannelDefinition` trägt `MautEpoch` und `MautIssuedAt`. Der eigene Tick erfasst beide vor seiner Berechnung. Einlieferung und physischer Frame verlangen aktuelle Sitzung, passenden Fahrmodus und höchstens 200 ms ursprüngliches Alter. Ein später Publish erneuert den Ursprung nicht. Definitionen und primitive Pedalwerte werden kopiert. Je geschützter Kanal-ID verhindert eine Watermark, dass ältere oder wiederholte Ergebnisse neuere Befehle ersetzen.

Der eigene Kanal trägt zusätzlich `MautSourceEpoch` und `MautSourceAt`: Quellenrevision und Minimum der tatsächlich ausgewerteten Verkehrs-, Park-, Signal- und Torursprünge. Ingress und physischer Frame prüfen die Quellenrevision. Positives Gas benötigt auch dort einen positiven, nicht zukünftigen, höchstens 200 ms alten Quellenursprung. Neue Providerdaten verlängern keinen gespeicherten Befehl. Ein normaler Bremsplan ohne Gas darf bei unbekannter Quelle `MautSourceAt = 0` tragen.

Provider erfassen Uhr und Quellenrevision vor der Decodierung. Danach speichern sie eine tiefe eigene DTO-Kopie mit unverändertem Ursprung vor dem synchronen Event-Publish. Decodierung, Kopieren und Callbacks laufen außerhalb des Ausgabelocks. Nur abschließender Store und Close/Catch-Widerruf verwenden dessen kurze gemeinsame kritische Sektion. Ein vor Close begonnener Decode kann anschließend weder seine alte Quelle neu attestieren noch einen neuen gültigen Frame überschreiben. Erfolgreicher planmäßiger Reconnect erneuert keinen Ursprung.

Auch die Pluginbeobachtung übernimmt ursprüngliche rohe Befehls- und Quellenstempel. `PedalAge` beschreibt aktuelle Quellenautorität; `PedalProducerAge` beschreibt den unveränderten ursprünglichen Tick samt Assistenzepoch für den 150-ms-Produzententimer. Ein Quellenwechsel entzieht Gas sofort, ist aber kein vorzeitiger Produzentenausfall. Ungültige neue Events verjüngen die Beobachtung nicht. Bleibt ein neuer angenommener Plan länger als 150 ms aus, fordert der unabhängige Watchdog Fehlerbremsung an. Verfügbare Schnittstellen und angeforderte Bremse beweisen keine tatsächliche Bremswirkung.

Die Halteschnittstelle lautet:

```text
MautSetPlannedHold(epoch, key, floor, issuedAt)
```

Sie verlangt aktuelle Sitzung, höchstens 200 ms ursprüngliches Alter und neuere Ursprungszeit. Die Leihe endet bei `issuedAt + 250 ms`; auch ein gültiges Clear erhält die Watermark. Das Plugin führt Branch-/Scene-/Signal-/Proof-Haltegründe vorher zusammen. Der Floor wird nach Auswahl der exklusiven Pedalquelle angewandt und schwächt stärkere normale oder Faultbremsung nicht ab.

## Bauen und isoliert prüfen

Voraussetzung sind .NET 10 und die festgelegten lokalen Herstellerreferenzen. Mono.Cecil ist auf 0.11.6 festgelegt; alternativ kann `CecilRoot` die Bibliotheken des lokalen ILLink-Werkzeugs angeben.

```powershell
$env:EtsRoot = 'D:/ETS2LA/current'
$originals = 'C:/privat/unveraenderte-gepruefte-originale'
$prepared = 'C:/privat/neue-host-040-vorbereitung'
dotnet build Tools/HostCompatibility -c Release -p:EtsRoot=$env:EtsRoot
dotnet ./Tools/HostCompatibility/bin/Release/net10.0/HostCompatibility.dll prepare $env:EtsRoot $prepared --originals $originals
dotnet ./Tools/HostCompatibility/bin/Release/net10.0/HostCompatibility.dll verify $env:EtsRoot $prepared --originals $originals
```

`--originals` bezeichnet die vier unveränderten Ausgangsdateien mit festgelegten Prüfsummen. `prepare` verweigert vorhandene Ausgabeverzeichnisse, Installationsunterordner und unbekannte Originalbuilds. Jede Vorbereitung erzeugt neue MVIDs; das Plugin muss genau deren `compatibility.json` einbetten.

`verify` prüft SHA256, MVID und gemeinsamen Revisionsmarker. Instrumentierte Kopien entstehen getrennt unter `isolated-replay-*`: Speicheröffnungen, Produktionskonstruktoren und Ausgabeschleifen sind gesperrt; physische Schreibstellen schreiben in Testwörterbücher. Der geprüfte v6-Lauf enthält **118 Hostassertions** für beide Senken, exklusiven Kanal, tatsächlich veröffentlichende abgewiesene native ACC-Ticks, fremde Besitzer, Faultdominanz, ungültige Mischpedale, ursprüngliche Quellen-/Befehlsfristen, Close-Widerruf, Watermarks, F9 und den geschützten Lane-Pausepfad.

Für die echte Pluginbrücke und synthetischen Torkarten zusätzlich vor `verify` setzen:

```powershell
$env:MAUT_PLUGIN_CANDIDATE = (Resolve-Path './Plugin/bin/Release/net10.0/MautAssistent.dll').Path
$env:MAUT_CANONICAL_FIXTURE = (Resolve-Path './Tests/bin/Release/net10.0/Tests.dll').Path
```

Beide Dateien werden vor dem Laden kopiert, versiegelt und auditiert; der Programmeinstieg der Tests.dll läuft nicht. Die Commit-Probe baut ihre Felder ohne Produktkonstruktoren auf. Die echte Tick-Probe verwendet ausschließlich den vorher auditierten DI-Konstruktor mit privaten Eingaben und Senken. Produktionskonstruktor, Init, SDK-Schleifen, Speicheröffnungen und Produktionstimer laufen nicht.

Der finale Lauf kombiniert **118 Host-, 63 Commit-/Lenk- und 42 Tick-/Quellen-/Tor-/Übergangsassertions zu 223 Prüfungen**. Er enthält reale Tick-/Hostlock-Barrieren, Quellenalterung trotz neuer Frames, Close während Decode, unveränderte rohe StorePedals-Stempel, F9 sowie den unabhängigen Pedalwatchdog. Die RawStore-Schicht wird separat mit dem unveränderten tatsächlich erzeugten Tickevent bei produktivem Schutz geprüft; der gesamte Fixture-Tick wird nicht als produktiv gestartete Instanz ausgegeben.

Die kanonische Torkarte prüft tatsächlichen Router, Tick und Öffnungswartezeit: eigenes offenes Tor 10 mit näherem geschlossenem Gegentor 15 gibt Gas, vertauschte Zustände bremsen an beiden Senken. Eine zusätzliche echte OnPath-Probe wechselt von manueller zu spät erreichbarer automatischer Spur. Der alte Vergleichsbuild verlor dabei seine Fahrentscheidung und löste Fehlerbremsung aus; der korrigierte Build leitet die neue Entscheidung unter derselben Route ab und bleibt im Fahrmodus. PathLib erhält ausschließlich eine private Telemetrie-DTO mit passenden Fixturekoordinaten, keinen echten Telemetriesingleton.

21 isolierte periodische Mautticks mit gültigem Nachbarfahrzeug benötigten lokal 0,798–28,795 ms; das größte vorherige Befehlsalter bei Folgeausgabe war 86,255 ms. Diese Messung betrifft eine synthetische Karte und private Quellen, keine Live- oder allgemeine Laufzeitgarantie.

## Produktionsdateien und Testkopien trennen

**Niemals den gesamten Vorbereitungsbaum oder instrumentierte Replay-DLLs installieren oder verteilen.** Das Werkzeug erzeugt vier Hostdateien, darunter eine native ACC-Kopie für die Abweisungs-Gegenproben. Die eigentliche Umstellung benötigt nur **drei Hostdateien (`ETS2LA.Game.dll`, `ETS2LA.State.dll`, `LaneAssist.dll`) plus Mautplugin**, insgesamt vier Austauschdateien. Die vorhandene native ACC-Datei bleibt bei dieser Installation unverändert; natives ACC ist keine Laufzeitabhängigkeit der eigenen Regelung. Sein Pedalkanal bleibt auch bei versehentlicher Aktivierung gesperrt.

Spielseitige SDK-/Geometriekomponenten müssen zur geprüften Baseline passen; dieses Werkzeug tauscht sie nicht aus. Ein späteres Installationspaket benötigt genaue Ziel-/Hashlisten, Sicherung, Prozessschutz und Rücksetzung. Die eigene Regelung kann auf freier Strecke Gas 0/Bremse 0 zum Rollen verlangen. Harte negative Sicherheitsgrenzen, Stopgründe und unbekannte Routen erteilen keine Coastfreigabe. Hintergrund-/Fokusfahrt unter TruckersMP, vollständige Verkehrswahrnehmung, Baustellen und beliebige Fahrzeug-/Ladungsmodelle sind durch diesen Hostvertrag nicht gelöst. Die Offlineprüfungen sind keine allgemeine Fahrfreigabe.
