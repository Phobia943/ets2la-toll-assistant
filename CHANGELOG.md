# Änderungen

## 0.2.5 – in Erprobung

- Schließt den Wiederanlauf ohne Mautbegrenzung nach einer Störung: Bei eingeschaltetem Plugin werden Startversuche ohne erfolgreiche Vorbereitung zurückgewiesen und erklärt.
- Prüfung beim Plugin-Tick, beim Empfang von Route und Telemetrie sowie in der ACC-Abfrage. Keine globale atomare Eingabesperre des Hostprogramms.
- Normaler F9-Stopp behält die Vorbereitung bei. Beobachtungsmodus stoppt die Fahrassistenz; reine Routenproben bleiben möglich.
- Wegfall des benötigten ACC-Moduls beendet die Fahrassistenz statt unbemerkt in Beobachtung weiterzulaufen.
- Diagnosen enthalten Vorbereitungsgrund, Startfreigabe, gewählte Spur und Mautanlagenkennung.
- Regressionstest reproduziert Fehlerabschaltung und wiederholte Starts über die echten Plugin-Einstiegspunkte mit isoliertem Hostzustand. 27 vollständige lokale Prüfungen bestehen. Keine automatische Erkennung der Höhenfreiheit hinzugefügt; Fahrtest bleibt offen.

## 0.2.4 – in Erprobung

- Routenmessung und Diagnose kopieren die veränderlichen Planungsdaten, bevor sie PathLib abfragen. Auch eine Abfrage mit ausgeschalteter Spurwechselbestätigung kann dort den Spurwechselzustand zurücksetzen oder initialisieren. Die Messung soll den Zustand der eigentlichen Lenkroute nicht ändern.
- Regressionstest gegen PathLib 1.2.8 reproduziert das Zurücksetzen des Zielspurindex und prüft die Trennung vom Original.
- Bei einer Routenunterbrechung werden Übergangsindex, Abstand, Elementtypen, Richtung und Spurindex gemeldet. Die bestehende 2-m-Prüfung bleibt erhalten.
- 26 lokale Prüfungen bestanden. Ein Fahrtest dieser Version und die erneute Prüfung direkt hinter der Schranke stehen aus.

## 0.2.3 – erste öffentliche Quellfassung

- Begrenzte Anfahrt und Durchfahrt mit Wartezeit nach Schrankenöffnung.
- Auswahl bestätigter automatischer Lkw-Mautspuren; manuelle und unbekannte Zahlarten führen zur Übernahme.
- Schrankenverfolgung bei manueller Abschaltung zurücksetzen.
- Wegmessung hinter einer freigegebenen Schranke nur bei weiterhin gültiger aktueller Navigation fortsetzen.
- Eine bereits passierte, wieder schließende Schranke löst keinen neuen Halt aus.
- Routendiagnose bei Fehlern an der Mautausfahrt.
- Öffentliche Bauanleitung mit frei konfigurierbarem Installationspfad.
- Eigene synthetische Testgeometrie und unabhängige Logiktests.
- Keine vorgegebenen Freigaben für einzelne Mautanlagen.

Die öffentliche Fassung basiert auf dem lokal geprüften Stand 0.2.3. Entfernte lokale Spurvorgaben und ersetzte Testdaten sind Anpassungen für die Veröffentlichung. Die erneute praktische Prüfung der Mautausfahrt bleibt offen.
