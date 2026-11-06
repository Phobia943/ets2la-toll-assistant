# Fahrnachweis

Stand: 0.3.7 im Spiel geladen und die Abschaltung bei gezielt deaktiviertem Lenkmodul im Stand geprüft, 6. November 2026. Die vollständige Mautdurchfahrt ist **noch nicht validiert**. Ein späterer Kurvenunfall mit 0.3.5 begrenzt die Aussagekraft der zuvor erfolgreichen kurzen Straßenbeobachtung. Ein physikalischer Bremsnachweis bleibt offen. Bestandene Logiktests, Kartenproben und einzelne Fahrfenster sind getrennte Nachweise.

| Anforderung | Vorhandener Nachweis | Noch erforderlich |
| --- | --- | --- |
| Geeignete erreichbare Lkw-Spur mit automatischer Abrechnung | Fahrzeug- und Anschlussprüfung; Tests der Zahlart; reale Kartenprobe der gemeldeten Anlage wählt erreichbare AutoPASS-Schranke 7 | Durchfahrt mit passendem Lkw und Auflieger im Spiel; sichtbare Höhe und Beschilderung bestätigen |
| Stabile Zuordnung derselben Schranke | Integrationstests für doppelte Signalobjekte, verschwindende Replikate und widersprüchliche Zustände | Zusammenhängende Aufnahme vor, während und nach einer echten Durchfahrt |
| Langsame Anfahrt und Halt vor geschlossener Schranke | Zustands- und Bremswegsimulationen; nachgestellte SDK-/Routenaufrufe | Gemessene reale Verzögerung mit Beladung; Halt ohne Kollision und ohne unnötige Fehlerbremsung |
| Verzögerte Freigabe | Tests verlangen zwei Sekunden ununterbrochen offene Schranke; Schließen und Datenlücken setzen die Wartezeit zurück | Offenzeit und tatsächliche Anfahrt im selben Fahrversuch beobachten |
| Langsame Passage und saubere Ausfahrt | 10-km/h-Begrenzung bis 30 m hinter der Schranke; Tests für schließende Schranke hinter dem Lkw und Anschluss-Spur | Vollständige Ausfahrt, danach stabile Straßenlenkung |
| Sichere manuelle Übernahme | Mit 0.3.3 im Stand und während der Fahrt durch Fahrer bestätigt; 0.3.5 prüft zusätzlich unmittelbare Ausgabe-Freigabe bei beschäftigter Routenverwaltung. Nach dem gezielten Ausfalltest mit 0.3.7 bestätigt der Fahrer erneut die manuelle Übernahme und Controller-Lenkung | Erneute Übernahme im abschließenden Fahrversuch bestätigen |
| Tatsächliche Lenkbereitschaft | 0.3.7 live: F8 aktiv, frische Lenksignale; Lane Assist im Stand gezielt deaktiviert, nach 152 ms seit dem letzten Signal Fehlerbremsung verriegelt und Assistenz aus | Tatsächliche SDK-Bremsausgabe während der Verriegelung messen; Kurvenfahrt und Ursache spontaner Signallücken weiter prüfen |
| Start und normale Straßenfahrt | 0.3.5 live geladen, automatisch vorbereitet und mit F8 aktiviert. Erste Minute plus weitere drei Minuten beobachtet: bis etwa 80 km/h, kein aufgezeichneter Trackingfehler und keine Fehlerbremsung | Kein Nachweis beliebiger Strecken; weitere Fehlermeldungen anhand ihrer Aufzeichnungen prüfen |

## Zusammenhängender Mautversuch

Im Einzelspieler vor der Zufahrt speichern. Beladung und Einstellungen festhalten. Die Aufnahme vor Beginn der Anfahrt starten und bis nach der Ausfahrt sowie der manuellen Übernahme weiterlaufen lassen. Während der Fahrt keine Plugin-Dateien oder Einstellungen ändern.

