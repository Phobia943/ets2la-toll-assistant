# Lokale Hostanpassung 0.3.18-host-3

Das Werkzeug bereitet vier zusammengehörige Kopien exakt geprüfter ETS2LA-Komponenten vor. Es installiert nichts. Dieser Stand ist ein lokaler Kandidat; gemeinsame Stand- und Fahrprüfung fehlen. Herstellerbibliotheken, vorbereitete DLLs und private Berichte gehören außerhalb des öffentlichen Repositorys. [Kandidatenumfang und kritischer Review](../../REVIEW-0.3.18.md).

Der neue Laufzeitstand ruft die getrennte Gespannvorhersage inzwischen aus der echten Pluginregelung auf. Der frühere v22-Befund einer großen Gespannkugel und globaler Unknown-Motion-Ablehnung ist historisch. Lokale Modell-/Bewegungsprüfung, aktuelle gemessene Boxen für die akute Notbremsung und Modellüberwachung bleiben vom Host-Commit-Vertrag getrennt. Finaler Plugin-Seal, erneute Gesamtprüfungen und die abgegrenzte Offline-FR-Laufzeitgegenprobe liegen vor; die Sieben-Datei-Kombination ist lokal installiert und hashverifiziert. Apps wurden nicht gestartet, die neue Stand-/Fahrprüfung steht aus.

## Ausgabeautorität

Die Anpassung erhält die Korrektur für ausgelassene native ACC-Pedalausgaben bei sehr langsamer Fahrt und neutralisiert abgelaufene native Achsen, ohne andere gültige Bremsbesitzer zu verdünnen.

`ControlChannelDefinition` trägt `MautEpoch` und `MautIssuedAt`. Der Ursprung wird beim Eintritt in den echten ACC-/Lane-Tick erfasst, vor der Berechnung, und unverändert durch die drei Publisher einschließlich Pause-Helfer gereicht. Einlieferung **und** physischer Frame verlangen eine passende Sitzung und höchstens 200 ms Alter. Ein später Publish darf den Ursprung nicht erneuern. Die gespeicherte Definition wird kopiert, damit eine wiederverwendete Publisher-Instanz ihr Alter nicht nachträglich umlabelt.

Je nativer Kanal-ID bleibt die höchste akzeptierte Ursprungszeit als Watermark bestehen, auch wenn der Kanal inzwischen entfernt oder abgelaufen ist. Ein älteres oder wiederholtes Ergebnis darf einen neueren Wert nicht ersetzen oder einen gelöschten Besitzer erneut aktivieren. Sitzungs-/Moduswechsel widerrufen die vorherige Autorität.

Die zusätzliche Halteschnittstelle hat **vier Argumente**:

```text
MautSetPlannedHold(epoch, key, floor, issuedAt)
```

Sie akzeptiert nur eine aktuelle passende Sitzung, ein höchstens 200 ms altes Ergebnis und eine neuere Ursprungszeit als die bereits angenommene. Auch ein gültiges Clear behält diese Zeit als Watermark; eine vorher begonnene Berechnung kann den Halt anschließend nicht erneut setzen. Die Leihe endet bei `issuedAt + 250 ms`, nicht 250 ms nach verspäteter Einlieferung. F9 und ein unpassender Modus entziehen die Halteautorität. Der Floor wird nach der normalen Kanalgewichtung angewandt und darf eine stärkere Bremsung nicht schwächen.

Das Plugin muss seine Branch-/Scene-/Bindungs-/Proof-Haltegründe vor diesem einen Commit zusammenführen. Ein neuer Hostmarker allein beweist weder die aktuellen Eingangsdaten noch die richtige Szenenwahrnehmung.

## Bauen und prüfen

Voraussetzung sind .NET 10, die bekannten lokalen Herstellerbibliotheken und Referenzen. Mono.Cecil ist auf 0.11.6 festgelegt; alternativ kann `CecilRoot` die Bibliotheken des lokalen .NET-ILLink-Werkzeugs angeben.

Aus dem Repositoryordner, mit eigenen Pfaden:

```powershell
$env:EtsRoot = 'D:/ETS2LA/current'
$originals = 'C:/privat/unveraenderte-gepruefte-originale'
$prepared = 'C:/privat/neue-host3-vorbereitung'
dotnet build Tools/HostCompatibility -c Release -p:EtsRoot=$env:EtsRoot
dotnet ./Tools/HostCompatibility/bin/Release/net10.0/HostCompatibility.dll prepare $env:EtsRoot $prepared --originals $originals
dotnet ./Tools/HostCompatibility/bin/Release/net10.0/HostCompatibility.dll verify $env:EtsRoot $prepared --originals $originals
```

`--originals` bezeichnet die unveränderten vier Ausgangsdateien mit den im Werkzeug festgelegten Originalprüfsummen. Der laufende Referenzordner kann bereits eine ältere Anpassung enthalten und ersetzt diesen Nachweis nicht. Ohne den optionalen Schalter wird derselbe Ordner für Referenzen und Originale benutzt.

`prepare` verweigert vorhandene Ausgabeverzeichnisse, Ziele innerhalb der Installation und unbekannte Originalbuilds. Es erstellt genau die Produktionspfade und `compatibility.json`. Jede Vorbereitung erzeugt neue MVIDs. Das Plugin muss mit dem Manifest **genau dieser** Vorbereitung neu gebaut werden; eine erneute Vorbereitung macht ältere Siegel nicht passend.

