# Optionale Messung der modernen Pedalausgabe

Diese Ergänzung beobachtet ausschließlich die bereits vorhandenen `input_mix`-Schreibvorgänge des gepinnten ETS2LA-SDK. Sie liest Gas und Bremse direkt vor und nach derselben vorhandenen Ausgabe. Sie ändert weder den Pedalwert noch eine Frist, das Fensterverhalten oder die Eingabemethode. Sie ist kein Fix für Hintergrundfahrt.

Der separate InputBridge-Test am 18. November zeigte im Spielvordergrund aktuelle SDK-Callbacks und ankommende logische Pedalwerte. Während des beobachteten Hintergrundintervalls wurden die Callbacks nicht weiter aufgerufen, obwohl die letzte SDK-Aktivitätsmeldung weiterhin `true` war. Ein gespeichertes Aktivitätsflag ist deshalb kein Empfangsnachweis. In diesem konkreten Hintergrundintervall wurde kein Gas angefordert; daraus wird keine neue Messung der Gaswirkung abgeleitet.

Die optionale Probe soll im nächsten Standvergleich zwischen erfolgreichem Schreiben in den modernen Eingabemix und tatsächlichem Spieleingang unterscheiden. Ein Vorher-/Nachher-Paar beweist keine Verarbeitung durch die Physik und keine Wirkung im nächsten Frame. Es wird nur der letzte Schreibvorgang gespeichert; der externe Leser ist keine verlustfreie Vollaufzeichnung.

## Bau und Prüfungen

`Native/ParkedScene/Build-SceneSdk.ps1 -IncludePedalProbe` ergänzt die Probe beim Bau einer neuen isolierten SDK-Fassung. Der normale Build bleibt unverändert. Der optionale Patch verlangt genau einmal vorhandene Anker aus der zuvor verifizierten Originalfassung; er setzt keine zusätzlichen Spiel-Getter, Speicheradressen oder Ereignis-Hooks ein. Die vorhandenen Mix-Objekte werden nur innerhalb ihres bisherigen Schreibblocks gelesen.

`Test.ps1` baut ausschließlich einen Testprozess mit einmaligem GUID-Kanal. Die 23 nativen Prüfungen decken Originalzeit, Vorher-/Nachher-Werte, konkurrierende Schreiber, ungültige Werte, Abmeldung und Sitzungswechsel ab. `Tools/InputBridge/TestPedalProbe.py` prüft das binäre Format unabhängig und kann die native Testdatei zusätzlich einlesen. Diese Tests öffnen keine Spielkanäle.

Die Produktionsdiagnose verwendet `Local\MautPedalProbe_v1`, 128 Byte, PID, Sitzung, QPC-Frequenz, Original-Befehlszeit und Vorher-/Nachher-Werte. Ein einzelner exklusiver Schreiber und eine durchgehende gerade/ungerade Sequenz schützen die Kopie. Der Leser öffnet vorhandenen Speicher ausschließlich lesend. `Tools/InputBridge/Record.py --pedal-probe` nimmt ihn optional neben Callback-, Fenster- und Telemetriedaten auf. Die Quellen sind nicht atomar zueinander.

Die Kandidatenfassung wird erst nach getrenntem Gegenreview und mit geschlossenem Spiel installiert. Eine erfolgreiche Kompilierung ersetzt den Standvergleich nicht.
