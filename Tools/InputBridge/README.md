# Kompatibler Legacy-Eingabeadapter

Dieser Adapter behebt einen in der vorherigen Originaldatei nachgewiesenen Lebensdauerfehler und ergänzt Geräte-/Callback-Diagnose. Er wurde am 18. November um 11:17 MEZ nach unabhängigem Review mit geprüfter Originalsicherung installiert. **Die tatsächliche TruckersMP-Hintergrundwirkung ist noch nicht belegt.** Build und Tests starten weder Spiel noch ETS2LA; die Tests verwenden eigene, zufällige Speichernamen.

## Konkreter Ausgangsbefund

Die vorher installierte `scs_sdk_controller.dll` hat SHA256 `D9FBD5ACE5E76EED70B7E10B78C450B111566AD6AA814DFF65D6244AD23E687E`. `VerifyLegacy.py` liest ausschließlich diese Originaldatei beziehungsweise ihre exakte Sicherung, ohne sie zu laden:

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

Aktivitäts- und Ereignis-Callbacks sowie das Freigeben ihrer Speicheransichten sind durch dieselbe SRW-Sperre serialisiert. Ein paralleler Writer kann deshalb keine gerade Zwischensequenz vortäuschen. Die SDK-Anmeldung hält diese Sperre nicht, weil sie synchron Callbacks auslösen darf. Im DLL-Einstieg wird keine Sperre genommen; reguläre SDK-Abmeldung räumt auf. Die isolierte Prüfung umfasst 10.000 gleichzeitige Aktivitäts- und 10.000 Ereignisaufrufe sowie eine deterministisch angehaltene Schreibphase mit Gegenprobe. 47 native Assertions und der Diagnosevertrag bestehen; der abschließende unabhängige Review hat die Fassung für einen beaufsichtigten Standtest freigegeben.

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

Die Schreiber werden gemeinsam mit der Freigabe ihrer Speicheransichten durch eine kurze SRWLOCK geschützt. Der Build prüft zusätzlich zu je 10.000 konkurrierenden Callback-Aufrufen eine gezielte Zwischenlage: Der Ereigniscallback wartet nach einer ersten Teiländerung bei ungerader Sequenz. Der bereits eingetroffene Aktivitätscallback darf diese Transaktion nicht betreten. Erst nach ausdrücklicher Freigabe laufen beide weiter.

Eine **absichtlich ungeschützte Negativvariante** wiederholt denselben Ablauf. Beide Schreiber werden mitten in ihren Transaktionen angehalten; ohne Sperre ist die Sequenz dabei gerade und der Leser akzeptiert den teilweise aktualisierten Status. Dieser Fehler muss in der Negativprobe tatsächlich nachgewiesen werden. Die korrekten Endzähler beider Varianten reichen ausdrücklich nicht als Synchronisationsbeweis.

Die Negativdatei heißt ausschließlich `bin/InputBridge.UNSAFE-NEGATIVE-TEST.dll`, hat einen eigenen Negativ-Testmarker und eine andere zufällige GUID als die geschützte Test-DLL. Sie ist **kein Kandidat und darf weder installiert noch in ein Produktpaket aufgenommen werden**. Der Präprozessor verweigert die ungeschützte Variante ohne Testkennung. Barrieren, Testmarker und deren Warteaufrufe fehlen im Produktkandidaten; dessen Exporte bleiben auf die beiden SCS-Einstiegspunkte beschränkt.

Die Prüfungen ersetzen weder den TruckersMP-Standnachweis noch die bestehenden Produkt-/Hostregressionen. Installation und Liveversuche gehören nicht zu diesem isolierten Build.

Prüflauf am 18. November 2026: **47 Assertions der geschützten Testvariante und der native/Python-Diagnosevertrag bestanden, Exit 0**. Die getrennte Negativvariante bestand **44 Prüfungen einschließlich der erwarteten Reproduktion des früheren Writerfehlers**, Exit 0; die überlappenden Grundprüfungen werden nicht addiert. Der nicht geladene Produktkandidat hat SHA256 `D92DA97344E33DABA0BC80E3811942DEDBA51C1367DDFC240F9EB0377A628B9B` und exportiert ausschließlich `scs_input_init`/`scs_input_shutdown`. Der Originalhash und die vollständige ABI-Tabelle wurden erneut lesend bestätigt. Der lokale Compiler benötigte für den Zig-CRT-Cache die erhöhte Ausführungsumgebung; alle Ausgaben blieben im Arbeitsbereich, sämtliche Tests in den zufälligen Testkanälen. Diese Änderung wurde lokal geprüft; ein erneuter CI-Lauf ist ein gesonderter Nachweis.

## Bewachter Einbau und Rücksetzen

