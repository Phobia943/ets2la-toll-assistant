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

Ab 0.2.15 liest die Erweiterung die Zahlart unterstützter Mautmodelle aus den Spiel-Hookups und ordnet sie einer eindeutigen Spurkurve zu. Für die Spurwahl prüft sie den vollständigen Weg auf Pkw- und Eskorte-Beschränkungen sowie die Anschlüsse vor und nach der Anlage. Bei ausreichendem Weg kann der native Planer den Wechsel frühzeitig auf der Zufahrt einleiten. Die Erkennung misst keine tatsächliche Durchfahrtshöhe. Mehrdeutige Modellmarkierungen oder unpassende Anschlüsse führen zum Halt vor der Aufteilung.

Wenn eine passende manuelle Lkw-Spur die einzige erkennbare Option ist, wählt der Assistent sie und hält vor der Schranke. Die Zahlung bleibt beim Spieler. Wenn der Zahlpunkt weiter vorne liegt, mit F9 übernehmen und manuell vorrollen.

## Entwicklungsstand

Quellstand **0.3.7 vorbereitet**, installierter Fahrstand 0.3.6. Die automatische Standprobe berechnet eine separate Route und lässt Spurführung und Tempomat eingeschaltet. Während der Fahrt fordert F8 eine neue Prüfung an. Erst nach Prüfung an der neuesten Lkw-Position wird dieselbe Route an den kompatiblen nativen Planer übergeben und die Assistenz aktiviert. Eine alte Standfreigabe reicht dafür nicht aus. Der Lkw muss zur Spur ausgerichtet sein, höchstens 1,25 m vom geplanten Verlauf liegen und genügend zusammenhängende Strecke für das aktuelle Tempo voraus haben. Ein offener Startversuch endet spätestens nach vier Sekunden. F9 verwirft auch ein noch laufendes Prüfergebnis. Ein abgelehnter Start während manueller Fahrt löst keine Fehlerbremsung aus; tatsächlicher Trackingverlust nach einer Übernahme bleibt überwacht.

Die Bremsübernahme liest eine separat zugeordnete physische Achse über die öffentliche ETS2LA-Eingabeschnittstelle. Sie wertet weder die simulierte Bremswirkung noch gemischte SDK-Pedalwerte aus. Bei geteilten Achsen muss die passende Achsenhälfte in Controls eingestellt werden; danach im Stand prüfen. Eine Bremsübernahme verwirft Startversuche, gibt Lenkung und Pedale frei und quittiert eine aktive Fehlerbremsung ausdrücklich zur manuellen Übernahme. Solange die Bremse gehalten wird, bleibt F8 gesperrt. Nach dem Loslassen ist ein neuer F8-Druck nötig.

Die Startsuche berücksichtigt auf geprüften Pathfinding-Builds auch die Grenzen zusammenhängender Kartenverbinder. Dort gelten weiterhin die Prüfung von Lkw-Freigabe, lokaler Richtung, geometrischer Verbindung und mindestens 25 Metern zusammenhängender Strecke. An einzelnen Kartenübergängen sind Richtung und Vorzeichen der Spurnummer unabhängig. Diese Kombination wird nur übernommen, wenn Verkehrsseite und Fahrbahngeometrie übereinstimmen und zwei native Startproben hinter dem Übergang dieselbe Spur bestätigen.

Die Mautregelung reduziert das Tempo über den verfügbaren Weg. Unter gleichwertigen Zahlarten bevorzugt sie eine erreichbare Spur mit weniger Änderungen an der Route. Wenn die gewählte Spur unerreichbar wird, versucht sie vor der Aufteilung zu halten, solange der geprüfte Bremsweg reicht. Bei einer früh sichtbaren Routenlücke bremst sie auf dem zusammenhängenden Teil der Route ab. Bei verlorener oder falsch gerichteter Spur greift weiterhin die Fehlerbremsung.

66 vollständige lokale Prüfungen und 42 unabhängige Logikprüfungen bestehen. Neue Starttests prüfen die Bewegung während der Berechnung, den ersten Steuerungszyklus, erneutes F8 nach F9 und sichere Ablehnungen. Bremsübernahme wird mit aktiver Assistenz, offener Prüfung und aktiver Fehlerbremsung geprüft.

