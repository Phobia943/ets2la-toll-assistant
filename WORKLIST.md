# Offene Produktarbeit ab 0.4.6

Stand: 18. November 2026. Auftrag: alle offenen Themen nacheinander bearbeiten, vorhandene Komponenten vereinfachen und jede wesentliche Änderung kritisch gegenprüfen. Keine pauschale Fertigmeldung aufgrund von Tests allein.

0.4.7 ist die zuletzt installierte und gestartete Fassung (SHA-256 8FD5297421DDBA2FE5A3FC3A6AD70E6BD79225AA294CC4C7B36EFBC9C3857611). Die Stand-/Fahrtaufnahme am 18. November zeigte zusätzliche Ausfälle; ein vollständig erfolgreicher Abnahmetest ist deshalb nicht belegt. Ältere Installationsnotizen sind historische Zustände.

Vor dem Einbau von 0.4.7 bestanden 233 Integrations-, 100 Kernlogik- und 316 tatsächliche Hostprüfungen. Der gesonderte InputBridge-Adapter ist nach 47 geschützten Prüfungen, Diagnosevertrag, gezielter Negativprobe und Endreview ebenfalls mit Sicherung installiert. Er hat das Hintergrundproblem im beobachteten Intervall nicht beseitigt. Die nächste Fassung 0.4.8 ergänzt ausgelagerte statische Hindernisprüfung und gezielte Ausgabediagnose. **Gesamtauftrag und reale Abnahme bleiben offen.** Details: [0.4.7](REVIEW-0.4.7.md), [0.4.8](REVIEW-0.4.8.md).

| Thema | Konkrete Arbeit / Abnahme | Zustand |
| --- | --- | --- |
| Routenalter und kleine Fehlbremsungen | Gemeinsame geprüfte Entscheidung für Überwachung, Pedale, Rollen und Schrankenbindung; gültige 301-ms-Route erzeugt keinen künstlichen Null-Sollwert. Ungültige Route und neue Gefahr bleiben wirksam. | Implementiert, gegengeprüft und installiert, Fahrt offen |
| Lenk-/Pedalausgaben | Wiederholte Vollserialisierung entfernt. Ein nativer Lenkabbruch wegen zu hoher Forderung ist nachgewiesen; andere Verzögerungen noch ungeklärt. Dauerhafter Pedalfehler und ursprüngliche Tickphasen ergänzt, keine Fristenverlängerung. | Neue Diagnose gegengeprüft; unterschiedliche Ursachen teilweise offen |
| Hintergrundpedale in TruckersMP | Kontextfehler behoben; Callback-Abfragen setzen im Hintergrund trotzdem aus. Letztes Aktivitätsflag blieb dabei true. Optionaler nativer Vorher-/Nachher-Probe für die moderne Ausgabe gegengeprüft, kein Wirkungsvorspiegeln. | Hintergrundfunktion weiterhin offen |
| Mautdurchfahrt | Akzeptierter Korridor bleibt an offenen Toren gebunden; frische geschlossene Tore bremsen weiterhin. Gesamtarchiv erneut inventarisiert; weder vollständige Passage noch Länderabdeckung daraus erfinden. | Routenfristfix getestet, reale Gesamtpassage offen |
| Manuelle Mautzahlung | Passive Reservierung vor erstem Impuls übersteht kurze Unterbrechung; bereits ausgestellte Versuche, F9 und neue Sitzung bleiben gesperrt. Tatsächliche Zahlung und Weiterfahrt gesondert bestätigen. | Implementiert, gegengeprüft und installiert, Fahrt offen |
| Bahnübergänge | 0.4.6: profilgebundene offene Schranken. Warnphase vor Bewegung und unbekannte/geschlossene Zustände gezielt prüfen, danach echte Durchfahrt. | Implementiert, Warnphase/Fahrnachweis offen |
| Baustellen, Schilder, enge Kurven | Gerade/fehlende Vorschau getrennt. Zusätzliche tatsächliche coll-Straßenmodelle in isoliertem Prozess; historische Baustellenprobe positiv, Originalfristen/Spurwechsel/F9/Prozessausfall geprüft. Kein vollständiger Gespannnachweis und keine automatische Hindernisumfahrung. | Zusätzliche Quelle implementiert und gegengeprüft; Fahrabnahme und enge nicht nachfahrbare Routen offen |

## Arbeitsregeln

- Erst reproduzieren, dann kleinste passende Änderung, unabhängiger Gegenfall, Tests und vorhandene Aufnahmen.
- Fehlende Beobachtung ist weder freie Straße noch bewiesener Fehler. Historische Fehlerdaten sind keine aktuelle Gefahr.
- Keine Änderungen an laufenden Fahrmodulen. Erst Paket und Sicherung prüfen, dann im bestätigten Stillstand installieren.
- Fahrtprüfung und Installation sind eigene Zustände. Ohne tatsächliche Messung bleiben sie offen.
- Kein neuer Zweig: GitHub `main` bleibt der einzige Arbeitszweig.
