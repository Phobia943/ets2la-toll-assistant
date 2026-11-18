# Laufzeitdiagnose der Lenklücke

Ein separater Beobachter zeichnet Laufzeitereignisse auf, ohne Spielmodule auszutauschen, Fenster zu aktivieren oder Fahrzeugbefehle zu schreiben. Er soll die ungeklärten rund 151 ms ohne neue Lenkausgabe zwischen Speicherbereinigung, Threadpool-Arbeit und längeren Funktionsaufrufen eingrenzen. Der bestehende Ausgabewächter bleibt unverändert.

Microsofts `dotnet-trace` wird außerhalb der Spielinstallation bereitgestellt. Die lokale Fassung ist 10.0.750501. `Capture.ps1` verlangt eine konkrete Prozess-ID aus der angegebenen ETS2LA-Installation und einen neuen Aufnahmeordner. Standardmäßig werden höchstens 60 Sekunden leichte Laufzeitereignisse erfasst. `-IncludeManagedStacks` ergänzt Stichproben verwalteter Threadstapel. Zunächst im Stand den zusätzlichen Aufwand prüfen; keine absichtlich provozierten Ausfälle während der Fahrt.

```powershell
./Capture.ps1 -TargetProcessId 1234 -EtsRoot D:/ETS2LA/current -TraceTool C:/tools/dotnet-trace.exe -OutputDirectory C:/captures/runtime-001
dotnet run --project . -c Release -- C:/captures/runtime-001/runtime.nettrace C:/captures/runtime-001/analysis
```

Die Prozess-ID und Pfade sind Beispiele. Zusätzlich läuft die bestehende getrennte Steuerungs-/Telemetrieaufnahme; die Zeitstempel der Originalbefehle bleiben maßgeblich. Der Offline-Leser schreibt Laufzeitereignisse und Threadstichproben mit UTC-Zeit. Mit zwei optionalen UTC-Argumenten kann man das Zeitfenster um einen tatsächlich gemessenen Ausfall begrenzen. Die Ereignisverlustzahl und fehlende Stapel werden ausgewiesen.

Die Aufnahme ergänzt die Standard-Laufzeitereignisse ausdrücklich um Lock-Contention (Maske `0x100003C01D`) und setzt `--resume-runtime:false`; ein diagnostisch angehaltener Prozess wird nicht fortgesetzt. Die sieben Sekunden lange Offlineprobe mit absichtlichen Speicherbereinigungen ergab 4706 aufgelöste Stapelstichproben, 39273 relevante Laufzeitereignisse und keine vom Decoder gemeldeten Verluste. Sie war noch ohne das zusätzliche Contention-Keyword und beweist keine tatsächliche Lenkausfallursache.

Ein Threadstapel beweist weder CPU-Auslastung noch einen Kernel-Schedulingfehler. Die Aufzeichnung selbst hat Aufwand. Fehlende Ereignisse sind kein Beweis für ununterbrochenen Betrieb. Die Ursache wird erst nach zeitlicher Korrelation mit dem tatsächlichen Vorfall bewertet.

`--fixture` startet nur einen eigenen sieben Sekunden langen .NET-Testprozess mit gezielten Speicherbereinigungen. Damit lässt sich die komplette Aufnahme-/Lesekette unabhängig von Spiel und Assistenz prüfen.

Quellen: [Microsoft dotnet-trace](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-trace), [TraceEvent/TraceLog](https://github.com/microsoft/perfview/blob/main/src/TraceEvent/TraceLog.cs).
