# Echter Hostausgang bis zum nativen Eingabecallback

Diese optionale lokale Probe verbindet den festgelegten, isolierten ETS2LA-Host mit der geschützten InputBridge-Test-DLL. Sie installiert nichts und öffnet weder `Local\SCSControls` noch den produktiven Diagnosekanal. Die normale Hostprüfung bleibt ohne die Umgebungsvariable unverändert.

Zunächst `Tools/InputBridge/Build.ps1 -Test` nach den dortigen Voraussetzungen ausführen. Anschließend vor dem üblichen `HostCompatibility verify` setzen:

```powershell
$env:MAUT_INPUT_BRIDGE_TEST = (Resolve-Path './Tools/InputBridge/bin/InputBridge.Test.dll').Path
dotnet ./Tools/HostCompatibility/bin/Release/net10.0/HostCompatibility.dll verify $env:EtsRoot $prepared --originals $originals
Remove-Item Env:MAUT_INPUT_BRIDGE_TEST
```

`$prepared`, `$originals` und `$env:EtsRoot` haben dieselbe Bedeutung wie in [README.md](README.md). Der HostCompatibility-Build muss den neuen Helper enthalten. `MAUT_PLUGIN_CANDIDATE` und `MAUT_CANONICAL_FIXTURE` können wie bisher zusätzlich gesetzt werden. Die Probe verlangt Windows x64.

Vor dem Laden werden Dateiname und die beiden eingebetteten GUID-Kanalnamen geprüft. Produktkanäle, abweichende Namensräume und die negative Kontroll-DLL werden abgelehnt. Vor `scs_input_init` muss außerdem der geschützte Testmarker vorliegen. Der SDK-Registrierungsaufruf prüft alle 276 Eingaben einschließlich ihrer Namen, Typen und Offsets gegen das tatsächliche Hostlayout.

Die originale, gepinnte Hostlogik für `OnControlEvent`, `MautFrame`, Sitzungswechsel und Ausgabeentscheidung bleibt erhalten. Nur die bereits isolierten Schreibendpunkte wechseln vom Testwörterbuch in den GUID-Speicher der Test-DLL. Deren echte SCS-Callbacks liefern anschließend die Eingaben an den kleinen verwalteten Testhost zurück. Produktionskonstruktoren, Produzententicks, Ausgabeschleifen und Live-Speicheröffnungen bleiben gesperrt.

Zwölf zusätzliche Prüfungen decken Gas, Bremse, Rückgabe der Steuerung, abgewiesene alte Sitzung, ursprünglichen Befehlsablauf während inaktiver SDK-Abfrage, Wiederaktivierung, abgewiesene Wiederholung veralteter Befehle und eine Übernahme mitten in einer teilweise abgefragten Eingabefolge ab. Die Aktivierungsdiagnose wird unabhängig von den Kommandowerten gelesen. Beim lokalen Stand 0.4.7 bestanden damit insgesamt 316 Prüfungen einschließlich der bestehenden Host-/Pluginproben.

Die Lenkung des aktuellen Hosts verwendet den modernen Ausgang. Die Probe bestätigt dessen Wert und Neutralisierung und kontrolliert, dass keine Lenkung fälschlich als Legacy-Ausgabe behauptet wird. Sie prüft keine moderne spielseitige Lenk-DLL. Der manuelle Abschaltfall verwendet den tatsächlichen Hostzustandswechsel, den auch F9 auslöst; der physische Tastendruck wird hier nicht simuliert.

Der Befehlsablauf setzt weiterhin voraus, dass der Host seine Ausgabeprüfung ausführt. Die ältere gemeinsam genutzte Eingabeschnittstelle enthält selbst keinen Ursprungstempel; diese Probe belegt daher keine Neutralisierung nach einem vollständigen Hostabsturz. Auch tatsächliche Spieleingabe, Bremswirkung, Hintergrundfahrt unter TruckersMP und reale Mautdurchfahrten sind damit nicht bewiesen. Die JSON-Ergebniszeile nennt die geprüfte Test-DLL-Prüfsumme, den GUID-Kanal und diesen begrenzten Prüfungsumfang.
