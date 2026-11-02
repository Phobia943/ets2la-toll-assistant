# ETS2LA Mautassistent

Experimentelle Erweiterung für **ETS2LA** in Euro Truck Simulator 2: langsam an Mautstellen heranfahren, eine bestätigte Lkw-Spur verwenden und nach dem Öffnen der Schranke verzögert weiterfahren.

Die Erweiterung ergänzt Pathfinding, Lane Assist und Adaptive Cruise Control. Sie ist kein eigenständiges Fahrsystem. Oberfläche und Meldungen sind auf Deutsch.

## Funktionen

- Anfahrt standardmäßig mit höchstens 20 km/h; Überwachung mindestens 200 m vor der zugeordneten Schranke.
- Bevorzugung einer ausdrücklich bestätigten, erreichbaren Lkw-Spur mit automatischer Abrechnung.
- Freigabe erst nach zwei Sekunden durchgehend vollständig geöffneter Schranke.
- Durchfahrt mit höchstens 10 km/h bis 30 m hinter der Schranke.
- Speicherung der Spur und Zahlart pro Mautanlage, Einfahrt und Ausfahrt.
- Start im Beobachtungsmodus; aktive Begrenzung muss im Stand vorbereitet werden.
- Rückgabe der Steuerung beim Ausschalten der Assistenz oder bei ungültigen Daten.

Die Erweiterung erkennt Höhenfreiheit und Zahlart **nicht automatisch**. Eine offene Schranke oder die Kartenkennzeichnung „AllVehicles“ reicht dafür nicht aus. Ohne bestätigte automatische Lkw-Spur endet die Assistenz mit einem Übernahmehinweis. Manuelle Zahlstellen müssen selbst angefahren und bedient werden. Es werden keine Zahlungstasten gedrückt oder Gebühren verändert.

## Entwicklungsstand

Quellstand **0.2.4 in Erprobung**. Anfahrt, Spurwahl, langsame Durchfahrt und Rückgabe an den Controller wurden mit einer früheren Version an einer ausgewählten Mautstelle praktisch geprüft. Die normale Straßenlenkung wurde mit 0.2.3 nach einem Neustart ebenfalls erfolgreich geprüft. Version 0.2.4 isoliert die Routenmessung vom veränderlichen Spurwechselzustand der Lenkroute; sie wurde bislang nur lokal automatisiert geprüft.

**Offen bleibt ein gemeldeter Lenkfehler direkt nach der Mautausfahrt.** Die Version enthält Korrekturen für veraltete Schrankenverfolgung und zusätzliche Routendiagnosen. Eine erneute vollständige Fahrt durch die betroffene Ausfahrt steht aus; der Fehler gilt noch nicht als abschließend behoben.

Der Halteabstand von 8 m bezieht sich auf den Telemetriepunkt des Lkw, nicht auf die Stoßstange. Er ist kein geprüfter Haltepunkt für manuelles Bezahlen. Gefälle, unterschiedliche Fahrzeuge und Beladungen sind nicht umfassend validiert. Bei Datenverlust bedeutet die Abschaltung manuelle Übernahme, keine garantierte Bremsung bis zum Stillstand.

## Voraussetzungen

Geprüfte lokale Kombination:

| Komponente | Version |
| --- | --- |
| ETS2LA | 2026.10.5231 |
| Adaptive Cruise Control | 2.1.0 |
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

1. Lkw sicher abstellen, Fahrassistenz ausschalten und ETS2LA schließen. Eine vorhandene Version von `MautAssistent.dll` sichern.
2. Die gebaute DLL in den Ordner `Plugins` der ETS2LA-Installation kopieren und ETS2LA neu starten. Keine andere Kopie desselben Plugins parallel installieren.
3. Mautassistent, Pathfinding, Lane Assist und Adaptive Cruise Control im Plugin-Manager einschalten. Der Mautassistent beobachtet zunächst nur.
4. Unter **Mautassistent → Einstellungen** die erkannte Mautanlage prüfen. Eine tatsächlich Lkw-taugliche Spur und deren Zahlart bestätigen. Es sind keine fremden Spurfreigaben vorinstalliert.
5. Bei stehendem Lkw und ausgeschalteter Fahrassistenz **Mautassistenz vorbereiten** wählen. Zum Fahren in ETS2LA den Modus **Full Self-Driving** verwenden.
6. Mit der eingerichteten Starttaste aktivieren. Bei falscher Route oder unpassender Bremsung ausschalten und selbst übernehmen.

Für **Route im Stand prüfen** müssen Lane Assist und ACC ausgeschaltet sein. Die Probe berechnet kurz eine Route ohne Gas- oder Lenkausgabe und schaltet die Fahrassistenz wieder aus.

### Tastatur und Stream Deck

Die Tasten werden in ETS2LA unter **Settings → Controls** zugeordnet; das Plugin setzt sie nicht selbst. Ein Stream-Deck-Hotkey kann anschließend dieselbe Taste senden.

| Taste | ETS2LA-Zuordnung | Verhalten |
| --- | --- | --- |
| F8 | Increase/RES | Assistenz einschalten / Tempo erhöhen |
| F9 | Cancel/MODE | Aktive Assistenz ausschalten; im ausgeschalteten Zustand Modus wechseln |
| F10 | Decrease/SET | Tempo verringern / setzen |
| F11 | Next/OK | Weiter / bestätigen |
| F12 | frei | Keine Belegung erforderlich |

Zuerst im Stand prüfen, ob F9 die Steuerung zuverlässig an den Controller zurückgibt. Ein Overlay ist nicht erforderlich.

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

GitHub Actions führt die unabhängigen Logiktests aus. Fremde Bibliotheken werden weder im Repository noch in der CI verteilt.

## Dateien und Diagnose

- `Core/`: Bremsentscheidungen, Zustandsüberwachung, Spurgraph und Einstellungen.
- `Plugin/`: ETS2LA-Anbindung, Routenzuordnung, ACC-Begrenzung und Einstellungsseite.
- `CoreTests/` und `Tests/`: unabhängige Tests und Integrationstests.
- Laufzeitdaten: Unterordner `MautAssistent` der ETS2LA-Installation; unter anderem `settings.json`, `status.json` und `route-error.json`.

Diagnosedateien können Spielpositionen und weitere Sitzungsdaten enthalten. Bei Fehlerberichten nur die relevanten Angaben teilen und Zeitstempel prüfen: Nach dem Ausschalten kann eine Statusdatei veraltet sein. Zur Deinstallation ETS2LA schließen und die eigene Plugin-DLL entfernen; Hersteller-DLLs werden nicht gepatcht.
