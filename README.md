# ETS2LA Mautassistent

Experimentelle Erweiterung für **ETS2LA** in Euro Truck Simulator 2: langsam an Mautstellen heranfahren, eine bestätigte Lkw-Spur verwenden und nach dem Öffnen der Schranke verzögert weiterfahren.

Die Erweiterung ergänzt Pathfinding, Lane Assist und Adaptive Cruise Control. Sie ist kein eigenständiges Fahrsystem. Oberfläche und Meldungen sind auf Deutsch.

## Funktionen

- Standardmäßig auf 20 km/h vor der Schranke abbremsen. Die Überwachung beginnt mindestens 200 m vor der zugeordneten Schranke.
- Automatische Wahl einer erreichbaren Lkw-Spur mit bevorzugter automatischer Abrechnung.
- Freigabe erst nach zwei Sekunden durchgehend vollständig geöffneter Schranke.
- Durchfahrt mit höchstens 10 km/h bis 30 m hinter der Schranke.
- Speicherung der Spur und Zahlart pro Mautanlage, Einfahrt und Ausfahrt.
- Automatische Vorbereitung nach dem Start mit mindestens einer Sekunde Stillstand, frischen Spieldaten, erfolgreicher Routenprobe und geprüfter ACC-Anbindung. Die Fahrt beginnt mit F8.
- F8 kann während der Vorwärtsfahrt eine neue Routenprüfung und anschließende Übernahme anfordern. Während der Prüfung bleibt die Fahrt manuell. F9 bricht auch einen offenen Startversuch ab.
- Physische Bremse als manuelle Übernahme: „Bremse – manuelle Übernahme“ in ETS2LA Controls der eigenen Bremsachse zuordnen. Ab 12 % schalten Lenkung und Tempomat ab; Loslassen startet nicht automatisch erneut. Automatische Bremsausgaben werden nicht als Benutzereingabe ausgewertet.
- Startversuche ohne Vorbereitung werden bei eingeschaltetem Plugin zurückgewiesen, auch nach einer Störung. F9 bleibt für die manuelle Übernahme verfügbar.
- Fehlerbremsung bei Trackingverlust; Gegenrichtungsprüfung während der Lenkassistenz, auch außerhalb von Mautstellen.
- F9 gibt die Steuerung zurück und löst eine aktive Fehlerbremsung ausdrücklich zur manuellen Übernahme.

Die Erweiterung liest die Zahlart unterstützter Mautmodelle aus den Spiel-Hookups und ordnet sie einer eindeutigen Spurkurve zu. Für die Spurwahl prüft sie den vollständigen Weg auf Pkw- und Eskorte-Beschränkungen sowie die Anschlüsse vor und nach der Anlage. Ab 0.3.12 berücksichtigt sie die sichtbare Modellvariante und zugeschaltete Teile bei einer statischen Geometrieprüfung. Der Prüfkorridor ist 3 m breit und 6 m hoch; tatsächliche Fahrzeugmaße werden nicht gemessen. Breitere oder höhere Ladungen erfordern manuelle Übernahme. Bei ausreichendem Weg kann der native Planer den Wechsel frühzeitig auf der Zufahrt einleiten. Mehrdeutige Modellmarkierungen oder unpassende Anschlüsse führen zum Halt vor der Aufteilung.

Wenn eine passende manuelle Lkw-Spur die einzige erkennbare Option ist, wählt der Assistent sie und hält vor der Schranke. Die Zahlung bleibt beim Spieler. Wenn der Zahlpunkt weiter vorne liegt, mit F9 übernehmen und manuell vorrollen.

## Entwicklungsstand

**0.3.16 ist als Offline-Testfassung vorbereitet.** Eine zusätzliche Kurvenvorschau begrenzt das Tempo bereits auf der Zufahrt, berechnet außerhalb der Fahrsteuerung und mit begrenzter Zusatzverzögerung. Außerdem behält der Modelllader jetzt die zuvor verlorenen Schrankenpositionen für passende manuelle Zahlhaltebereiche. Die Kurvensimulation und die beiden gespeicherten griechischen Kartenwiederholungen sind geprüft; noch kein Fahrnachweis. [Änderungen, Annahmen und offene Punkte](VERSION_0316.md).

