# Fahrnachweis

Stand: 0.3.5, 5. November 2026. Die vollständige Mautdurchfahrt ist **noch nicht validiert**. Bestandene Logiktests, Kartenproben und normale Straßenfahrt sind getrennte Nachweise.

| Anforderung | Vorhandener Nachweis | Noch erforderlich |
| --- | --- | --- |
| Geeignete erreichbare Lkw-Spur mit automatischer Abrechnung | Fahrzeug- und Anschlussprüfung; Tests der Zahlart; reale Kartenprobe der gemeldeten Anlage wählt erreichbare AutoPASS-Schranke 7 | Durchfahrt mit passendem Lkw und Auflieger im Spiel; sichtbare Höhe und Beschilderung bestätigen |
| Stabile Zuordnung derselben Schranke | Integrationstests für doppelte Signalobjekte, verschwindende Replikate und widersprüchliche Zustände | Zusammenhängende Aufnahme vor, während und nach einer echten Durchfahrt |
| Langsame Anfahrt und Halt vor geschlossener Schranke | Zustands- und Bremswegsimulationen; nachgestellte SDK-/Routenaufrufe | Gemessene reale Verzögerung mit Beladung; Halt ohne Kollision und ohne unnötige Fehlerbremsung |
| Verzögerte Freigabe | Tests verlangen zwei Sekunden ununterbrochen offene Schranke; Schließen und Datenlücken setzen die Wartezeit zurück | Offenzeit und tatsächliche Anfahrt im selben Fahrversuch beobachten |
| Langsame Passage und saubere Ausfahrt | 10-km/h-Begrenzung bis 30 m hinter der Schranke; Tests für schließende Schranke hinter dem Lkw und Anschluss-Spur | Vollständige Ausfahrt, danach stabile Straßenlenkung |
| Sichere manuelle Übernahme | Mit 0.3.3 im Stand und während der Fahrt durch Fahrer bestätigt; 0.3.5 prüft zusätzlich unmittelbare Ausgabe-Freigabe bei beschäftigter Routenverwaltung | Erneute Übernahme im abschließenden Fahrversuch bestätigen |
| Start und normale Straßenfahrt | 0.3.5 live geladen, automatisch vorbereitet und mit F8 aktiviert. Erste Minute plus weitere drei Minuten beobachtet: bis etwa 80 km/h, kein aufgezeichneter Trackingfehler und keine Fehlerbremsung | Kein Nachweis beliebiger Strecken; weitere Fehlermeldungen anhand ihrer Aufzeichnungen prüfen |

## Zusammenhängender Mautversuch

Im Einzelspieler vor der Zufahrt speichern. Beladung und Einstellungen festhalten. Die Aufnahme vor Beginn der Anfahrt starten und bis nach der Ausfahrt sowie der manuellen Übernahme weiterlaufen lassen. Während der Fahrt keine Plugin-Dateien oder Einstellungen ändern.

Die Messung muss dieselbe Anlage und Durchfahrt verfolgen. Prüfen: Zahlart und Lkw-Eignung, frühe Geschwindigkeitsreduktion, Halt falls geschlossen, verzögerte Freigabe nach Öffnung, langsame Passage, anschließende Lenkung und manuelle Übernahme mit Bremse beziehungsweise F9. Bei Fehlverhalten selbst übernehmen; ein abgebrochener Versuch bleibt als solcher dokumentiert.

Rohaufnahme, Messbericht, Version, Einstellungen und Fahrerbeobachtung gehören zusammen. Eine Aufnahme ohne Mautschranke oder ohne vollständige Ausfahrt kann keinen Mautnachweis liefern. Das Auswertungswerkzeug meldet deshalb Beobachtungen und Lücken, keine automatische Gesamtfreigabe.

## Grenzen

Die Erweiterung misst keine tatsächliche Durchfahrtshöhe. Die Schrankenquelle besitzt keinen eigenen Zeitstempel. Statusbilder mit etwa 1 Hz ersetzen keine hochauflösende Messung jeder Steuerentscheidung. Eine ausgefallene Anwendung kann keine verlässliche Bremsausgabe garantieren. Rohaufnahmen enthalten Spielpositionen und bleiben außerhalb des öffentlichen Repositorys.
