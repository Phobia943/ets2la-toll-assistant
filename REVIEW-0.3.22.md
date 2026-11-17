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

Die installierte .21 bleibt unverändert; .22 ist noch nicht installiert oder live gefahren. Das Paket ersetzt nur das Plugin und prüft die sechs unveränderten Begleitdateien. Die ursprüngliche erste Bremsung vor dem offenen Tor bleibt ungeklärt. Eine erfolgreiche zweite Passage belegt keine zuverlässige Durchfahrt an allen Anlagen oder vollständige Gespannfreiheit.
