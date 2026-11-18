# Isolierter Legacy-InputBridge-Kandidat

Dieser Kandidat behebt einen in der installierten Originaldatei nachgewiesenen Lebensdauerfehler und ergänzt Geräte-/Callback-Diagnose. Er ist **nicht installiert** und belegt keine funktionierenden TruckersMP-Hintergrundpedale. Build und Tests starten weder Spiel noch ETS2LA; die Tests verwenden eigene, zufällige Speichernamen.

## Konkreter Ausgangsbefund

Die installierte `scs_sdk_controller.dll` hat SHA256 `D9FBD5ACE5E76EED70B7E10B78C450B111566AD6AA814DFF65D6244AD23E687E`. `VerifyLegacy.py` liest ausschließlich ihre PE-Datei, ohne sie zu laden:

- `scs_input_init`, RVA `0x1820`, legt den Kontext auf dem Stack bei `rsp+0x30` an. Ab RVA `0x18aa` schreibt es dessen Adresse in `device.callback_context`; `device` liegt bei `rsp+0x40` und sein Kontextfeld bei `+0x20`.
- Der registrierte Ereigniscallback, RVA `0x16f0`, dereferenziert diesen Kontext später. Nach Rückkehr aus `scs_input_init` gehört der Speicher nicht mehr diesem Kontext.
- Die Tabelle ab RVA `0x3200` enthält 276 Eingaben in 342 Bytes. Relevant: `steering` Index 70/Offset 118, `aforward` 71/122, `abackward` 72/126 und `activate` 74/134.