Mit 0.3.3 praktisch geprüft: Die physische Bremse eines Xbox-Controllers schaltet Full Self-Driving aus; Gas löst die Abschaltung nicht aus. Loslassen startet nicht automatisch neu, die manuelle Lenkung funktioniert wieder. Nach manueller Fahrt wurde F8 bei rund 69 km/h angefordert, die Route geprüft und dieselbe gültige Route in den Fahrmodus übernommen. Die Aufzeichnung und der Fahrer bestätigen die Übernahme ohne Fehlerbremsung. Die konkrete Achsenhälfte war an diesem Gerät `Axis Z / SplitPos`; andere Geräte müssen separat geprüft werden. Eine vollständige Mautdurchfahrt mit dieser Version bleibt offen.

Anfahrt, Spurwahl, langsame Durchfahrt und Rückgabe an den Controller wurden mit einer früheren Version an einer ausgewählten Mautstelle praktisch geprüft. Die Fahrvalidierung von 0.3.2 steht aus. Die normale Straßenlenkung wurde mit 0.2.3 nach einem Neustart ebenfalls erfolgreich geprüft. Version 0.2.4 isolierte die Routenmessung vom veränderlichen Spurwechselzustand der Lenkroute.

Bei der weiteren Fahrt mit 0.2.4 trat ein nativer Routenfehler auf. Danach konnte F8 die Fahrassistenz ohne aktive Mautbegrenzung erneut starten; eine zu niedrige Spur wurde gemeldet. Version 0.2.5 verlangt bei eingeschaltetem Mautassistenten eine erfolgreiche Vorbereitung, bevor Fahrassistenz eingeschaltet bleiben darf. Nach einer Störung zuerst mit F9 manuell übernehmen und anhalten; ab 0.2.10 erfolgt die erneute Vorbereitung dann automatisch. Ein normaler Stopp mit F9 löscht die Vorbereitung nicht. Die Rückweisung wird in Daten-Callbacks und im Plugin-Tick geprüft; sie ist keine atomare Sperre sämtlicher Ausgabethreads des Hostprogramms. Bei deaktiviertem Plugin kann diese Prüfung nicht wirken. Die Korrektur ist automatisiert geprüft, eine erneute vollständige Mautfahrt steht aus.

**Version 0.2.6 korrigiert die Übergabe an die Anschluss-Spur hinter dem Mauttor.** Die bestätigte Mautspur wird mit einer eindeutig anschließenden Spur derselben Straßenseite verbunden; die Korrektur erreicht auch die intern weiterverwendete Hostroute. Zusätzlich wird die native Positionsprojektion auf gekrümmten Mautspuren genauer. Knoten, Richtung und Geometrie müssen übereinstimmen; eine fehlende Anschlussstraße wird nicht ersetzt. Bei unklarer oder verspäteter Übergabe ist manuelle Übernahme erforderlich.

45 vollständige lokale Prüfungen und 35 unabhängige Logikprüfungen bestehen. Die fehlende Spurerkennung an der konkret gemeldeten Ausfahrt ist **noch nicht im Fahrtest bestätigt behoben**. Die gespeicherten Daten enthalten deren Anschlussstraßen nicht. Ein künstlicher Kurvenfall reproduziert einen Projektionsfehler; an der gespeicherten Mautgeometrie war er zu klein, um den Vorfall allein zu erklären. Eine vollständige Mautfahrt bleibt deshalb erforderlich.

**Version 0.2.7 fordert bei einem erkannten Trackingfehler starkes Bremsen an und schaltet die Lenkassistenz aus.** Die Bremsanforderung läuft unabhängig vom ACC weiter. Sie bleibt bis zur manuellen Übernahme mit F9 aktiv; dabei selbst die Bremse halten. Eine wiedergefundene Route oder F8 lösen die Bremsung nicht. Nach der Übernahme wird ab 0.2.10 im Stand automatisch neu vorbereitet; ein weiterer F8-Druck bleibt zum Fahren erforderlich. Ohne eingeschaltetes, vorbereitetes Plugin wirkt diese Überwachung nicht.

