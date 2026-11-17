# Review 0.3.22 – Standmessung, Befehlsalter und Bremsdiagnose

## Beobachtung mit 0.3.21

Die zehnminütige Aufnahme am 17. November enthält drei getrennte Vorgänge:

1. An der ersten automatisch abrechnenden Mautspur blieb der Lkw ungefähr 13 m vor dem bereits offenen Tor stehen. Die Assistenz blieb bis zur bestätigten manuellen Übernahme aktiv. Es wurden keine Luftdruckwarnung, SDK-Pause oder neuen Kabinen-/Chassisschäden aufgezeichnet; eine Spielstörung durch bereits vorhandenen Verschleiß ist damit nicht ausgeschlossen. Laut Nutzer war das Spiel im Vordergrund. Dieser Halt ist kein autonomer Durchfahrterfolg.
2. Auf der Straße löste um 11:28:04 MEZ die Überwachung fehlender Lenksignale aus. Der gespeicherte Befehlswert war rund 156 ms alt; fast gleichzeitig liegen wieder neue Lenksignale vor. Telemetrie und Route hatten ebenfalls eine kurze Verarbeitungslücke. Der anschließend diagnostizierte Fehler „Assists are disabled.“ entstand nach der eigenen Abschaltung und beweist keinen vorherigen Planerfehler.
3. Die nächste automatische Mautspur wurde mit durchgehend aktiver Assistenz passiert; niedrigste aufgezeichnete Geschwindigkeit etwa 7,31 km/h, keine Fehlerbremsung. Der Nutzer bestätigte die Durchfahrt und die anschließende Spurführung ohne Eingriff. Der Recorder deckt nur den ersten Ausfahrtabschnitt ab; die weitere Spurführung ist eine Nutzerbestätigung.

## Nachgewiesene Korrekturen

### Sehr kleine negative Standgeschwindigkeit

Die Standard-Szenenprüfung behandelte bisher jeden negativen Geschwindigkeitswert als fehlenden gerichteten Mautabschnitt. Genau die aufgezeichneten Werte von etwa −0,0000624 und −0,000924 km/h reproduzieren den Fehler bei ansonsten frischer, freier Szene. Dadurch konnte während eines Halts ein neuer Haltegrund erzeugt werden.

Nur negative Werte innerhalb des benannten Standrauschens von 0,01 m/s werden nun auf null gesetzt. Wirkliche Rückwärtsfahrt, nicht endliche Werte, unbekannte Abschnitte, fehlende Quellen und Hindernisse bleiben gesperrt. Diese Änderung beseitigt den falschen Stand-Haltegrund; sie erklärt **nicht** das anfängliche Abbremsen von etwa 9 km/h bis zum Stillstand. Dafür fehlen in der bisherigen Aufnahme die vollständigen Begrenzungen des nativen Tempomaten.

### Alter und Sitzung der Lenksignale

Die bisherige Rechnung zog ein später gemessenes natives Befehlsalter von einer früher gelesenen lokalen Uhr ab. Eine Unterbrechung zwischen diesen Ablesungen konnte das Ergebnis verfälschen. Überwachung und Reihenfolge verwenden jetzt den unveränderten ursprünglichen `Stopwatch`-Zeitstempel. Die Differenz wird vor der Fließkommaumwandlung gebildet.

Die Ausgabezulassung bleibt bei 200 ms, die Grenze für fehlende Lenksignale bei 150 ms und die anfängliche Wartefrist bei 350 ms. Vor dem Verriegeln der Fehlerbremsung wird innerhalb derselben kurzen Sperre wie beim Speichern eines Lenkbefehls erneut geprüft. Ein vorher eingegangener frischer Befehl kann so die ältere Beobachtung entkräften. Alte Sitzungsepochen werden beim Speichern und bei der Überwachung erneut ausgeschlossen; manuelle Übernahme entfernt gespeicherte Lenk-/Pedalbefehle.

Der Straßenfall enthält keine rohen ursprünglichen Zeitstempel und keinen eindeutigen Zeitpunkt des damaligen Speicherns vor/nach der Abschaltung. Deshalb wird nicht behauptet, dass er mit .22 sicher ausgeblieben wäre. Die Korrektur beseitigt die unabhängig reproduzierten Zeitrechnungs- und Sitzungsfehler, ohne tatsächliche lange Lenkausfälle freizugeben.

### Diagnose ohne zusätzliche Arbeit im Überwachungspfad