Dies stimmt mit dem lokal geprüften [Legacy-Quellstand](https://github.com/ETS2LA/scs-sdk-controller/blob/27a7b0a6290b8d08be9bf1bea2de43dcf25d10e6/scs_sdk_controller.cpp) überein. Es beweist den Lebensdauerfehler **auch im konkreten Binärprogramm**, aber nicht, dass er die beobachtete Alt+Tab-Störung verursacht hat.

`LegacyInputs.h` enthält die faktischen Namen, Typen und Reihenfolge aus genau dieser Datei. Die Extraktion verweigert andere Dateihashes. Der Adapter ist eine kleine eigenständige Implementierung mit denselben offiziellen SCS-Callbacks, keinem übernommenen Original-Implementierungscode und unverändertem Legacy-Speicherlayout.

## Änderung und Diagnose

Der Callback-Kontext besitzt jetzt DLL-Lebensdauer. Die bisherigen Gerätenamen, der semantische Gerätetyp, alle 276 Eingaben und `Local\SCSControls` bleiben kompatibel. Ereignisse lesen jeweils den aktuell im Speicher stehenden Wert; es gibt keine Eingabewarteschlange und keinen dauerhaften Framespeicher. NaN/Unendlich werden neutralisiert, endliche Werte korrekt auf −1 bis +1 begrenzt. Anmeldung, Ablehnung und Abmeldung räumen ihre Ressourcen auf.

`Local\MautInputBridgeStatus_v1` enthält einen getrennten, versionierten Diagnoseblock. `ReadStatus.py` öffnet ausschließlich diesen bereits vorhandenen Block lesend; es erzeugt keinen Speicher und öffnet niemals den Befehlsblock. Die Ausgabe nennt:

- tatsächliche Geräte-Aktivitätsmeldungen, anfangs ausdrücklich unbekannt;
- QPC-Zeitpunkt der letzten Aktivitätsmeldung, des letzten Callbacks, Frames und Wiederaktivierungsflags;
- gestartete und vollständig abgefragte Frames sowie ausgegebene Ereignisse;
- je einen eigenen Frame, QPC und Wert für tatsächlich zurückgegebene Lenkung, Gas, Bremse und `activate`.

Eine nur begonnene Abfrage bestätigt keine späteren Pedalereignisse. Die Werte sind **SDK-Callback-Rückgaben**, keine Bestätigung des logischen Spieleingangs oder der Fahrzeugwirkung. Der Status ist nur mit identischer gerader Sequenz vor/nach der Kopie gültig. `(processId, sessionQpc)` bezeichnet die Sitzung; der Leser erneuert keine Zeitstempel. Ein alter Block ist keine aktuelle Aktivitätsbestätigung. Ein zweiter Eigentümer desselben Diagnoseblocks wird abgewiesen. Ein Leser soll den Block nach jeder Kopie schließen; ein gehaltenes Handle kann eine Neuanmeldung bis zu seinem Schließen verhindern.

## Fristen und verbleibende Beweisgrenze

Aktivitäts- und Ereignis-Callbacks sowie das Freigeben ihrer Speicheransichten sind durch dieselbe SRW-Sperre serialisiert. Ein paralleler Writer kann deshalb keine gerade Zwischensequenz vortäuschen. Die SDK-Anmeldung hält diese Sperre nicht, weil sie synchron Callbacks auslösen darf. Im DLL-Einstieg wird keine Sperre genommen; reguläre SDK-Abmeldung räumt auf. Die isolierte Prüfung umfasst 10.000 gleichzeitige Aktivitäts- und 10.000 Ereignisaufrufe. 38 native Assertions und der Diagnosevertrag bestehen; der abschließende unabhängige Review der Serialisierung steht noch aus.

Die bestehenden ursprünglichen Host-Befehlsfristen werden weder erhöht noch neu gestartet. Ein während Inaktivität vom Host gelöschter Befehl wird bei Wiederaktivierung nicht aus einem Adaptercache wiederholt. Das gilt auch für `activate`.

Das alte 342-Byte-Protokoll besitzt **keinen Ursprungszeitstempel, keine Sequenz und keine Sitzungskennung des Absenders**. Deshalb kann der kompatible Adapter nicht erkennen, ob ein unveränderter Wert gerade frisch geschrieben wurde oder nach einem Host-Absturz stehen blieb. Er behauptet keine zusätzliche spielseitige TTL, keine atomare Gesamtbefehlsaufnahme und keine neue Crash-/Zahlungs-Endegarantie. Eine solche Garantie benötigt einen gemeinsam angepassten Host mit ursprünglicher Befehlszeit und Generation; ein bloßer Beobachtungszeitstempel wäre falsch. Die bestehenden Host-/Zahlungswächter bleiben unverändert und müssen vor einer späteren Paketfreigabe zusammen mit diesem Kandidaten geprüft werden.

Die [offizielle SCS-Input-API](https://download.eurotrucksimulator2.com/scs_sdk_1_16.zip) fragt Ereignisse nur bei aktiven Geräten ab. `input_active_callback` meldet Änderungen; es aktiviert das Gerät nicht. Die [TruckersMP-Input-Dokumentation](https://github.com/TruckersMP/Docs/blob/master/client-sdk/modules/input.md) beschreibt Oberflächen-Eingaben und Sperren, keinen Pedal-/Hintergrund-Aktivierungsaufruf. Dieser Kandidat verwendet keinen Focus Keeper, Hook, privaten Spieloffset oder Eingriff in fremde Eingabesperren.

Der nächste belastbare Nachweis muss im Stand den ursprünglichen Hostbefehl, die Callback-Diagnose, den logischen Eingang und die effektiven Pedale über Vordergrund → Hintergrund → Vordergrund → Hintergrund zusammenführen. Bleiben die Callbacks bei inaktivem Gerät aus, löst dieser Adapter allein die Hintergrundsteuerung nicht. Werden Ereignisse ausgegeben, aber nicht logisch übernommen, liegt die nächste Grenze hinter dem Callback. Unterschiedliche logische und effektive Werte sind wiederum ein anderer Befund. Erst Gas **und** Bremse sowie Abschaltung/Übernahme bestehen zu lassen, genügt für den nächsten beaufsichtigten Fahrversuch; reine Speicherabholung genügt nicht.

## Reproduzierbare Offline-Prüfung

Aus dem Repository:

```powershell
python Tools/InputBridge/VerifyLegacy.py 'D:\SteamLibrary\steamapps\common\Euro Truck Simulator 2\bin\win_x64\plugins\scs_sdk_controller.dll' --check-header Tools/InputBridge/LegacyInputs.h
./Tools/InputBridge/Build.ps1 -Test
```

Der Build nutzt den lokal vorhandenen Zig-Compiler und die offizielle SDK-1.16-ZIP mit SHA256 `49821E25568F4D7E462135F577F8FEF2AB197326404F2C1DAEF139B8F3DC5E69`. Es gibt keinen Download oder Installationsschritt. Das getrennte Ergebnis heißt `bin/scs_sdk_controller.candidate.dll` und darf nicht zusätzlich zur Original-DLL im Spiel geladen werden.

Der Fake-SDK-Host überprüft vor der Initialisierung einen nur im Testbuild vorhandenen Export. DLL und Host werden mit derselben zufälligen Testkennung kompiliert; sämtliche Eingaben gehen an diese isolierten Testkanäle. Geprüft werden die vollständige ABI-Tabelle, Kontextspeicher nach Rückkehr/Stacküberschreiben, Teilabfragen, unbekannte Aktivität, Aktivitätswechsel, tatsächliche Ereigniswerte, Wiederaktivierung nach Host-Neutralisierung, ungültige Zahlen, Abmeldung, Ablehnung und Neuanmeldung. Der Python-Leser wird gegen einen vom echten nativen Testadapter geschriebenen Diagnoseblock geprüft. Ein ausdrücklicher negativer Test hält fest, dass rohe Legacy-Werte keine beweisbare spielseitige TTL besitzen.

Die Prüfungen ersetzen weder den TruckersMP-Standnachweis noch die bestehenden Produkt-/Hostregressionen. Installation und Liveversuche gehören nicht zu diesem isolierten Build.

Prüflauf am 18. November 2026: **35 native Assertions und der native/Python-Diagnosevertrag bestanden, Exit 0**. Der nicht geladene Produktkandidat hat SHA256 `76469C162DAB6B95FB412AEF88985124596EB1C7920A937DD75ACCBAAE3C5F92` und exportiert ausschließlich `scs_input_init`/`scs_input_shutdown`. Der zusätzliche Testmarker ist ausschließlich im getrennten Testbuild vorhanden. Der Originalhash und die vollständige ABI-Tabelle wurden danach erneut lesend bestätigt. Der lokale Compiler benötigte für den Zig-CRT-Cache die erhöhte Ausführungsumgebung; alle Ausgaben blieben im Arbeitsbereich, sämtliche Tests in den zufälligen Testkanälen.
