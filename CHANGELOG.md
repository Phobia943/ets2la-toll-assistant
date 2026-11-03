# Änderungen

## 0.2.6 – Ausfahrtkorrektur, Fahrtest ausstehend

- Verbindet die bestätigte Mautspur mit der geometrisch anschließenden Spur der bereits geplanten Straße. Ein korrekter Zielspurindex allein genügt nicht, wenn die Straße noch auf einer anderen Eingangsspur beginnt.
- Prüft gemeinsamen Routenknoten, Fahrtrichtung, Abstand und Eindeutigkeit. Kein Wechsel auf die Gegenseite, keine erfundene Anschlussstraße und keine nachträgliche Korrektur, wenn der Lkw bereits auf der anderen Spur fährt. Unklare Übergaben führen zur manuellen Übernahme.
- Ändert den von Pathfinding intern weiterverwendeten Planungseintrag und setzt dessen Spurwechselzustand über die öffentliche PathLib-Schnittstelle zurück. Die zugrunde liegenden Kartendaten bleiben unverändert.
- Unterteilt ausgewählte Mautkurven für die native Positionsprojektion. Ein synthetischer Kurvenfall erzeugte zuvor mehr als 2,2 m scheinbare Abweichung trotz korrekter Position. Die gespeicherte Mautgeometrie zeigte diesen starken Fehler nicht; dies ist kein Nachweis der Ursache des gemeldeten Vorfalls.
- Exakte Segmentlängensummen vermeiden Rundungslücken an den Kurvenendpunkten. Rückwärts durchlaufene einzelne Straßen werden auch in der Diagnose korrekt abgetastet.
- 29 lokale Prüfungen bestehen, darunter Anschluss-Spuren in beiden Richtungen, einzelne und gruppierte Straßen, native Folgeabfragen, Gegenfahrbahn-Ausschluss und mehrdeutige Übergänge. Fahrtest auf Wunsch verschoben; die betroffene Ausfahrt ist noch nicht praktisch bestätigt.

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