Der Watchdog behandelt gemeldete Routenfehler direkt und reagiert auf mindestens 0,5 s alte Spieltelemetrie. Seit 0.2.13 entscheidet der gemeinsame Fahrzustand, ob die Assistenz noch startet, kurz auf Daten wartet oder sofort abbremsen muss. Bei Lenkassistenz werden ein Spurabstand über 3 m, mehr als 90° entgegen der lokalen Fahrtrichtung, Rückwärtsfahrt sowie eine gegenläufig neu berechnete Route abgefangen. Die Prüfung ist keine allgemeine Erkennung aller Verkehrszeichen oder Fahrbahnverbote.

Der Halteabstand von 8 m bezieht sich auf den Telemetriepunkt des Lkw, nicht auf die Stoßstange. Er ist kein geprüfter Haltepunkt für manuelles Bezahlen. Gefälle, unterschiedliche Fahrzeuge und Beladungen sind nicht umfassend validiert. Die Fehlerbremsung und der tatsächliche Bremsweg sind noch nicht praktisch geprüft; eine ausgefallene App oder Spielanbindung kann keine verlässliche Bremsausgabe garantieren.

Version 0.2.8 unterscheidet den aktuellen Trackingverlust von einer entfernten Planungslücke. Bei sauberer aktueller Spur wird nur der zusammenhängende Teil vor der ersten Lücke verwendet. Liegt deren Ende innerhalb der geschwindigkeitsabhängigen Planungsreserve, wird weiter gebremst. Die Reserve beträgt mindestens 25 m, sonst 15 m plus eine Sekunde Fahrweg plus den rechnerischen Bremsweg bei 2 m/s². Unverbundene Punkte werden nicht überbrückt; die Grenzwerte für aktuelle Spurabweichung und Gegenrichtung bleiben erhalten. Der konkrete Fehlalarm muss mit dieser Anpassung noch im Spiel nachgeprüft werden.

Mit 0.2.7 wurde im Stand bestätigt: Die Unterbrechung von Pathfinding aktiviert die Fehlerbremsung und schaltet die Assistenz aus. Das erneute Einschalten des Routenmoduls löst die Sperre nicht. F9 löst sie, und die Controllerlenkung reagiert wieder normal. Die tatsächliche Verzögerung während einer Fahrt wurde dabei nicht gemessen.

Version 0.2.11 unterscheidet kurze Verbindungen zwischen getrennten Routenabschnitten von tatsächlichen Fahrsegmenten. Nur die Fahrsegmente bestimmen die Richtung. Der synthetisch reproduzierte Gegenrichtungs-Fehlalarm an einem kurzen Übergang ist damit korrigiert; für den gemeldeten Vorfall fehlen die damaligen Richtungsvektoren. Sein konkreter Auslöser bleibt daher unbestätigt. Bei weiteren Richtungsfehlern werden die ausgewerteten Vektoren und benachbarte Routenpunkte lokal aufgezeichnet.

**Version 0.2.12 trennt einen geplanten Halt vor einer unbekannten Mautspur von einem Trackingfehler.** Bei vorhandenen SDK- und Routendaten wird die Mautanlage bis zu 1 km voraus berücksichtigt (die tatsächliche Reichweite hängt vom Spiel und Pathfinding ab). Die Haltegrenze liegt 15 m vor dem Ende der eindeutig zusammenhängenden, für Lkw freigegebenen Zufahrt, spätestens vor der ersten Verzweigung oder beschränkten Kurve. Die Grenze wird entlang der Route verfolgt, nicht als Luftlinie zur Schranke. Eine Warnung fordert zur Übernahme auf; Lenkung und Tracking bleiben während des geplanten Halts aktiv. Die ACC-Verzögerung ergibt sich aus Geschwindigkeit und Restweg, einschließlich 1,5 s Reaktionsreserve, und liegt zwischen 0,4 und 2,5 m/s². Im Stand wird gehalten, bis manuell mit F9 übernommen oder die Spur im ausgeschalteten Zustand bestätigt wird. Es gibt keine automatische Freigabe durch eine neue Route oder offene Schranke. Reicht der Anhalteweg nicht, verschwindet die Haltegrenze aus der gültigen Route oder fällt das Tracking aus, bleibt die starke Fehlerbremsung erhalten. Ohne aktiven Tempomaten ist der geplante Halt nicht verfügbar.

