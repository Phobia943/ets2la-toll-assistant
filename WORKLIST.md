# Offene Produktarbeit ab 0.4.6

Stand: 18. November 2026. Auftrag: alle offenen Themen nacheinander bearbeiten, vorhandene Komponenten vereinfachen und jede wesentliche Änderung kritisch gegenprüfen. Keine pauschale Fertigmeldung aufgrund von Tests allein.

0.4.6 ist installiert (SHA-256 AAF69631EE06881340EEB89F58F79D2B0F4382FEE35CCDFA6B11E241608D838F), aber noch nicht im Spiel geprüft. Maßgeblich für den Einbau ist `outputs/Mautassistent-0.4.6-validation.json` im Arbeitsverzeichnis, nicht die älteren Sitzungsnotizen.

0.4.7 ist nach unabhängigem Endreview installiert: 233 Integration, 100 Kernlogik und inzwischen 316 tatsächliche Hostprüfungen bestanden. Der gesonderte InputBridge-Adapter ist nach 47 geschützten Prüfungen, Diagnosevertrag, gezielter Negativprobe und Endreview ebenfalls mit Sicherung installiert. **Stand-/Fahrprüfung und Gesamtauftrag bleiben offen.** Details: [REVIEW-0.4.7.md](REVIEW-0.4.7.md).

| Thema | Konkrete Arbeit / Abnahme | Zustand |
| --- | --- | --- |
| Routenalter und kleine Fehlbremsungen | Gemeinsame geprüfte Entscheidung für Überwachung, Pedale, Rollen und Schrankenbindung; gültige 301-ms-Route erzeugt keinen künstlichen Null-Sollwert. Ungültige Route und neue Gefahr bleiben wirksam. | Implementiert, gegengeprüft und installiert, Fahrt offen |
| Lenkausgaben | Wiederholte Vollserialisierung historischer Fehler entfernt. Tatsächliche Ursache der 151-ms-Aussetzer nicht nachgewiesen; keine Fristenverlängerung. | Diagnoseentlastung umgesetzt, Ursache offen |
| Hintergrundpedale in TruckersMP | Callback-Kontextfehler im vorherigen Binärprogramm nachgewiesen; kompatibler Adapter mit serialisierter Aktivitäts-/Ereignisdiagnose installiert. Echter Host → Callback geprüft; logischer Empfang → Wirkung im Hintergrund noch zu messen. | Gegengeprüft und installiert, TMP-Nachweis offen |
| Mautdurchfahrt | Akzeptierter Korridor bleibt an offenen Toren gebunden; frische geschlossene Tore bremsen weiterhin. Gesamtarchiv erneut inventarisiert; weder vollständige Passage noch Länderabdeckung daraus erfinden. | Routenfristfix getestet, reale Gesamtpassage offen |
| Manuelle Mautzahlung | Passive Reservierung vor erstem Impuls übersteht kurze Unterbrechung; bereits ausgestellte Versuche, F9 und neue Sitzung bleiben gesperrt. Tatsächliche Zahlung und Weiterfahrt gesondert bestätigen. | Implementiert, gegengeprüft und installiert, Fahrt offen |
| Bahnübergänge | 0.4.6: profilgebundene offene Schranken. Warnphase vor Bewegung und unbekannte/geschlossene Zustände gezielt prüfen, danach echte Durchfahrt. | Implementiert, Warnphase/Fahrnachweis offen |
| Baustellen, Schilder, enge Kurven | Geprüfte Gerade wird von fehlender Vorschau getrennt; neue Kurvengrenzen gelten auch bei verzögerter Route. Statische Straßenhindernisse außerhalb erfasster Quellen und Sondergespanne bleiben nicht vollständig abgedeckt. | Kurvenfix getestet, zusätzliche Hindernisquelle offen |

## Arbeitsregeln

- Erst reproduzieren, dann kleinste passende Änderung, unabhängiger Gegenfall, Tests und vorhandene Aufnahmen.
- Fehlende Beobachtung ist weder freie Straße noch bewiesener Fehler. Historische Fehlerdaten sind keine aktuelle Gefahr.
- Keine Änderungen an laufenden Fahrmodulen. Erst Paket und Sicherung prüfen, dann im bestätigten Stillstand installieren.
- Fahrtprüfung und Installation sind eigene Zustände. Ohne tatsächliche Messung bleiben sie offen.
- Kein neuer Zweig: GitHub `main` bleibt der einzige Arbeitszweig.