**0.3.15 ist als Offline-Fassung vorbereitet, noch nicht installiert.** Sie warnt bei anhaltend fehlender Spielreaktion auf frische Bremsbefehle. Ein separates, ausschließlich lesendes TruckersMP-Modul für Fahrzeug-/Auflieger-Modellmaße und ein optionaler Statusleser sind gebaut. Die Messungen verändern keine Fahrentscheidung; ein Test im tatsächlichen TruckersMP-Client fehlt. Hintergrund-Pedale und vorausblickende Baustellenerkennung sind weiterhin offen. Umfang, Nachweise und Grenzen: [VERSION_0315.md](VERSION_0315.md).

**0.3.14 ist offline geprüft und lokal installiert.** Sie lädt Mautmodelle früher, verwendet bei der Startvorschau dieselben Freiraumprüfungen wie während der Fahrt und verlagert die zusätzliche Statusgeometrie aus dem Fahrzyklus. Eindeutige gerade Modell-Zahlzonen können einen früheren manuellen Haltepunkt liefern; der bisherige Sicherheitsabstand wird dabei nicht verkleinert. Die beiden griechischen Offline-Spurwahlen bleiben gültig. 84 Integrations- und 46 Logikprüfungen bestehen. Noch nicht gestartet oder gefahren. Umfang und offene Punkte: [VERSION_0314.md](VERSION_0314.md).

**0.3.13 führte die breitere Mauterkennung ein.** Sie verwendet den Prefab-Definitionskatalog unabhängig vom Modellordner und liest die Zahlungsart aus geprüften Maut-Units. Ältere manuelle Zahlspuren bleiben manuell. Zwei gespeicherte griechische Zufahrten wählen mit den aufgezeichneten SDK-Schrankenpositionen und -zuständen eine verbundene AutoPASS-Spur. Bei breiten Durchgängen müssen beide getrennten Schrankenflügel offen sein. Einer der beiden Kartenfälle umfasst nur den exakt rekonstruierten Abschnitt aus Zufahrt, Mautanlage und folgendem Straßenstück; ein späterer Abzweig fehlt. Diese Offline-Wiederholungen ersetzen keine echte Durchfahrt. Eine Abdeckung aller Länder ist nicht belegt; Einzelheiten stehen in [TOLL_COVERAGE.md](TOLL_COVERAGE.md).

Eine neue Kollisionsabschaltung reagiert auf einen abrupten Geschwindigkeitsverlust mit gleichzeitig neuem Kabinen-/Fahrgestellschaden. Sie soll weiteres automatisches Gasgeben nach einem Aufprall unterbinden. Sie erkennt keine Hindernisse vorab und verhindert den ersten Aufprall nicht. Die Bewegungsdiagnose zeichnet zusätzlich Schadenswerte, tatsächliche Spielpedale und Simulationszeit auf.

Die Modellprüfung läuft mit einer begrenzten Warteschlange im Hintergrund und eigenen Archivzugriffen. Ein absichtlich blockierter Modelllader hält den Routenaufruf nicht auf. Fehlende oder unbekannte Geometrie ergibt keine Freigabe. Die Diagnose-Schreibzugriffe bleiben wie seit 0.3.10 getrennt; die Diagnose-Zeitkorrektur aus 0.3.11 bleibt erhalten. Die manuelle Übernahme wurde zuletzt mit 0.3.11 und der Standard-Pedalausgabe tatsächlich bestätigt; für die neue DLL steht sie noch aus.

Mit **0.3.9** wurde am 13. November in TruckersMP eine rollende Mautdurchfahrt erfasst und vom Fahrer bestätigt: passende Lkw-Spur mit automatischer Abrechnung, etwa 20 km/h bei der Zufahrt, Wartephase nach dem Öffnen, Passage mit knapp 10 km/h und stabile Weiterfahrt. Die Aufzeichnung ist mit rund zwei Sekunden Offenzeit vor Freigabe vereinbar. Ein längerer Stillstand vor einem geschlossen bleibenden Tor wurde dabei nicht geprüft. F8-Start und physische Bremsübernahme wurden separat bestätigt.