Der gemeldete harte Stopp bei ungefähr 90 km/h wurde durch fehlende Spurfreigabe ausgelöst, nicht durch einen Trackingfehler. Automatische Tests prüfen die neue Unterscheidung im tatsächlichen Plugin-Aufrufpfad einschließlich F9 und anschließendem Trackingverlust. Eine Bremswegsimulation prüft den Halt vor der Grenze. Das ersetzt keinen Fahrtest mit beladenem Lkw; eine sanfte Verzögerung an jeder Mautstelle ist damit noch nicht nachgewiesen.
**Version 0.2.13 bündelt die Entscheidungen über Start, Datenlücken und Trackingverlust.** Im Stand fordert der Tempomat während der ersten Routenberechnung Halt an. Bei gemeldetem Kartenladen wartet die Assistenz bis zu acht Sekunden, sonst bis zu zwei Sekunden auf die erste vollständige Fahrentscheidung. Meldet ETS2LA einen unbrauchbaren Startpunkt, wird F8 im Stand mit einer Meldung abgebrochen. Während der Fahrt lösen eine ungültige Route, fehlende Spieltelemetrie oder falsche Fahrtrichtung weiterhin sofort die Fehlerbremsung aus.

Bei einer kurzen Datenlücke abseits einer erkannten Mautzufahrt fordert der Tempomat eine leichte Verzögerung an. Voraussetzung sind frische Spieltelemetrie, eine weiterhin prüfbare Route und ausreichende Strecke voraus. Die Erholungsphase dauert höchstens 0,5 Sekunden und endet erst nach 0,15 Sekunden stabiler Daten. Eine mindestens eine Sekunde alte Route löst die Fehlerbremsung aus. Für Schranken und unbestätigte Mautspuren gelten weiterhin die eigenen Halteregeln.

`last-fault.json` enthält ab 0.2.13 bis zu 512 der letzten Entscheidungen samt Eingaben und Anfangszustand. Die Zustandslogik lässt sich ohne laufendes Spiel erneut ausführen:

```powershell
dotnet run --project CoreTests -c Release -- --replay 'C:\Pfad\zu\last-fault.json'
```

Die Wiedergabe vergleicht die Entscheidungen des Watchdogs. Sie simuliert weder Lenkung noch Fahrzeugphysik. Ältere Dateien enthalten noch keine Entscheidungsfolge.

**Start auf Kartenverbindern ab 0.2.14.** Wenn Pathfinding im Stand keinen regulären Startabschnitt findet, prüft die Erweiterung nahe Kartenverbinder. Sie verlangt eine Lkw-taugliche Spur ohne Schranke oder Signal, eine passende Fahrtrichtung und einen durchgehenden Anschluss an die nächste Straße. Bei überlappenden Spuren kann der letzte manuell gefahrene Spurverlauf die Zuordnung klären. Der Verlauf bleibt im Arbeitsspeicher. Mehrdeutige Ausfahrten und fehlende Anschlüsse führen weiterhin zum Startabbruch.

Die Übergabe erfolgt beim Fehlerereignis im Thread des nativen Routenplaners. Sie ist für die lokal geprüften Builds von Pathfinding 1.2.9, PathLib 1.2.8 und den ETS2LA-Ereignisbus freigeschaltet. Bei anderen Builds bleibt die reguläre Startpunktsuche aktiv. Erst ein gültiges Routenereignis kann den Start freigeben.

Version 0.3.4 korrigiert zwei reproduzierte Fehlalarme: SDK-Daten warten nicht mehr hinter der Mautberechnung, und Kartenhöhe zählt nicht als seitlicher Spurabstand. Die Auswahl des Straßenabschnitts bleibt dreidimensional. Die Grenzwerte für echte Telemetrieverluste, seitliche Abweichung und Gegenrichtung bleiben erhalten. Physische Bremsübernahme gibt die Ausgaben auch während einer laufenden Berechnung sofort frei.

