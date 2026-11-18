# Offene Produktarbeit ab 0.4.6

Stand: 18. November 2026. Auftrag: alle offenen Themen nacheinander bearbeiten, vorhandene Komponenten vereinfachen und jede wesentliche Änderung kritisch gegenprüfen. Keine pauschale Fertigmeldung aufgrund von Tests allein.

**0.4.11 ist offline geprüft und mit verifizierter Sicherung installiert.** Getrennte Korrekturen betreffen kurze Mautausfahrten, eigenständige Grenzschranken, den Pause-Übergang im tatsächlichen Stillstand, die Rückkehr nach belegtem Fokusverlust, frische Gefahren unmittelbar vor der Gasausgabe und zusätzliche Konfliktprüfung für den nachlaufenden Auflieger. Neustart und Fahrprüfung stehen aus. Die technische 175-ms-Ausgabepause sowie der getrennte native Planerabbruch sind nicht als behoben erklärt. [Umfang und Endprüfung](REVIEW-0.4.11.md).

Die **zuletzt im Spiel beobachtete Fassung ist 0.4.10**: wiederholte Katalogladungen entfernt, Quellenwechsel und unvollständige Hookups abgesichert. Nach bewusstem F8 sind 26 Sekunden Parkneutralisierung mit aufgezeichneten Pedalen 0/0 sowie anschließendes Anfahren belegt. Die erneute Controllerübernahme und vollständige Fahrabnahme stehen aus. Zwei weitere technische Ausgabeausfälle sind untersucht; deren Kausalität bleibt offen. [Gegenprüfungen und Messgrenzen](REVIEW-0.4.10.md).

Historisch wurde 0.4.9 um 13:45 MEZ installiert; die folgenden neuen Fahrbefunde stammen aus 0.4.8. Die reale Fahrt zeigte einen Gegenrichtungshalt an grüner Kreuzung und eine vom Nutzer verhinderte Pfostenkollision des innen nachlaufenden Aufliegers. Die gezielte Korrektur der nativen Kreuzungssuche ist eingebaut; Neustart und geladene Version sind bestätigt, Fahrprüfung steht aus. Gas und Bremse bei Vordergrundfahrt sind belegt, Hintergrundübertragung und abschließende Übernahmeprüfung bleiben offen. **Gesamtauftrag nicht abgeschlossen.** [Nachweise und Grenzen](REVIEW-0.4.9.md).