Ein späterer Kurvenabbruch nach der Fahrzeugreparatur zeigt einen weiteren Mechanismus: Die native Spurhilfe verschiebt ihre Sollpunkte um 1,5 m und überschreitet dadurch ihre eigene Abstandsgrenze. Der verifizierte vordere Routenabschnitt reproduziert etwa 1,2 m Abstand ohne Zusatzkorrektur und 2,6 m mit ihr bei einer Grenze von 2,25 m. In der lokalen Einstellung der geprüften Lane-Assist-Fassung ist deshalb `WidenTurnsWithTrailer = false` gespeichert; die Wirkung ist noch im Spiel zu prüfen. Diese Änderung wird vom Mautplugin nicht automatisch für andere Installationen vorgenommen. Die Abschaltschwellen wurden nicht gelockert.

81 Integrationstests, 45 unabhängige Logiktests, 44 Aufnahmeprüfungen und drei Prüfungen der Archivinventur bestehen. Diese Suiten belegen unterschiedliche Teile und ersetzen keine Fahrfreigabe. Bremsweg mit Beladung, Halt vor geschlossen bleibender Schranke und die aktualisierte Kombination müssen noch praktisch geprüft werden. Hintergrund-Gas und -Bremse unter TruckersMP bleiben ungelöst. Nachweise und Grenzen: [VALIDATION.md](VALIDATION.md), [BACKGROUND_INPUT.md](BACKGROUND_INPUT.md). Die Entwicklungshistorie steht in [CHANGELOG.md](CHANGELOG.md).

Die lokale Pedalausgabe wurde nach wiederholtem starkem Luftdruckverlust auf **Standardausgabe** (`EnableModernOutputForPedals = false`) zurückgestellt. Ein vierminütiger Vergleich derselben Fahrt enthält normale automatische Bremsphasen, ausreichend Luftdruck und bestätigte manuelle Übernahme. Beim vom Fahrer bestätigten Heraustabben bleibt dagegen auch die Standard-Bremsausgabe wirkungslos. Deshalb ist diese Konfiguration nur bei Spiel im Vordergrund weiter zu prüfen; sie löst die geforderte Hintergrundfahrt nicht. Der Mautassistent verändert diese globale Einstellung nicht selbst.

Die Erweiterung prüft sichtbare statische Modellflächen im genannten Korridor, misst aber keine Fahrzeugmaße und simuliert keine vollständige Auflieger-Kollisionshülle. Unbekannte Kindmodelle, animierte Geometrie und nicht unterstützte Archivquellen bleiben ohne automatische Freigabe. Unbekannte oder unpassende Mautspuren erfordern eine manuelle Übernahme. Der eingestellte Haltabstand bezieht sich auf den Telemetriepunkt des Lkw, nicht auf seine Stoßstange oder einen geprüften manuellen Zahlpunkt. Ein App- oder SDK-Ausfall kann keine verlässliche Bremsausgabe garantieren.

## Voraussetzungen

Geprüfte lokale Kombination:

| Komponente | Version |
| --- | --- |
| ETS2LA | 2026.11.5297 |
| Spiel/SDK | ETS2 1.62 / offizielles SDK 1.62 |
| Pathfinding | 1.2.9 |
| Lane Assist | 1.6.2 |
| Adaptive Cruise Control | 2.1.1 |
| PathLib | 1.2.8 |
| PIDLib | 1.0.4 |
| .NET SDK | 10.0 |

ETS2LA samt Spielanbindung, Pathfinding, Lane Assist und ACC muss separat eingerichtet sein. Die Projektquellen enthalten keine Herstellerbibliotheken, Spieldateien oder fertigen Stream-Deck-Profile. Es besteht keine offizielle Zugehörigkeit zu ETS2LA, SCS Software oder TruckersMP; eine allgemeine TruckersMP-Kompatibilitätszusage wird nicht gegeben.

