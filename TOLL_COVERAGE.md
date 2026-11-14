# Mautabdeckung und Baustellenbefund – 0.3.13

Stand: 14. November 2026. Diese Übersicht trennt Archivbefunde, Modellprüfung und tatsächliche Fahrnachweise. 0.3.13 ist vorbereitet, noch nicht installiert oder gefahren.

## Was rückwirkend untersucht wurde

Eine erneute Inventur des lokalen Diagnosearchivs findet 24 lesbare SDK-Fahrtdateien und 24 unterschiedliche Kombinationen aus Fehlerzeit und Fehlermeldung. Sieben Fahrtdateien enthalten zugeordnete oder räumlich nahe rohe Schrankendaten. Das sind **keine 24 Mautdurchfahrten oder 24 Straßenunfälle**: Darunter sind Standprüfungen, Teilaufnahmen, überlappende Mitschnitte und zusammengehörende Auslöser-/Fehlermeldungen. Das Inventurwerkzeug liest die gespeicherten Dateien und dedupliziert gleiche Fehler mit verschieden dargestellter Zeitzone.

Nicht aufgezeichnete Fahrten und bereits überschriebene Zustände lassen sich nicht wiederherstellen. Das Land wurde nicht systematisch gespeichert. Die Inventur rät es nicht aus Koordinaten oder Dateinamen. Damit ist eine vollständige erfolgreiche Abdeckung aller bisher befahrenen Länder ausdrücklich **nicht belegt**.

| Fehlergruppe | Befund und Stand |
| --- | --- |
| Mautmodell wird gar nicht geprüft | Bis 0.3.12 wurde nur `/prefab2/tollgate/` durchsucht. 16 von 81 aktuellen Modelldefinitionen mit `toll` im Pfad lagen außerhalb, unter anderem ältere polnische, italienisch/französische, österreichische und skandinavische Modelle. 0.3.13 liest den Prefab-Definitionskatalog unabhängig vom Ordner. Das allein gibt keine Spur frei. |
| Ältere Zahlungsdefinition wird als unbekannt verworfen | 0.3.13 prüft die tatsächlichen `tollgate`-Units einschließlich Typ, Aktivierungsgeschwindigkeit und zulässigem Schrankenmodell. Die 35 in diesen Modellen referenzierten und aufgelösten Maut-Units wurden offline geprüft: im normalen Auftrag 31 manuell, drei automatisch, eine nur für Spezialauftrag. Die Anzahl der Definitionen ist keine Anzahl physischer Fahrspuren. |
| Griechische Zusatzteile und breite Doppelschranke | Korrekturen aus 0.3.12 bleiben enthalten. Beide gespeicherten Kartenfälle wählen mit 0.3.13 wieder eine verbundene automatische Spur; der erste Fall ist nur über drei verifizierte Abschnitte rekonstruierbar, der zweite über fünf. Noch keine echte Durchfahrt mit der neuen DLL. |
| Anschluss nach Schranke, Straßenende, Spurwechsel oder ausbleibende Lenkung | Im Archiv enthalten; frühere Korrekturen und ihre Grenzen bleiben in `VALIDATION.md` dokumentiert. Die Inventur ist kein Beweis, dass alle historischen Fälle mit den heutigen Karten exakt reproduzierbar oder behoben sind. |
| Manuelle Zahlspur | Bleibt manuell. Automatische Zahlung wird nicht erfunden und keine Bestätigungstaste blind gesendet. Der bekannte Unterschied zwischen Haltepunkt und tatsächlicher Zahlzone bleibt praktisch zu prüfen. |

Der Modellkatalog stammt aus den aktuell lokal gelesenen Spielarchiven und sucht für diese Bestandsaufnahme nach `toll` im Modellpfad. Die neue Produktionserkennung verwendet diese Namenssuche nicht. Der Katalog ist weder eine Liste aller denkbaren Mautstellen noch ein Fahrtest jedes Landes. Modellvarianten und alle konkreten Platzierungen wurden dabei nicht vollständig geometrisch durchfahren. Unbekannte Attribute, fremde Kindmodelle und mehrdeutige Definitionen liefern weiterhin keine Freigabe.

## Baustellenunfall und Schutzumfang

Die gesicherte 0.3.11-Aufnahme zeigt etwa 15 Sekunden frische Route und Lenkausgaben. Vor dem abrupten Stopp lagen höchstens etwa 7 cm Abstand zur geplanten Linie vor. Bei knapp 80 km/h wurde Gas angefordert; ein Bremsbefehl ist nicht protokolliert. Danach folgen fast Stillstand und rückwärts gemessene Bewegung. Erst diese Rückwärtsbewegung löste den gesicherten Fehlerbericht aus.

Das ist mit einem Aufprall vereinbar. Die Aufnahme enthält weder das getroffene Objekt noch die Schadensänderung oder tatsächliche Spielpedale. Die statische Kartenprobe enthält im Umkreis keine separat platzierten Modellobjekte; sie beweist keine freie Fahrbahn, da Straßen-/Prefab-Geometrie und zur Laufzeit erzeugte Hindernisse damit nicht vollständig geprüft sind. Baustellenhindernis, anderes Fahrzeug und andere Physikursachen lassen sich aus diesen Daten nicht sicher unterscheiden.

0.3.13 ergänzt eine **Abschaltung nach einer Kollisionssignatur**: Ein großer, ungewöhnlich schneller Geschwindigkeitsverlust muss mit neuem Kabinen- oder Fahrgestellschaden in frischen, räumlich und zeitlich zusammenhängenden SDK-Proben zusammenfallen. Alte Schäden, normale starke Bremsung, Pausen, stillstehende Quelluhren, Teleport-artige Sprünge und deaktivierte Assistenz reichen nicht aus. Die Abschaltung veröffentlicht ihre Bremsanforderung ohne auf die Routenberechnung zu warten. F9 und physische Bremsübernahme lösen sie und verwerfen noch wartende Fehlerberichte.

Dies ist **keine vorausschauende Baustellenerkennung und verhindert nicht den ersten Aufprall**. Die alte Unfallaufnahme ohne Schadenswerte kann den neuen Detektor nicht endständig validieren. Die neue Bewegungsdiagnose bewahrt deshalb zusätzlich Schaden, tatsächliches Spielgas/-bremse und Simulations-/Renderzeit. Eine physisch wirksame Bremsung bleibt von Spiel und Ausgabeweg abhängig; das bekannte Fokusproblem unter TruckersMP ist nicht behoben.

## Wiederholbare Inventur

```powershell
python ./Tools/Audit-TollHistory.py 'Pfad-zum-lokalen-Archiv' 'Pfad-zur-Auswertung.json'
python -m unittest discover -s Tools -p test_toll_history.py
```

Die Auswertung gibt niemals automatisch eine vollständige Mautdurchfahrt frei. Nahe rohe Schranken können auf einer Nachbarspur oder Gegenfahrbahn liegen; veralteter Status darf keine Spurzuordnung erzeugen. Rohdaten bleiben lokal und sind nicht Bestandteil dieses Repositorys.