Status und Bewegungsverlauf unterscheiden künftig die Maut-Beschleunigungsgrenze, normale Halteausgabe, Szenenhalteausgabe und Haltebesitzer. Der Tempomat wird ausschließlich über vorhandene primitive Felder und eine begrenzte Kopie seiner Begrenzungswerte beobachtet. PID, native Begrenzungsfunktionen und native Fahrzyklen werden nie zu Diagnosezwecken ausgeführt.

Diese Fremdbeobachtung läuft auf dem begrenzten Diagnosearbeiter, einschließlich Versionsprüfung und einmaliger Dateiprüfung. Die zusätzliche Tempomatdiagnose führt auf dem Überwachungspfad keine Fremdbeobachtung oder Dateiprüfung aus; die Zustands-/Epochenprüfung bleibt Teil der eigentlichen Überwachung. Nur eine unveränderliche Beobachtung mit eigenen Anfangs-/Endzeitstempeln wird übernommen; überholte Aufträge und gewechselte Instanzen werden verworfen. Die Beobachtung ist ausdrücklich nicht atomar und gehört nicht zwingend zum selben Fahrzyklus wie die übrigen Statuswerte.

Die Fehleraufzeichnung trennt erste Prüfung, letzte Prüfung und Bremsverriegelung sowie ursprünglichen Ausgabezeitstempel und Empfangszeit. Rohwerte der SDK-Eingabe gelten weiterhin nicht als Nachweis einer physischen Controllerbetätigung.

## Bereitstellung und Grenzen

177 Integrations-, 87 Kernlogik-, 146 isolierte Host-/Plugin- und 63 Aufnahmeprüfungen bestehen. Die Hostprüfungen enthalten 26 gezielte Prüfungen der ursprünglichen Befehlszeit, Sitzung und abschließenden Überwachung. Die kritische Gegenprüfung fand zusätzlich zwei Reihenfolgefehler: Das Warten auf die Host-Epochenfreigabe durfte einen Befehl nicht verjüngen; eine während dieses Wartens neu gemeldete SDK-Pause muss vor der Bremsverriegelung berücksichtigt werden. Beide Gegenfälle scheitern nachweislich mit den jeweiligen vorherigen Zwischenständen und bestehen mit dem finalen Build. Eine tatsächlich veraltete Lenkausgabe bleibt gesperrt.

Finaler Plugin-SHA256: `A8F789D79C9CCE3B49F6B273749C104617AA77140E7641E0148A9D570CD8BC96`; MVID: `1a5587a1-f9c2-4b2b-ab48-277676359ba3`. Der Build hat keine Fehler und eine bestehende Veraltungswarnung zur ETS2LA-Abhängigkeitsangabe.

