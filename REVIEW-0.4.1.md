# 0.4.1 – Eingangsbestätigung und wirksame Gasstellung trennen

Stand: 17. November 2026. Offline geprüfter Kandidat, noch nicht installiert. Ausgangsfassung ist 0.4.0; die drei Hostbibliotheken und deren Manifest bleiben unverändert.

## Beobachteter Fehler

Nach der ersten Fahrt mit 0.4.0 meldete die Überwachung fehlende Pedalwirkung und löste einen verriegelten Fehlerhalt aus. In der gespeicherten letzten Sequenz folgte die effektive Gasstellung zunächst dem eigenen Befehl. Dann fiel sie innerhalb weniger Zehntelsekunden gegen null, während der Lkw noch positiv beschleunigte. Die Route blieb gültig; es war keine zugeordnete Mautschranke der Haltegrund.

Die alte Regelung behandelte `truck.effective.throttle` als Empfangsbestätigung. Laut den mit der Spielanbindung gelieferten SCS-Headern sind empfangener Eingang (`truck.input.throttle`) und in der Simulation verwendetes Gas getrennte Kanäle. Die Aufnahme enthält noch keine separate Eingangsrückmeldung, Kupplung, Gänge oder Drehzahl. Eine bestimmte Schaltung oder ein Fokuswechsel als historische Ursache ist damit nicht bewiesen.

## Korrektur

- Die eigene Regelung prüft den tatsächlich gemeldeten Gaseingang. Ein vorübergehend reduziertes effektives Gas allein löst keinen Transportfehler aus. Solange die Wirkung abweicht, bleiben Integrator und aufwärts gerichtete Gasnachregelung begrenzt; Gasreduzierung bleibt möglich.
- Bleibt der empfangene Gaseingang aus, wird ausschließlich die Beschleunigung verriegelt und eine Übernahmeaufforderung angezeigt. Daraus allein entstehen weder Vollbremsung noch Abschaltung der weiterhin gültigen Lenkführung. Eine wiederkehrende Eingangsmeldung aktiviert das Gas nicht selbstständig; manuelle Übernahme und ein neuer Start sind erforderlich.
- Echte negative Geschwindigkeitsgrenzen, Halteanforderungen, explizite Notbremsung sowie die unabhängigen Fristen für Quellen und Lenk-/Pedalbefehle bleiben wirksam. Die bisherige Überwachung fehlender effektiver Bremsausgabe wird nicht durch eine allgemeine längere Schonfrist ersetzt.
- Aufnahme und Diagnose bewahren künftig Gaseingang, Bremseingang, effektive Kupplung, Motor- und Anzeigegang sowie Drehzahl getrennt. Rohe Eingänge können auch SDK-Befehle enthalten und beweisen keine körperliche Pedalbetätigung; dafür bleibt die separate Controller-Bremsübernahme zuständig.

## Prüfung und Grenzen

Die Gegenfälle unterscheiden empfangene Eingaben mit unterdrückter Wirkung, tatsächlich fehlenden Eingang, Rückkehr der Gaswirkung ohne Sprung, spätere Bremsgrenzen und manuelle Übernahme. Die ursprüngliche Aufnahme ist unvollständig abgetastet; nur gespeicherte, eindeutige Regleraufrufe dürfen wiedergegeben werden, keine erfundenen Zwischenframes.

Der endgültige Plugin-Kandidat `4ACD652AAAC20D353104F970F93249FF3C5ADDC4686650F1743D0FC3A5D83CB6` besteht 202 Integrations-, 94 Kernlogik-, 232 isolierte Host-/Plugin- und 68 Aufnahmeprüfungen. Die unabhängigen Gegenprüfungen sind darin teilweise enthalten; die Zahlen werden nicht addiert. Der exakte alte Produktbinärstand reproduziert den Fehler in der gespeicherten, ausgedünnten Folge bei 1,703 s; der endgültige neue Binärstand bleibt bei allen 27 gespeicherten Eingaben ohne Fehler- oder Bremsausgabe und erhöht das Gas während der gemeldeten Wirkungsabweichung nicht. Der ursprüngliche aufgezeichnete Fehler liegt bei 1,765 s: fehlende Zwischenaufrufe verhindern eine Behauptung identischer zeitlicher Wiedergabe. Der tatsächliche Plugin-Tick bis zu beiden isolierten Ausgabesenken prüft außerdem Empfang ohne effektive Wirkung, echten Empfangsausfall mit erhaltener Lenkung, Pause/Fortsetzen und anschließend eine tatsächlich kartierte geschlossene Schranke mit weiterhin wirksamer Bremse. Eine reale Stand-/Fahrprüfung und die TruckersMP-Fokusproblematik bleiben offen. Diese Korrektur ist kein Nachweis einer allgemeinen Maut-, Länder- oder Hindernisabdeckung.