Doppelte Signalobjekte derselben Mautdurchfahrt werden zusammengefasst; widersprüchliche Zustände bleiben unbekannt. Normale AutoPASS-Spuren und Sondertransport-Spuren werden anhand der Spieldefinitionen unterschieden. An der gespeicherten betroffenen Mautanlage wählt die Kartenprobe eine erreichbare automatische Lkw-Spur ohne Halt wegen unbekannter Zahlart. Eine vollständige Mautdurchfahrt und eine erneute Straßenfahrt mit 0.3.4 sind noch nicht praktisch bestätigt.

Ab 0.3.5 kann die Startsuche auf dem ersten Kartenverbinder einen vollständig passierten Signalabschnitt mit mindestens 2 m Abstand hinter dem Lkw berücksichtigen. Signale voraus bleiben gesperrt; Signalmetadaten und Fahrzeugbeschränkungen werden nicht entfernt. Die Kartenprobe am betroffenen Haltepunkt liefert damit eine gültige Route, ohne den Lkw zu versetzen.

Mit 0.3.5 im Spiel bestätigt: automatische Vorbereitung und Aktivierung von Full Self-Driving mit F8. Eine Minute normaler Straßenfahrt bis rund 77 km/h wurde mit 545 SDK-Proben und 59 frischen Statusbildern aufgezeichnet; keine Fehlerbremsung und kein gemeldeter Routenfehler in diesem Zeitfenster. Das bestätigt weder die gesamte Strecke noch eine vollständige Mautdurchfahrt.

Version 0.3.6 ergänzt eine Überwachung der tatsächlichen Lenkausgabe. Bei aktivem FSD lösen mehr als 150 ms ohne gültigen Lane-Assist-Befehl eine Abschaltung und gehaltene Bremsanforderung aus; ein eigener 50-ms-Timer wartet dafür nicht auf Routenarbeit. Beim Start werden höchstens 350 ms auf den ersten Befehl gewartet, während die ACC-Anbindung Halt anfordert. Geradeaus-Befehl 0, Spielpause und reiner ACC-Modus werden berücksichtigt. Bremse beziehungsweise F9 quittieren manuell; wiederkehrende Lenksignale starten nicht neu.

Ein späterer Kurvenunfall mit 0.3.5 ist noch nicht vollständig erklärt. Der Lkw lag tatsächlich rund 7 m neben der geplanten Spur, die Fehlerbremsung wurde angefordert. Die vorherige Lenkausgabe fehlt in der alten Aufzeichnung. Die neue Überwachung schließt den nachgestellten Fall einer gültigen Route bei ausgefallener Lenkung; sie beweist keine Behebung aller Ursachen für Abkommen in Kurven. Neue Fehleraufzeichnungen enthalten deshalb zusätzlich Position, Richtung, Tempo und die letzten Lenk- und ACC-Befehle. Reale Funktionsprüfung und erneute Kurvenfahrt mit 0.3.6 stehen noch aus.

0.3.7 ist vorbereitet und noch nicht installiert: Pause und Wiederaufnahme werden für die unabhängige Lenküberwachung direkt aus dem zuletzt gelieferten SDK-Bild gelesen. Ein nachgestellter Fehlalarm bei beschäftigter Routenverwaltung ist damit korrigiert. Die Fehleraufzeichnung ergänzt außerdem die native Fehlermeldung und letzte Regleranteile eines exakt geprüften Lane-Assist-Builds; sie ruft keine nativen Steuerzyklen auf. Die einzelnen nativen Felder werden ohne Sperre des Hostprogramms gelesen und ausdrücklich nicht als atomisches Steuerungsbild ausgegeben. Die Ursache des ursprünglichen Kurvenunfalls bleibt offen. Die neu gesicherten Fahrdaten belegen ausbleibende Lenksignale auch bei weiterhin gültiger Route und weniger als einem Meter seitlichem Abstand.

## Voraussetzungen

Geprüfte lokale Kombination:

| Komponente | Version |
| --- | --- |
| ETS2LA | 2026.11.5251 |
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

GitHub Actions führt die unabhängigen Logiktests aus. Fremde Bibliotheken werden weder im Repository noch in der CI verteilt.

