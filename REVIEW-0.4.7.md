# 0.4.7 – gemeinsamer Routenentscheid und klare Vorschauzustände

Stand: 18. November 2026. **Geprüfter Arbeitskandidat, nicht installiert. Der Gesamtauftrag ist nicht abgeschlossen.** Die abschließende unabhängige Gegenprüfung steht noch aus. Die bereits gemeldeten Gegenfälle wurden bearbeitet; eine abschließende Freigabe wird daraus nicht abgeleitet.

## Änderungen und Gegenfälle

- Die Aufzeichnung vom 18. November enthält eine 301 ms alte Route bei weiterhin gültigem Korridor und frischer Lenkung. Die Fahrüberwachung erlaubte Weiterfahrt, während die eigene Temporegelung ab 200 ms einen Null-Sollwert erzeugte. Beide benutzen jetzt dieselbe unveränderliche, am aktuellen Fahrzeugpunkt geprüfte Entscheidung. Ursprüngliche Routen- und Entscheidungsfristen laufen weiter; Pfadwechsel und manuelle Übernahme entziehen die alte Entscheidung.
- Schrankenbindung, Szenenkorridor, Halteboden und Rollen benutzen dieselbe geprüfte Geometrie. Die unabhängig gemessenen Tor-/Verkehrs-/Telemetriefristen werden nicht verlängert. Ein neues geschlossenes Tor bleibt wirksam; alte Tordaten werden weiterhin zurückgewiesen.
- Eine vollständig geprüfte gerade Strecke lieferte bisher dieselbe leere Kurvenentscheidung wie eine fehlende Vorschau. Der eigene Regler setzte dadurch dauerhaft `CurvePending` und eine Bremsgrenze. `TryEvaluate` unterscheidet jetzt gültig ohne begrenzende Kurve von ungültiger Auswertung. Der Fehler wurde vor der Korrektur im tatsächlichen Plugin-Tick reproduziert.
- Im Gegenreview fiel auf, dass der Wiederherstellungszustand bei verzögerten Routen neue Kurven-/Szenengrenzen übersprang. Auch dieser Gegenfall wurde zunächst reproduziert. Jetzt gelten weiterhin mindestens −0,5 m/s² und zusätzlich alle aktuell stärkeren Grenzen; ein neu fertig gewordenes enges Kurvenprofil kann nicht übersprungen werden.
- Bei manueller Zahlung bleibt eine noch nie ausgestellte eigene Reservierung bei vorübergehendem Fokus-/Datenverlust passiv erhalten. Physische Berechtigung und Stillstandsprüfung werden sofort verworfen. Nach Rückkehr gelten erneut dieselbe Sitzung, Quelle, Zahlzone und volle Stillstandszeit. Bereits ausgestellte, unklare oder explizit widerrufene Versuche werden nicht wiederholt. F9 und Quellen-/Sitzungswechsel widerrufen weiterhin.
- Wiederkehrender Status enthält nur die Identität und Zusammenfassung des letzten Fehlers samt Verweis auf `last-fault.json`. Der vollständige Vorfall mit Route und Bewegungshistorie wird weiterhin separat geschrieben. Damit wird ein großer historischer Bericht nicht jede Sekunde erneut serialisiert. Eine Verringerung der tatsächlichen Lenkausfälle ist damit noch nicht nachgewiesen.

## Getrennter Eingabekandidat

[InputBridge](Tools/InputBridge/README.md) behebt einen auch in der installierten Original-DLL nachgewiesenen ungültigen Callback-Kontext. Sein Diagnosekanal meldet echte Aktivitäts-/Callback-Ereignisse, keine erfundene Spielbestätigung. Der Gegenreview verlangte zusätzlich die Serialisierung möglicher paralleler Aktivitäts-/Ereignis-Callbacks; diese ist umgesetzt und separat getestet. Der finale Gegenreview dieser Ergänzung steht noch aus.

Kein Fokus-Hook und keine Veränderung des alten Befehlslayouts. Dieses Layout enthält keine ursprüngliche Befehlszeit: Es gibt somit keine neue spielseitige Ablaufgarantie bei Host-Absturz. Das Modul ist nicht installiert und beweist keine funktionierenden Hintergrundpedale in TruckersMP. Die Original-DLL bleibt unverändert.