`Install.ps1` betrifft ausschließlich `scs_sdk_controller.dll`. **Ohne Modusangabe gilt `ValidateOnly`: keine Sicherungs-, temporäre oder Zieldatei wird geschrieben.** Standardziel ist der bisherige ETS2-Pluginordner unter `D:\SteamLibrary\steamapps\common\Euro Truck Simulator 2\bin\win_x64\plugins`; `-GamePluginDirectory` kann ihn ausdrücklich ersetzen. `-ApplicationDirectory` bezeichnet die ETS2LA-Installation, standardmäßig `D:\ETS2LA\current`.

```powershell
# Ausschließlich lesen und prüfen:
./Tools/InputBridge/Install.ps1

# Ablehnungsfälle und Rundlauf nur in temporären Workspace-Testordnern:
./Tools/InputBridge/Test-Install.ps1
```

Erst der ausdrücklich gewählte Modus `-Mode Install` tauscht die Datei aus. Er verlangt das oben gepinnte Original und genau den Kandidaten `D92DA9…28B9B`. Ein eigener PE-Leser prüft x64-DLL, genau `scs_input_init`/`scs_input_shutdown`, die Produktkanäle und fehlende Testmarker. Die DLL wird dazu nicht geladen. Eine weitere Datei mit einer der beiden Controlleridentitäten im Pluginordner wird als Duplikat abgewiesen. Verknüpfte Zielverzeichnisse und Zieldateien werden nicht verwendet.

Die Prozessliste muss belegen, dass Spiel, TruckersMP und ETS2LA geschlossen sind. Ein fehlgeschlagenes oder leeres Inventar reicht nicht. Der Installer sichert die Zieldatei zunächst als eindeutig benannte `.bin` unter `MautInputBridgeBackups`, schreibt die Sicherung auf den Datenträger und prüft ihren Hash. Eine `.pending`-Datei im Zielordner wird ebenfalls vollständig geschrieben und geprüft. Unmittelbar vor dem atomaren `File.Replace` folgt eine zweite Prozessprüfung. Nachher werden Ziel und Sicherung erneut geprüft. Es entsteht keine zweite `.dll` neben dem Controller; eine abgewiesene Vorbereitung entfernt nur ihre eigene `.pending`-Datei und lässt die geprüfte Sicherung bestehen.

`-Mode Restore -BackupFile <ausdrücklich ausgewählter Originalbackup-Pfad>` ist nur zulässig, wenn die aktuelle Zieldatei exakt der bekannte Kandidat und das Backup exakt das gepinnte Original ist. Auch das Rücksetzen sichert die ersetzte Datei und prüft die Prozesse zweimal. Unbekannte oder bereits anderweitig geänderte Installationen werden nicht überschrieben. Ein Fehler nach erfolgtem Austausch wird ausdrücklich als solcher gemeldet, mit Sicherungspfad; es erfolgt kein unbeaufsichtigter zweiter Austausch.

Installerprüfung am 18. November 2026: **26 Prüfungen bestanden, Exit 0**. Dazu gehören Default-Readonly, falsche Kandidaten/Originale/Backups, beide Test-DLLs, laufende Anwendungen, fehlende Prozessnachweise, Duplikate sowie ein Spielstart zwischen Vorbereitung und Austausch. Ein vollständiger Install→Restore-Rundlauf bestätigte Ziel- und Backuphashes in einem neu angelegten GUID-Testverzeichnis. Die installierte Original-DLL wurde nur gelesen; kein Spiel-/Appprozess wurde gestartet, keine Bibliothek geladen und kein Liveeinbau durchgeführt. Diese Skriptprüfung ersetzt weiterhin weder den echten Host→Bridge-Vertrag noch den TruckersMP-Standnachweis.

## Übertragung lesend aufzeichnen

`python Tools/InputBridge/Record.py <neue-datei.jsonl> --seconds 60` liest nur vorhandene Befehls-, Callback- und Spieltelemetriekanäle. Fehlende oder kurz unvollständige Bridge-Daten werden als Messlücke protokolliert, die Aufnahme läuft weiter. Die 13 Offlineprüfungen in `TestRecord.py` unterscheiden logische und effektive Pedale und prüfen die Erholung nach einem unvollständigen Status.

Die Maps sind keine gemeinsame atomare Aufnahme. Auch Legacy- und Telemetrieblöcke besitzen hier keinen Sequenzvertrag. Der Vordergrundvergleich bezieht sich ausdrücklich nur auf die vom Adapter gemeldete PID; die aktuelle Prozessidentität muss separat bestätigt werden. Ein unveränderter Legacy-Wert erhält keinen künstlichen Ursprungszeitstempel.

Der zusätzliche [Vertragstest mit den gepinnten Hostbibliotheken](../HostCompatibility/INPUT_BRIDGE_REPLAY.md) besteht: 316 Prüfungen insgesamt, darunter zwölf neue bis zum nativen Callback, plus drei gezielte Isolation-Gegenproben. Dieser Offlinepfad ersetzt keine Spiel-/Physikmessung.