## Bauen unter Windows

Im geklonten Projektordner in PowerShell ausführen. Den Beispielpfad durch den tatsächlichen Ordner ersetzen, der `ETS2LA.Game.dll` enthält:

```powershell
$env:EtsRoot = 'C:\Pfad\zu\ETS2LA\current'
.\Prepare-References.ps1
dotnet run --project Tests -c Release
dotnet build Plugin/MautAssistent.csproj -c Release
```

`Prepare-References.ps1` prüft die bekannten Bibliotheksversionen anhand ihrer Prüfsummen und kopiert die benötigten Referenzen ausschließlich in den lokalen, von Git ausgeschlossenen Ordner `References`. Bei abweichenden Versionen muss die Kompatibilität vor einer Anpassung geprüft werden. Die ETS2LA-Installation wird dabei nicht verändert.

Das Ergebnis liegt unter `Plugin/bin/Release/net10.0/MautAssistent.dll`.

## Installation und Bedienung

ETS2LA-Updates können den kompletten Anwendungsordner einschließlich manuell installierter Plugins ersetzen. Bewahre die gebaute DLL außerhalb dieses Ordners auf und prüfe nach einem Update die Bibliotheksversionen, bevor du sie bei geschlossenem ETS2LA wieder installierst. Ab 0.2.9 liegen die Maut-Einstellungen dauerhaft unter `%LOCALAPPDATA%\ETS2LA-Mautassistent\settings.json`. Vorhandene gültige Einstellungen aus dem früheren Ordner `MautAssistent` werden einmalig übernommen; bereits gelöschte Dateien lassen sich dadurch nicht zurückholen.

1. Lkw sicher abstellen, Fahrassistenz ausschalten und ETS2LA schließen. Eine vorhandene Version von `MautAssistent.dll` sichern.
2. Die gebaute DLL in den Ordner `Plugins` der ETS2LA-Installation kopieren und ETS2LA neu starten. Keine andere Kopie desselben Plugins parallel installieren.
3. Mautassistent, Pathfinding, Lane Assist und Adaptive Cruise Control im Plugin-Manager einschalten. Im Stand bereitet sich der Mautassistent automatisch vor; während der Fahrt fordert F8 eine neue Prüfung an.
4. Unter **Mautassistent → Einstellungen** die erkannte Mautanlage prüfen. Eine tatsächlich Lkw-taugliche Spur und deren Zahlart bestätigen. Es sind keine fremden Spurfreigaben vorinstalliert.
5. Bei stehendem Lkw und ausgeschalteter Fahrassistenz auf **Bereit – F8 startet die Fahrassistenz** warten. Kein zusätzlicher Klick ist erforderlich. Zum Fahren in ETS2LA den Modus **Full Self-Driving** verwenden.
6. In **Settings → Controls** die physische Bremse unter **Bremse – manuelle Übernahme** zuordnen und die Achsenrichtung einstellen. Im Stand prüfen: Bremse drücken muss die Assistenz ausschalten, Gas darf dies nicht tun. Erst danach die Abschaltung verwenden. Die eingerichtete Starttaste kann bei gültiger Route auch während der Vorwärtsfahrt übernehmen; bis zum Abschluss der Prüfung selbst steuern.

Für **Route im Stand prüfen** muss die Fahrassistenz ausgeschaltet sein. Die Module Lane Assist und ACC bleiben eingeschaltet. Die Probe berechnet eine separate Route ohne Gas- oder Lenkausgabe.

### Tastatur und Stream Deck

Die Tasten werden in ETS2LA unter **Settings → Controls** zugeordnet; das Plugin setzt sie nicht selbst. Ein Stream-Deck-Hotkey kann anschließend dieselbe Taste senden.