## Ausgeführte Prüfungen

- 233 Integrationsgruppen: bestanden. Enthalten zwölf neue Gruppen zu tatsächlicher Pedalausgabe, gültiger/fehlender Kurvenvorschau, Wiederherstellung, Schrankenbindung, Halteboden, Zahlung und Diagnose.
- 100 unabhängige Kernlogikgruppen: bestanden.
- 304 Prüfungen mit den tatsächlichen gepinnten ETS2LA-Ausgabebibliotheken und dem neuen Plugin: bestanden, ohne Livekanäle oder Spielausgabe.
- 38 native InputBridge-Assertions einschließlich paralleler Callback-Writer und nativer → Python-Diagnosevertrag: bestanden.
- Paket- und eingebettete Hostmanifestprüfung: bestanden. Installation ist im vorbereiteten Skript bis zum ausstehenden Endreview gesperrt.
- [GitHub-Prüflauf für diesen Kandidaten](https://github.com/Phobia943/ets2la-toll-assistant/actions/runs/44280004565): Kernlogik, Aufnahmeauswertung sowie native Geometrie-/Eingabetests bestanden.

Plugin-SHA256: `8FD5297421DDBA2FE5A3FC3A6AD70E6BD79225AA294CC4C7B36EFBC9C3857611`.

InputBridge-Kandidat-SHA256: `0F4AA92E00D9E748DA2F320734F77313CAB7340079E61729A6265EFFFA7CAE3E`.

Hostdateien bleiben auf `0.4.4-host-1`. Lokal installiert bleibt 0.4.6, SHA256 `AAF69631EE06881340EEB89F58F79D2B0F4382FEE35CCDFA6B11E241608D838F`.

## Noch offen – ausdrücklich kein Gesamtabschluss

1. Finaler unabhängiger Gegenreview, danach Einbau im bestätigten Stillstand und Stand-/Fahrprüfung.
2. Lenklücke: Ursache für die aufgezeichneten rund 151 ms ohne neue Ausgabe nicht bewiesen. Diagnoseentlastung ist kein Beweis einer Behebung.
3. TruckersMP-Hintergrundpedale: tatsächliche Callback-, logische Eingabe- und Wirkungswerte vor/nach Alt+Tab fehlen für den neuen Adapter. [Upstream-Hintergrundthema](https://github.com/ETS2LA/ETS2LA/issues/642) und [experimentelle Pedalausgabe](https://github.com/ETS2LA/ETS2LA/issues/636) wurden erneut geprüft; daraus ergibt sich keine bestätigte TMP-Lösung.
4. Vollständige reale Mautpassage mit eigener Regelung einschließlich manueller Zahlung und Anschlussstraße. Die erneute Archivinventur zählt 43 lesbare SDK-Aufnahmen, davon 18 mit Torbeobachtungen, und 50 unterschiedliche gespeicherte Fehlerereignisse. Das sind keine 43 vollständigen Durchfahrten und kein Ländernachweis; 20 nicht lesbare/ungeeignete Archivdateien sind separat gemeldet.
5. Bahnübergang: Warnphase vor beginnender Schrankenbewegung bleibt mit den bisher aufgenommenen Daten unbelegt. Offene Actors allein beweisen keine Warnlichtfreiheit.
6. Baustellen und Schilder: Parkfahrzeug-/Verkehrskörper und Maut-Prefab-Geometrie erfassen nicht jedes statische Hindernis auf gewöhnlicher Straße. Die Kurvenkorrektur ersetzt keine fehlende Hindernisquelle oder ungeprüfte Sondergespanngeometrie.

Die lokale Lane-Assist-Einstellung `WidenTurnsWithTrailer` ist bereits `false`. Der weiterhin offene [Upstream-Fall 659](https://github.com/ETS2LA/ETS2LA/issues/659) rechtfertigt deshalb keine Behauptung, diese Funktion sei erst mit diesem Kandidaten abgeschaltet und der beobachtete Unfall damit erklärt.