| Thema | Konkrete Arbeit / Abnahme | Zustand |
| --- | --- | --- |
| Wiederholte Modell-/Hookup-Kataloglast | Vollständige Daten pro Quellengeneration wiederverwenden; Queue-Leerlauf, Quellenwechsel, Lesefehler und widersprüchliche Zahlart gegengeprüft. Erstladung und Archivöffnung bleiben messbare Last. | 0.4.10 geladen; Parkneutralisierung und Anfahren belegt, vollständige Fahrtabnahme und Einfluss auf Ausgabeausfälle offen |
| Falsche Gegenrichtung an Kreuzung | Native Suche folgt gerichteten NextLines statt Vor-/Rückkanten zu mischen; beide korrekten Wege erhalten, echte falsche Alternativen ausgeschlossen. | 0.4.9 installiert, Gegenreview/Offlineprobe bestanden, geladen, Fahrt offen |
| Innen nachlaufender Auflieger | Gemessenen Nachlauf eines einzelnen starren Aufliegers bis 20 km/h gegen bekannte feste Hindernisse prüfen. Eigener tatsächlicher Konfliktfortschritt statt Pfostenprojektion; keine pauschale Korridorverbreiterung. | Positive Konfliktbremsung in 0.4.11 geprüft und installiert; automatische weiter ausholende Fahrkurve, allgemeine Hindernisabdeckung und Fahrt offen |
| Routenalter und kleine Fehlbremsungen | Gemeinsame geprüfte Entscheidung für Überwachung, Pedale, Rollen und Schrankenbindung; gültige 301-ms-Route erzeugt keinen künstlichen Null-Sollwert. Ungültige Route und neue Gefahr bleiben wirksam. | Implementiert, gegengeprüft und installiert, Fahrt offen |
| Lenk-/Pedalausgaben | Wiederholte Vollserialisierung entfernt. Ein nativer Lenkabbruch wegen zu hoher Forderung ist nachgewiesen; andere Verzögerungen noch ungeklärt. Dauerhafter Pedalfehler und ursprüngliche Tickphasen ergänzt, keine Fristenverlängerung. | Neue Diagnose gegengeprüft; unterschiedliche Ursachen teilweise offen |
| Hintergrundpedale in TruckersMP | Kontextfehler behoben; Callback-Abfragen setzen im Hintergrund trotzdem aus. Letztes Aktivitätsflag blieb dabei true. Optionaler nativer Vorher-/Nachher-Probe für die moderne Ausgabe gegengeprüft, kein Wirkungsvorspiegeln. | Hintergrundfunktion weiterhin offen |
| Beschleunigung nach Rückkehr ins Spiel | Ausschließlich belegte Hintergrund-Empfangsunterbrechung separat halten; neuer Versuch nur bei stabiler Rückkehr, fortschreitender Telemetrie, aktueller Lenkausgabe und unveränderter Freigabe. Bestehende echte Fehler bleiben erhalten. | 0.4.11 geprüft und installiert; Wirkung im Spiel noch zu messen |
| Fehlerhalt beim Verlassen einer Pause im Stand | Frisch gemessener Stillstand erzeugt bei noch pausiertem Planer eine Startablehnung statt neuer Fehlerverriegelung. Alte Fehler und Bewegung werden nicht quittiert. | Gegenfall und Kontrollfluss geprüft, 0.4.11 installiert; Spielabnahme offen |
| Kurze Mautausfahrt | Tatsächlichen 118,76-m-Anschluss reproduzieren; vorhandene 45-m/20-km/h-Regel mit fortbestehender Tempobegrenzung, Routenpruning und frischer Aufnahme nach F9 nutzen. | 0.4.11 gegengeprüft und installiert; vollständige Durchfahrt offen |
| Eigenständige Grenzschranken | Gerichtete Aktivierungsfläche der realen Map.Gate-Instanz an eigenen SDK-Pfosten binden; normale Haltebedingung nur nach eindeutigem Open aufheben. | 0.4.11 gegengeprüft und installiert; keine automatische Grenzabfertigung behauptet |
| Mautdurchfahrt | Akzeptierter Korridor bleibt an offenen Toren gebunden; frische geschlossene Tore bremsen weiterhin. Gesamtarchiv erneut inventarisiert; weder vollständige Passage noch Länderabdeckung daraus erfinden. | Routenfristfix getestet, reale Gesamtpassage offen |
| Manuelle Mautzahlung | Passive Reservierung vor erstem Impuls übersteht kurze Unterbrechung; bereits ausgestellte Versuche, F9 und neue Sitzung bleiben gesperrt. Tatsächliche Zahlung und Weiterfahrt gesondert bestätigen. | Implementiert, gegengeprüft und installiert, Fahrt offen |
| Bahnübergänge | 0.4.6: profilgebundene offene Schranken. Warnphase vor Bewegung und unbekannte/geschlossene Zustände gezielt prüfen, danach echte Durchfahrt. | Implementiert, Warnphase/Fahrnachweis offen |
| Baustellen, Schilder, enge Kurven | Gerade/fehlende Vorschau getrennt. Zusätzliche tatsächliche coll-Straßenmodelle in isoliertem Prozess; historische Baustellenprobe positiv, Originalfristen/Spurwechsel/F9/Prozessausfall geprüft. Kein vollständiger Gespannnachweis und keine automatische Hindernisumfahrung. | Zusätzliche Quelle implementiert und gegengeprüft; Fahrabnahme und enge nicht nachfahrbare Routen offen |

Neuer Nachweis unter 0.4.10 um 14:39 MEZ: Nach aufgehobenem Ampelhalt und erneutem Anfahren führte eine 174,95-ms-Pedalausgabepause wieder zum Fehlerhalt. Die Katalogoptimierung beseitigt diese Ausfallklasse nicht vollständig. Der verspätete Beginn des nächsten Durchlaufs ist belegt; die genaue Laufzeitursache bleibt zu klären. Details im 0.4.10-Review.

## Arbeitsregeln

- Erst reproduzieren, dann kleinste passende Änderung, unabhängiger Gegenfall, Tests und vorhandene Aufnahmen.
- Fehlende Beobachtung ist weder freie Straße noch bewiesener Fehler. Historische Fehlerdaten sind keine aktuelle Gefahr.
- Keine Änderungen an laufenden Fahrmodulen. Erst Paket und Sicherung prüfen, dann im bestätigten Stillstand installieren.
- Fahrtprüfung und Installation sind eigene Zustände. Ohne tatsächliche Messung bleiben sie offen.
- Kein neuer Zweig: GitHub `main` bleibt der einzige Arbeitszweig.
