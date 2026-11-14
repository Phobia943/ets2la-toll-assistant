# 0.3.14: frühere Modellprüfung und weniger Diagnosearbeit im Fahrzyklus

Stand: 14. November 2026. Offline gebaut und geprüft, noch nicht gefahren. Eine vollständige Behebung aller historischen Probleme wird nicht behauptet.

## Änderungen

**Mautmodelle früher laden.** Die Route fordert jetzt bereits vor dem Auftauchen passender SDK-Schranken die Prüfung der nächsten höchstens vier Prefabs mit Signalkurven an. Die bestehende Modellwarteschlange bleibt auf einen aktiven und vier wartende Aufträge begrenzt. Das soll Wartezeit auf Modell-/Zahlungsdaten während der eigentlichen Anfahrt vermeiden. Die Vorabprüfung wählt keine Spur, verändert keine Lenkroute und erfindet keinen offenen Schrankenzustand. Ein Fehler dieser optionalen Vorbereitung darf die Straßenfahrt nicht allein abschalten; die späteren verbindlichen Geometrie- und Schrankenprüfungen bleiben bestehen.

**Gleiche Spurprüfung bei Vorschau und aktiver Fahrt.** Die Startvorschau baute bisher einen Router ohne den Geometrieprüfer. Sie verwendet jetzt dieselbe Router-Fabrik mit denselben Zahlungs-, Freiraum- und Vorbereitungsschnittstellen. Ein synthetischer Test ruft die tatsächliche Vorschau auf und prüft, dass eine gesperrte automatische Spur nicht ausgewählt wird. Ein sicherer geplanter Halt auf der Zufahrt kann weiterhin eine zulässige Startoption sein.

**Statusgeometrie außerhalb des Fahrzyklus.** Die regelmäßige Statusausgabe führte bisher native Pfadprojektionen unter der gemeinsamen Fahrsteuerungssperre aus. Der Status baut seine zusätzliche Geometrie nun auf dem Diagnosearbeiter ausschließlich aus dem unveränderlichen, bereits geprüften Punktverlauf und den plugin-eigenen SDK-Schrankenproben. Ein absichtlich angehaltener Geometriearbeiter blockiert weder ACC noch Routenzustellung noch F9. Das entfernt diese konkrete Kopplung; es beweist nicht, dass alle spontanen nativen Lenkpausen behoben sind. Die seltene Fehleraufzeichnung behält ihre bisherige native Geometrie für detaillierte Ursachenanalyse.

Der Status kennzeichnet die Herkunft mit `geometry.source` (`VerifiedSampledTrace` oder `SdkOnly`) und `nativeProjectionMeasured = false`. `truckFactor` bleibt dort null, statt eine native Messung vorzutäuschen. Gate-Projektionen jenseits einer nicht aufgelösten Verbindung ergeben keine Routenentfernung. `lastFault.geometry` bleibt eine getrennte, native Diagnose. Ältere Auswertungen dürfen diese Quellen nicht gleichsetzen.

**Konservativer modellbasierter Zahlhaltepunkt.** Bei einer eindeutig zugeordneten manuellen Spur wird zusätzlich ein Halteintervall aus dem rechteckigen Modelltrigger ausgewertet. Die erste Fassung unterstützt nur eine gerade, vorwärts befahrene Gate-Kurve, einen vollständig darin enthaltenen und passend ausgerichteten Trigger ohne ungeprüften Versatz oder Skalierung sowie eindeutig zugehörige Signalpositionen. Eine fehlende, widersprüchliche, gekrümmte oder zu enge Zuordnung ergibt keinen neuen Haltepunkt.

Der neue Punkt kann nur mindestens so weit vor der Schranke liegen wie der bisher eingestellte Sicherheitsabstand. Er kann den Lkw also nicht automatisch dichter an die geschlossene Schranke schicken. Beispielsweise liefert ein geprüftes Modellintervall von 11–19 m bei bisher 8 m einen Sollhalt bei 15 m. Das ist ein synthetischer Testfall, keine gemessene Zahlzone eines konkreten Lkw. Nach Verlust optionaler Modelldaten bleibt ein bereits etablierter früherer Halt erhalten; der Lkw beginnt dadurch nicht von allein vorzurollen. Zahlung bleibt manuell. Wenn die tatsächliche Zahlzone näher an der Schranke liegt als der sichere Haltepunkt, ist weiterhin manuelle Übernahme erforderlich.

## Offline-Ergebnis

- 84 Integrationsprüfungen und 46 unabhängige Logikprüfungen bestehen.
- Die zwei gespeicherten griechischen Kartenfälle wählen weiterhin eine verbundene automatische Spur ohne unbestätigten Halt. Der erste Fall umfasst nur drei verifizierte Abschnitte, der zweite fünf. Die dortige Modellprüfung liefert keinen freigegebenen manuellen Zahlhaltepunkt; das darf nicht als Behebung dieser manuellen Zahlzonen ausgegeben werden.
- Noch keine echte Durchfahrt, kein physischer Bremsweg und kein neu bestätigter F9-/Controller-Test mit der neuen DLL. Die Tests prüfen veröffentlichte Steuerbefehle und unabhängige Reaktion auf gehaltene Diagnosearbeit, keine tatsächliche Fahrzeugwirkung.

## Baustellen und Hintergrundfahrt

In der geprüften Installation ist keine zuverlässige, validierte Quelle für alle temporären Baustellenabsperrungen angebunden. Die vorhandene Verkehrs-/Fahrzeugverarbeitung belegt keine Erkennung jeder Absperrung oder freien Ausweichspur. Eine experimentelle oder nur in anderen Fassungen erwähnte Schnittstelle wird nicht ungeprüft als Schutzfunktion eingebaut.

0.3.14 enthält deshalb keine vorausschauende Baustellenumfahrung. Die Oberfläche sagt dies ausdrücklich; in Baustellen manuell fahren. Die Kollisionsabschaltung aus 0.3.13 bleibt ein Schutz nach einer erkannten Kollisionssignatur und verhindert keinen ersten Aufprall. Das bekannte TruckersMP-Problem der Pedalausgabe nach Alt+Tab ist ebenfalls nicht behoben. Die Prüfung dieser Version erfordert heute keinen Spielstart.