| Taste | ETS2LA-Zuordnung | Verhalten |
| --- | --- | --- |
| F8 | Increase/RES | Assistenz einschalten / Tempo erhöhen |
| F9 | Cancel/MODE | Assistenz ausschalten / Fehlerbremsung zur manuellen Übernahme lösen; im ausgeschalteten Zustand außerdem Modus wechseln |
| F10 | Decrease/SET | Tempo verringern / setzen |
| F11 | Next/OK | Weiter / bestätigen |
| F12 | frei | Keine Belegung erforderlich |
| Eigene Bremsachse | Bremse – manuelle Übernahme | Lenkung und Tempomat ausschalten; gehaltene Bremse verhindert einen neuen Start |

Zuerst im Stand prüfen, ob F9 die Steuerung zuverlässig an den Controller zurückgibt. Ein Overlay ist nicht erforderlich.

„Nur beobachten / Fehlerbremsung lösen“ schaltet die Fahrassistenz aus, löst die Fehlerbremsung und pausiert die Vorbereitung einschließlich Start während der Fahrt. F9 gibt die Vorbereitung wieder frei; alternativ bleibt eine optionale manuelle Prüfung möglich. Für eine reine Routenprobe bleibt die separate Schaltfläche verfügbar.

## Tests

Die unabhängigen Logiktests benötigen nur das .NET-10-SDK:

```powershell
dotnet run --project CoreTests -c Release
```

Die vollständige Suite benötigt zusätzlich die oben genannten lokalen ETS2LA-Bibliotheken:

```powershell
dotnet run --project Tests -c Release
```

Geprüft werden unter anderem Schrankenwartezeit, Nachbarspur-Ausschluss, Speicherung der Zahlart, manuelle Übernahme, veraltete Daten, Routengeometrie und die ACC-Anbindung. Die Integrationstests verwenden künstlich angelegte Mautspuren mit den echten Bibliothekstypen. Die ACC-Prüfung startet keine Fahrzeugausgaben. Bremswegsimulationen ersetzen keine Fahrtests.

Gespeicherte Entscheidungen lassen sich ohne Spielausgabe mit `dotnet run --project CoreTests -c Release -- --replay 'C:\Pfad\zu\last-fault.json'` nachvollziehen. Das reproduziert die gespeicherten Entscheidungen, nicht die vollständige damalige Spielsimulation.

GitHub Actions führt die unabhängigen Logiktests und die Aufnahmeprüfungen aus. Fremde Bibliotheken werden weder im Repository noch in der CI verteilt.

## Dateien und Diagnose

Für einen zusammenhängenden Fahrnachweis stehen zwei rein lesende Werkzeuge für PowerShell 7 unter Windows bereit. Sie benötigen keine Plugin-Änderung und senden keine Fahrzeugausgabe. Bei geladenem Spiel und eingeschalteten Modulen:

```powershell
./Tools/Record-TollDrive.ps1 -EtsRoot 'D:\ETS2LA\current' -OutputPath './mautfahrt.jsonl' -DurationSeconds 600
./Tools/Measure-TollDrive.ps1 -Recording './mautfahrt.jsonl'
```

Die Aufnahme liest Tempo, Position, Motordrehzahl, Schäden, Luftdruck einschließlich fahrzeugspezifischer Warnschwellen und Warnflags sowie logische Pedalkanäle aus RenCloud-SDK-Revision 12 sowie die rohen Mautschranken aus dem ETS2LA-SDK. Standardmäßig werden zehn Beobachtungen pro Sekunde erfasst. Die Assistenten-Diagnose kommt weiterhin etwa einmal pro Sekunde; wiederholte Statusreferenzen werden nicht als neue Entscheidungen ausgegeben. Bestehende Aufzeichnungen werden nicht überschrieben.

Für die Diagnose der modernen Pedalausgabe kann `-IncludePedalOutput` ergänzt werden. Damit öffnet das Werkzeug zusätzlich die vorhandenen modernen Befehls- und Statusspeicher ausschließlich lesend und stellt angefordertes Gas/Bremse, Zeitstempel und gemeldete Übernahme den SDK-Pedalwerten gegenüber. Die Quellen sind nicht atomar. SDK `userThrottle` und `userBrake` können bereits simulierte Eingaben enthalten und beweisen keine physische Pedalbetätigung; dafür ist die getrennte Bremsachse des Assistenten maßgeblich. Der Schalter aktiviert keine Spielausgabe und stellt keine fehlenden Speicherbereiche her. Der aktuelle TruckersMP-Nachweis und seine Grenzen stehen in [BACKGROUND_INPUT.md](BACKGROUND_INPUT.md).