Die Messung muss dieselbe Anlage und Durchfahrt verfolgen. Prüfen: Zahlart und Lkw-Eignung, frühe Geschwindigkeitsreduktion, Halt falls geschlossen, verzögerte Freigabe nach Öffnung, langsame Passage, anschließende Lenkung und manuelle Übernahme mit Bremse beziehungsweise F9. Bei Fehlverhalten selbst übernehmen; ein abgebrochener Versuch bleibt als solcher dokumentiert.

Rohaufnahme, Messbericht, Version, Einstellungen und Fahrerbeobachtung gehören zusammen. Eine Aufnahme ohne Mautschranke oder ohne vollständige Ausfahrt kann keinen Mautnachweis liefern. Das Auswertungswerkzeug meldet deshalb Beobachtungen und Lücken, keine automatische Gesamtfreigabe.

## Standtest und neuer Ausfall am 6. November

Der kontrollierte Test erfasste bei praktisch stehendem Lkw und angezogener Feststellbremse zunächst aktive Assistenz und frische Lenksignale. Nach dem gezielten Deaktivieren von Lane Assist protokollierte 0.3.7 um 18:38:13 Ortszeit einen Signalabstand von 152 ms, das ausgeschaltete native Modul, deaktivierte Fahrassistenz und eine verriegelte Fehlerbremsung. Der Fahrer bestätigte anschließend die manuelle Übernahme einschließlich Controller-Lenkung. Das Lenkmodul wurde danach wieder eingeschaltet; spätere Live-Daten zeigen gesunde Lenksignale und keine verriegelte Fehlerbremsung.

Die separate SDK-Aufnahme lief von 18:37:37 bis 18:38:07 und endete damit **vor** dem gezielten Ausfall. Ihre 246 Beobachtungen bestätigen Stillstand mit Feststellbremse, können aber weder die Auslösung noch die tatsächlichen Bremspedalwerte danach beweisen. Dafür ist eine weitere Messung im Stand nötig. Auch diese würde noch keinen Bremsweg unter Beladung nachweisen.

Ein unabhängiger Ausfall um 18:32:36 bei rund 28 km/h bleibt ungeklärt. Der Monitor meldete 1,99 Sekunden seit dem letzten erfassten Lenksignal; gleichzeitig fehlen für etwa zwei Sekunden Bewegungsaufzeichnungen, und der zuletzt erfasste ACC-Befehl war 2,03 Sekunden alt. Bei der unmittelbar folgenden Fehleraufzeichnung lag bereits ein neuer Lenkwert vor. Das native Lenkmodul lief und meldete keinen internen Fehler. Die Daten unterscheiden eine gemeinsame Ausführungspause, verzögerte Ereigniszustellung und ein überholtes Monitor-Snapshot nicht sicher. Sie rechtfertigen weder eine gelockerte Abschaltschwelle noch die Aussage, dass die ursprüngliche Ursache behoben sei.

## Grenzen

Die vorbereitete 0.3.8 behebt einen zusätzlich reproduzierten Ausführungsfehler: Ein angehaltener nativer Diagnoseaufruf verzögerte bisher sowohl den ersten Fehlerbremsbefehl als auch die manuelle Freigabe der Lenküberwachung. Der neue Test bestätigt Bremsausgabe und physische Übernahme bereits während des angehaltenen Aufrufs sowie das Verwerfen seines verspäteten Ergebnisses. Das ist ein lokaler Schnittstellennachweis; 0.3.8 ist noch nicht installiert oder im Spiel geprüft und erklärt die spontane Aufzeichnungslücke nicht abschließend.

Die Erweiterung misst keine tatsächliche Durchfahrtshöhe. Die Schrankenquelle besitzt keinen eigenen Zeitstempel. Statusbilder mit etwa 1 Hz ersetzen keine hochauflösende Messung jeder Steuerentscheidung. Eine ausgefallene Anwendung kann keine verlässliche Bremsausgabe garantieren. Rohaufnahmen enthalten Spielpositionen und bleiben außerhalb des öffentlichen Repositorys.