Nach bestätigtem Stillstand, ausgeschalteter Assistenz und geschlossenem ETS2LA wurde .22 am 17. November um 12:12 Uhr MEZ installiert. Das Paket ersetzte nur das Plugin; die Vorversion .21 wurde gesichert. Plugin, Sicherung und sechs unveränderte Begleitdateien wurden danach unabhängig hashgeprüft. Paketmanifest-SHA256: `5924CEA690C96A1D9E46F5AD1BC19A756C71C130B04DDF51797A603CA20F0B05`. Alle drei [GitHub-Prüfungen des Code-Stands d75bb11](https://github.com/Phobia943/ets2la-toll-assistant/actions/runs/44071107830) bestehen.

Unmittelbar nach der Installation wurden Start- und Fahrprüfung auf Nutzerwunsch verschoben. Es wurde weder eine Anwendung noch die Fahrassistenz automatisch gestartet; die geladene Laufzeitversion und Fahrprüfung waren zu diesem Zeitpunkt offen. Der spätere Laufzeitstand folgt unten. Die ursprüngliche erste Bremsung vor dem offenen Tor bleibt ungeklärt. Eine erfolgreiche zweite Passage mit .21 belegt keine zuverlässige Durchfahrt an allen Anlagen oder vollständige Gespannfreiheit.

## Spätere Fahrt am 17. November: getrennte Mautstopps

Nach dem späteren Nutzerstart bestätigt der aktuelle Status die geladene .22 und aktive Full-Self-Driving-Fahrt. Der Nutzer berichtet von einer erfolgreichen und einer fehlgeschlagenen spanischen Mautpassage. Die Aufzeichnungen enthalten mehrere Anlagen; diese Nutzernummerierung wird nicht ungeprüft einzelnen Protokollsequenzen zugeordnet.

- **14:23 Uhr MEZ, Zufahrtsfreigabe:** geplanter Stillstand etwa 159 m vor dem Tor, ohne neue verriegelte Fehlerbremsung. Automatikspur 10 besteht die statische Wegprüfung, ihr Einstieg braucht jedoch den Wechsel von Zufahrtsspur −2 auf −1. Die echte, frisch geladene Karte mit dem gespeicherten Routen-/Spurzustand reproduziert nur 15,31 m Straßenrest und damit eine Ablehnung unter der vorhandenen 45-m-Grenze. Der Anschluss ist eindeutig; eine allgemeine Geometrie- oder Richtungsstörung ist nicht belegt. Vier ausdrücklich kontrafaktische frühere Positionen auf derselben Straße bei 160/220 m Rest und 20/77 km/h bestehen die tatsächlichen `Discover → Apply`- und Ausfahrtprüfungen. Sie beweisen nicht, weshalb die tatsächliche frühe Auswahl ausblieb. Ein früherer geplanter Haltepunkt mit erhaltener Spurwechselreserve ist ein begründetes Korrekturziel, noch keine implementierte Behebung.
- **14:29 und 14:31 Uhr MEZ, Halt nach Durchfahrtfreigabe:** Automatikspur 10 ist jeweils gewählt. Das Protokoll wechselt von Öffnungswartezeit auf langsame Durchfahrt, anschließend steht der Lkw und der Nutzer übernimmt. Für den zuletzt beschriebenen Stopp bestätigt der Nutzer eine offene Schranke und die richtige Spur. Dies ist ein anderer Fehler als die Zufahrtsablehnung. Die Phasenwechsel enthalten nicht die vollständigen gleichzeitigen Bremsquellen; eine Ursache durch nativen Tempomaten, Szenenhalt oder Reglernachwirkung ist damit nicht bewiesen.
- Der gespeicherte Lenksignalfehler von 14:15 Uhr ist ein separates Ereignis. Eine alte Kartenkopie von 11:22 Uhr und ein Routenfehler vom 15. November wurden nicht zur Erklärung der aktuellen Stopps verwendet.

Die Untersuchung verändert weder die laufende Assistenz noch den Fensterfokus. Produktstand bleibt .22; keine Schutzgrenze wurde gelockert. Rohaufnahmen und Kartendaten bleiben lokal.

## Folgeaufnahme 14:35–14:45 Uhr MEZ

Die zehnminütige Aufnahme enthält 4.494 lesbare Messzeilen. Bei der Passage gegen 14:40 Uhr gab der Nutzer ausdrücklich selbst Gas und meldete danach verlorene Spurverfolgung. Sie ist **kein autonomer Durchfahrterfolg**. Während des Gaseingriffs sind gleichzeitige Spielgas- und Bremswerte gespeichert; das Durchfahren ohne Stillstand beweist deshalb keine korrekte automatische Regelung.

In einem späteren sieben Sekunden langen Fenster nach der Schranke sinkt das Tempo von etwa 12 auf 4,4 km/h. Die wiederholten nativen Beobachtungen melden ein positives Beschleunigungsziel und positive Pedalausgabe; 51 von 53 Spielproben enthalten dennoch weniger als 0,1 % Gas und Bremse. Fensterfokus, Schaltvorgang oder tatsächlicher Stellweg sind damit nicht geklärt. Der Status meldet gleichzeitig eine 4,50-m-Routenlücke **357 m voraus**, bei frischen Lenksignalen. Das bestätigt eine vorausliegende Anschlussstörung, aber keinen an dieser Stelle nachgewiesenen Ausfall der Lenkausgabe. Der vom Nutzer berichtete Trackingverlust bleibt separat zu untersuchen. Der Auslöser „Keine stabile Fahrentscheidung verfügbar“ um 14:39 Uhr wurde als weiteres getrenntes Ereignis gesichert.

Eine zusätzliche Offline-Geometrieprobe auf dem echten Auto10-Pfad des betroffenen Mautmodells zeigt einen konkreten Gegenfall für die native Schrankenwahl: Das gegenläufige Tor 15 projiziert etwa 7,95 m vor das eigene Tor 10 und liegt nur 3,80 m von der Route entfernt. Die dokumentierte native 4-m-/Nähe-Regel kann dieses fremde Tor vorziehen. Dies ist bisher eine Geometrieprobe mit konstruiertem Lkw-Punkt, kein ausgeführter nativer Getter und kein Beweis für die historischen Stopps. Native Diagnosewerte sind nicht atomar; fehlende Einträge in einer gerade neu aufgebauten Begrenzungsliste gelten nicht als freie Fahrt.