Doppelte Rohobjekte derselben Durchfahrt werden nur nach einer eindeutigen ersten 3D-Beobachtung zusammengefasst: gleiche Signalnummer, höchstens 1 m Abstand und übereinstimmender Zustand. Fehlt später eines der Objekte, bleibt der beobachtete Anker erhalten. Widersprüchliche Zustände unterbrechen die Offenzeit. Ohne ursprünglichen Anker wird keine Identität aus bloßer Nähe geraten; andere Höhenlagen übernehmen den Anker nicht. Diese Messzuordnung ersetzt keinen Nachweis aus dem Mautmodell.

Die Auswertung zählt zugeordnete Schranken, beobachtete Offenzeitfenster, Halte- und Durchfahrtspunkte sowie Datenlücken. Nach normaler Freigabe des Schrankenschlüssels kann sie getrennt eine SDK-Ausfahrtprojektion erfassen. Dafür müssen Anfahrt und Passage beobachtet sein; die Weiterfahrt muss zusammenhängende SDK-Positionen und aktuelle Statusmeldungen mit gesunden, nicht veralteten Lenk-/Routenwerten liefern. Rückwärtsfahrt, Gegenrichtung, Sprünge, Höhenwechsel und Datenlücken beenden diesen Nachweis. Die Projektion auf eine beim Passieren eingefrorene Tor-Ebene ist keine Entfernung entlang der Route und wird höchstens 60 Sekunden fortgeführt. Bei mehreren Schrankenschlüsseln muss `-GateKey` ausdrücklich gewählt werden. Ohne zugeordnete Schranke endet sie mit `Incomplete` und Rückgabecode 2. Sie erteilt keine automatische Gesamtfreigabe: Durchfahrtshöhe, Kollisionen, Zahlart und die tatsächliche Lenkung benötigen weiterhin Spielbild beziehungsweise Fahrerbestätigung. Die rohen Schrankendaten besitzen keinen eigenen Quellzeitstempel; diese Grenze bleibt im Bericht sichtbar. Pausen, Abtastlücken und fehlende Schranken unterbrechen die gemessenen Offenzeitfenster.

`./Tools/Test-TollRecording.ps1` prüft die Auswertung mit künstlichen Aufzeichnungen, einschließlich benachbarter Schranken mit gleicher numerischer ID, Zeitlücken, veralteter Diagnose und unterbrochener Öffnung. Die laufende ETS2LA-Installation bleibt bei diesen Tests unberührt. Aufzeichnungen können Spielpositionen enthalten und werden als `*.jsonl` von Git ausgeschlossen.

43 Prüfungen der Aufnahmeauswertung und Datenformate bestehen. Den vorhandenen Nachweisen und dem noch offenen vollständigen Fahrversuch ist [VALIDATION.md](VALIDATION.md) gewidmet.

- `Core/`: Bremsentscheidungen, Zustandsüberwachung, Spurgraph und Einstellungen.
- `Plugin/`: ETS2LA-Anbindung, Routenzuordnung, ACC-Begrenzung und Einstellungsseite.
- `CoreTests/` und `Tests/`: unabhängige Tests und Integrationstests.
- Laufzeitdaten: Unterordner `MautAssistent` der ETS2LA-Installation; unter anderem `settings.json`, `status.json` und `route-error.json`.

Diagnosedateien können Spielpositionen und weitere Sitzungsdaten enthalten. Bei Fehlerberichten nur die relevanten Angaben teilen und Zeitstempel prüfen: Nach dem Ausschalten kann eine Statusdatei veraltet sein. Zur Deinstallation ETS2LA schließen und die eigene Plugin-DLL entfernen; Hersteller-DLLs werden nicht gepatcht.