`verify` prüft erst SHA256, MVID und gemeinsamen Revisionsmarker aller vier Produktionsdateien. Danach erzeugt es separate `isolated-replay-*`-Testkopien. Dort sind Konstruktoren/Singletons mit Spiel- oder Ausgabepfaden gesperrt bzw. injiziert, Schreibsenken auf Testwörterbücher umgeleitet und Speicheröffnungs-/Ausgabe-Threads verboten. Teile der Constraint-Eingänge werden kontrolliert eingespeist. Der echte Tick, Publisher und Hostframe bleiben Gegenstand der Prüfung; dies ist kein Fahr- oder Wahrnehmungsnachweis.

## Optionale echte Plugin-Commit-Probe

Erst das passende `compatibility.json` als `Plugin/HostCompatibility.json` übernehmen und das Plugin gegen die geprüfte Referenzkombination bauen. Dann kann dieselbe isolierte Prüfung zusätzlich die echte Commit-Brücke ausführen:

```powershell
$env:MAUT_PLUGIN_CANDIDATE = (Resolve-Path './Plugin/bin/Release/net10.0/MautAssistent.dll').Path
dotnet ./Tools/HostCompatibility/bin/Release/net10.0/HostCompatibility.dll verify $env:EtsRoot $prepared --originals $originals
$env:MAUT_PLUGIN_CANDIDATE = $null
```

`PluginHoldReplay` kopiert die Eingabedatei in den neuen Sandboxordner, versiegelt sie durch einen Leselock und prüft genau diese Kopie vor dem Laden. Manifest, Modulkennungen, isolierter Assemblykontext und erlaubte Initialisierung werden geprüft; unbekannte Modulinitialisierung verweigert den Test. Der echte Plugin-/Fault-Instanzkonstruktor und Treiber-/SDK-Schleifen werden nicht ausgeführt. Kontrollierte Floor-Felder speisen die unveränderten echten `CommitPlannedHolds`-/`ClearSceneHold`-Methoden und die reale isolierte Hostausgabe. Die geprüfte harmlose SDK-DTO-Initialisierung ist gesondert begrenzt.

`plugin-hold-replay.json` nennt den tatsächlich ausgeführten Plugin-SHA256/MVID und den Host-MVID. Die erneute finale Probe umfasst 37 zusätzliche Assertions, insgesamt 120 mit den 83 Hostprüfungen, gegen SHA256 `35542A74D0786DD459C686E0CE1D1B6EEAED8BAE59C8C1698A9235EAEB087CE5` und MVID `4c065272-6d4e-4c65-afff-cb931b24a0bb`. Ein späterer Plugin-Build benötigt eine neue Prüfung; ältere v22-/v20-Berichte werden nicht übertragen. Der Test prüft Owner-Clear, Maximum statt Verdünnung, beide Ausgabewege, F9, veralteten Ursprung und einzeln ungültige Produktzustände. Er erzeugt keine reale Branch-/Scene-Erkennung aus einem Spielbild. Die separate Produkt-Laufzeitprobe im [Review](../../REVIEW-0.3.18.md) ist ebenso kein vollständiger Erfassungs-/Ausgabefahrtest.

## Produktionsdateien und Replaykopien strikt trennen

**Niemals den gesamten Vorbereitungsbaum, `isolated-replay-*` oder instrumentierte DLLs installieren oder verteilen.** Diese Test-DLLs besitzen andere Prüfsummen und absichtlich gesperrte/umgeleitete Laufzeitwege. Nur die vier in `compatibility.json` aufgeführten Produktionspfade gehören zusammen mit dem final versiegelten Plugin, der passenden spielseitigen SDK-Datei und `MautGeometry.dll` in das **exakt geprüfte Sieben-Datei-Paket**.

Die passende Geometriequelle `MautGeometry.dll` gehört jetzt zum gemeinsamen Austausch: 16-ms-QPC-Abstand, präzise UTC am Capturebeginn, unveränderte 256-Byte-ABI. Nur `MautAssistent.dll` auszutauschen ergibt keine passende Kombination. Der getrennte neue Austausch verlangt ein separat gehashtes Runtimegate, geschlossene Anwendungen, vollständige geprüfte Sicherungen und Staging aller sieben Dateien vor dem ersten Commit. Gate und Hash werden im Journal gesichert und vor Staging/Commit erneut geprüft; der Prozessguard steht direkt vor Commit-/Rollback-Moves. Die alte Sechs-Datei-Sperre bleibt erhalten. Jeder einzelne Dateiaustausch ist atomisch; ein gemeinsamer sieben Dateien umfassender atomischer Wechsel wird nicht behauptet. Die lokale Transaktion ist abgeschlossen und alle sieben Hashes stimmen; der Installer startete keine Apps oder Fahrassistenz und änderte keine Eingaben.

## Rollen und Grenzen

Der Komfortmodus kann bei einer kleinen Wunschtempo-Korrektur tatsächliches Gas 0 und Bremse 0 veröffentlichen. Ein Beschleunigungsziel von null allein würde wegen Vorsteuerung/Reglerintegral nicht genügen. Er benötigt frische und passende native Route-/Constraint-Eingänge, vollständige Kurvenabdeckung sowie gesonderte eigene Freigabe. Unbekannte Container, Haltegründe, Recovery und negative Sicherheitsbegrenzungen erteilen diese Freigabe nicht. Eine Gefälleverriegelung überlebt einen Workerwechsel.

Die älteren nativen Verkehrs-/Semaphorencontainer haben keinen vollständigen eigenen Produzentenzeitvertrag. Deren erfolgreiche Lektüre ersetzt keine komplette Verkehrswahrnehmung. Die zusätzliche neue Park-Szene ist ebenfalls auf tatsächlich gelesene geladene Körper/Bereiche begrenzt und hat keinen SpawnFinished-Beweis. Diese Hostanpassung löst weder allgemeine Hindernisvermeidung noch das Fokus-/Hintergrundpedalproblem unter TruckersMP. Die neue Kombination muss noch im realen Spiel geprüft werden.