## Dateien und Diagnose

Für einen zusammenhängenden Fahrnachweis stehen zwei rein lesende Werkzeuge für PowerShell 7 unter Windows bereit. Sie benötigen keine Plugin-Änderung und senden keine Fahrzeugausgabe. Bei geladenem Spiel und eingeschalteten Modulen:

```powershell
./Tools/Record-TollDrive.ps1 -EtsRoot 'D:\ETS2LA\current' -OutputPath './mautfahrt.jsonl' -DurationSeconds 600
./Tools/Measure-TollDrive.ps1 -Recording './mautfahrt.jsonl'
```

Die Aufnahme liest Tempo, Position und physische Pedale aus RenCloud-SDK-Revision 12 sowie die rohen Mautschranken aus dem ETS2LA-SDK. Standardmäßig werden zehn Beobachtungen pro Sekunde erfasst. Die Assistenten-Diagnose kommt weiterhin etwa einmal pro Sekunde; wiederholte Statusreferenzen werden nicht als neue Entscheidungen ausgegeben. Bestehende Aufzeichnungen werden nicht überschrieben.

Doppelte Rohobjekte derselben Durchfahrt werden nur nach einer eindeutigen ersten 3D-Beobachtung zusammengefasst: gleiche Signalnummer, höchstens 1 m Abstand und übereinstimmender Zustand. Fehlt später eines der Objekte, bleibt der beobachtete Anker erhalten. Widersprüchliche Zustände unterbrechen die Offenzeit. Ohne ursprünglichen Anker wird keine Identität aus bloßer Nähe geraten; andere Höhenlagen übernehmen den Anker nicht. Diese Messzuordnung ersetzt keinen Nachweis aus dem Mautmodell.

Die Auswertung zählt zugeordnete Schranken, beobachtete Offenzeitfenster, Halte- und Durchfahrtspunkte sowie Datenlücken. Bei mehreren Schrankenschlüsseln muss `-GateKey` ausdrücklich gewählt werden. Ohne zugeordnete Schranke endet sie mit `Incomplete` und Rückgabecode 2. Sie erteilt keine automatische Gesamtfreigabe: Durchfahrtshöhe, Kollisionen, Zahlart und die tatsächliche Lenkung benötigen weiterhin Spielbild beziehungsweise Fahrerbestätigung. Die rohen Schrankendaten besitzen keinen eigenen Quellzeitstempel; diese Grenze bleibt im Bericht sichtbar. Pausen, Abtastlücken und fehlende Schranken unterbrechen die gemessenen Offenzeitfenster.

`./Tools/Test-TollRecording.ps1` prüft die Auswertung mit künstlichen Aufzeichnungen, einschließlich benachbarter Schranken mit gleicher numerischer ID, Zeitlücken, veralteter Diagnose und unterbrochener Öffnung. Die laufende ETS2LA-Installation bleibt bei diesen Tests unberührt. Aufzeichnungen können Spielpositionen enthalten und werden als `*.jsonl` von Git ausgeschlossen.

18 Prüfungen der Aufnahmeauswertung bestehen. Den vorhandenen Nachweisen und dem noch offenen vollständigen Fahrversuch ist [VALIDATION.md](VALIDATION.md) gewidmet.

- `Core/`: Bremsentscheidungen, Zustandsüberwachung, Spurgraph und Einstellungen.
- `Plugin/`: ETS2LA-Anbindung, Routenzuordnung, ACC-Begrenzung und Einstellungsseite.
- `CoreTests/` und `Tests/`: unabhängige Tests und Integrationstests.
- Laufzeitdaten: Unterordner `MautAssistent` der ETS2LA-Installation; unter anderem `settings.json`, `status.json` und `route-error.json`.

Diagnosedateien können Spielpositionen und weitere Sitzungsdaten enthalten. Bei Fehlerberichten nur die relevanten Angaben teilen und Zeitstempel prüfen: Nach dem Ausschalten kann eine Statusdatei veraltet sein. Zur Deinstallation ETS2LA schließen und die eigene Plugin-DLL entfernen; Hersteller-DLLs werden nicht gepatcht.
