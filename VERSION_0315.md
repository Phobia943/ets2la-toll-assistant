# 0.3.15: Bremsreaktion überwachen und Fahrzeugmessung vorbereiten

Stand: 14. November 2026. Offline-Fassung; **nicht installiert oder im Spiel gestartet**. Lokal installiert bleibt 0.3.14. Keine Spieleinstellungen oder Pedalausgabe umgestellt.

## Fehlende Bremsreaktion sichtbar machen

Eine neue Diagnose vergleicht frische tatsächliche ACC-Ausgaben mit dem vom Spiel gemeldeten Bremseingriff. Bei mindestens 20 % Bremsanforderung, mindestens 2 m/s und einer Sekunde durchgehend fehlender Spielreaktion erscheint eine Warnung. Doppelte, pausierte, ungültige oder veraltete Proben begründen keine durchgehende Fehlreaktion. Ein bestätigter Bremseingriff setzt das Prüfintervall zurück. Die Warnung wird pro Assistenzsitzung einmal ausgegeben, im Status und der Oberfläche gehalten und bei manueller Übernahme zurückgesetzt.

Sie fordert die Rückkehr ins Spiel und manuelle Übernahme. Sie schaltet keine noch funktionierende Lenkung ab und sendet keinen zusätzlichen vermeintlichen Rettungsbefehl über denselben unwirksamen Pedalweg. Die Diagnose benötigt weder Routenberechnung noch deren Sperre. Das ist eine Erkennung fehlender Eingabeübernahme, kein physischer Bremswirkungsnachweis, kein Fokusdetektor und keine Behebung der Hintergrundfahrt.

Die Wiederholung der 12.000 vorhandenen Hochfrequenzproben erkennt den Beginn des bekannten Hintergrundausfalls nach 1,015 Sekunden: bei Aufnahmezeit 209,529 s, Bremsanforderung 52,6 %, Spielbremse 0 und etwa 67,8 km/h. Dabei ist das Befehlsalter mangels ursprünglichem Legacy-Erzeugerzeitstempel mit null angenommen; Assistenzstatus und Ausgang sind ebenfalls nicht atomar aufgezeichnet. Das Ergebnis belegt bedingt das Signalmuster, keine vollständige Gegenprobe des Laufzeitverhaltens. Der Laufzeitprüfer verlangt dagegen Befehle mit höchstens 150 ms Alter.

Reproduzierbar mit einer passenden privaten Aufnahme:

```powershell
dotnet run --project CoreTests -c Release -- --brake-replay path/to/capture.jsonl --assume-fresh-legacy-command
```

## Fahrzeugmaße aus offizieller TruckersMP-Schnittstelle

Ein separates lesendes Spielmodul und der zugehörige Statusleser sind gebaut. Sie verwenden die [öffentlichen Fahrzeug-/Aufliegerabmessungen von TruckersMP](https://github.com/TruckersMP/Docs/blob/master/client-sdk/modules/vehicles-and-trailers.md). Das soll die bisher fehlenden tatsächlichen Modellmaße und den Bezugspunkt für spätere Maut-Zahlhaltepositionen liefern. Bauanleitung und Protokoll: [Native/TruckersMPGeometry/README.md](Native/TruckersMPGeometry/README.md).

Das Modul wird mit einem simulierten Host und einem tatsächlich in C erzeugten, anschließend in C# gelesenen Messrahmen geprüft. Diese Prüfung ersetzt keinen echten TruckersMP-Start. Fehlende oder widersprüchliche Messungen bleiben unbekannt. Modellgrenzen sind keine zugesicherte Ladungs- oder Kollisionshülle. Deshalb erteilt die Messung keine automatische Spurfreigabe, verkleinert keinen Sicherheitsabstand und verändert weder Route noch Pedale. Der Leser läuft außerhalb des Fahrzyklus; ein fehlendes Messmodul löst keine Fehlerbremsung aus.

## Weiterhin offen

- **Hintergrundfahrt:** Der aktuelle offizielle ETS2LA-Controller ist ein neu geschriebener SCS-Eingabegeräteweg, aber kein nachgewiesener TruckersMP-Fokusfix. Ein Austausch ohne Nachweis würde die Luftdruck-/Ausgabevergleiche entwerten. Standard-Pedale bleiben eingestellt. Der nur für Einzelspieler vorgesehene Focus Keeper bleibt unbenutzt.
- **Baustellen:** Die geprüfte öffentliche Fahrzeug-/Eingabeschnittstelle liefert keine allgemeine Hindernisabfrage für temporäre Absperrungen. Eigene Fahrzeugmaße ersetzen diese Wahrnehmung nicht. Keine erfundene freie Fahrbahn und keine behauptete Unfallverhütung.
- **Manuelle Zahlpunkte:** Bis Modellhülle, SDK-Ursprung und tatsächliche Zahlzone im Spiel abgeglichen sind, bleibt der bisherige Sicherheitsabstand bestehen. Für die zwei griechischen AutoPASS-Wiederholungen ist kein neuer manueller Zahlpunkt nachgewiesen.
- **Länderabdeckung und Fahrprüfung:** Vollständige Mautdurchfahrten mit Halt, Öffnung und stabiler Ausfahrt sind weiterhin erforderlich. Heute wird dafür kein Start angefordert.

## Prüfstand

89 Integrations- und 49 unabhängige Logikprüfungen bestehen, jeweils einschließlich der optionalen Prüfung des tatsächlich in C erzeugten Protokollrahmens. Ohne diesen lokalen Rahmen sind es 88 beziehungsweise 48. Zusätzlich bestehen 18 native Host-Prüfungen, 44 Aufnahmeprüfungen und drei Inventurprüfungen. Die neue Integration hält die Routensperre absichtlich fest und bestätigt: Die Bremswarnung wird trotzdem erkannt; Assistenzzustand und veröffentlichte Steuerausgaben werden dadurch nicht verändert. F9 löscht den Warnzustand. Der tatsächliche Speicherleser erstellt keine Quelle und lehnt unbestätigte Prozesse, halbfertige Schreibvorgänge und entladene Messmodule ab.

Ein von einem Leser noch offen gehaltener Messspeicher verhindert einen späteren Spielneustart nicht dauerhaft: Unter exklusiver Schreibersperre darf das neue Modul nur einen bekannten, sauber invalidierten oder seit über einer Sekunde veralteten Protokollrahmen übernehmen. Frische Daten einer fremden Quelle werden weder gelöscht noch übernommen. Der Leser schließt ungültige Quellen und kann sie später neu öffnen. Nach dem Ausschalten des Plugins bleibt er dagegen bis zum ausdrücklichen erneuten Einschalten geschlossen.

Die neue Windows-Prüfung auf GitHub baut den nativen Beobachter gegen festgelegte offizielle Header, führt den simulierten Host aus und übergibt dessen Binärrahmen an die unabhängigen C#-Tests. Ein Spiel oder fremde native ETS2LA-Plugins werden dort nicht gestartet. Die vollständige ETS2LA-Integration bleibt eine lokale Prüfung gegen die bereits dokumentierte Installation.
