# Lokale Ausgabekorrektur: Entwurf 0.3.17

Das Werkzeug bereitet separate lokale Kopien vier exakt geprüfter ETS2LA-Komponenten vor. Es installiert nichts. Herstellerbibliotheken und daraus vorbereitete Dateien gehören nicht ins öffentliche Repository.

Es korrigiert die ausgelassene native ACC-Pedalveröffentlichung bei sehr langsamer Fahrt, neutralisiert abgelaufene Achsen trotz weiterlaufender Lenkung und verwirft verspätete Ergebnisse früherer Assistenzsitzungen. Der Pause-Helfer behält den Ursprung seiner Sitzung.

## Bauen und prüfen

Voraussetzung: .NET 10, die lokale ETS2LA-Kombination mit den im Werkzeug festgelegten Originalprüfsummen und die üblichen Projektverweise unter References. Mono.Cecil ist auf 0.11.6 festgelegt; alternativ kann CecilRoot auf die Bibliotheken des lokalen .NET-ILLink-Werkzeugs zeigen.

```powershell
dotnet build Tools/HostCompatibility -c Release -p:EtsRoot='D:/ETS2LA/current'
./Tools/HostCompatibility/bin/Release/net10.0/HostCompatibility.exe prepare 'D:/ETS2LA/current' 'C:/separate-neue-vorbereitung'
./Tools/HostCompatibility/bin/Release/net10.0/HostCompatibility.exe verify 'D:/ETS2LA/current' 'C:/separate-neue-vorbereitung'
```

prepare verweigert vorhandene Ausgabeordner, Ziele innerhalb der Installation und unbekannte Originaldateien. verify prüft zuerst alle vier Ziel-/Quellprüfsummen, Modulkennungen und den gemeinsamen Revisionsmarker. Anschließend laufen native Gegenproben in nochmals separaten instrumentierten Kopien. Speicheröffnungs- und Ausgabewege sind dort gesperrt. Schreibziele sind ausschließlich Testwörterbücher. Einige Constraint-Eingänge werden injiziert; das ist kein Fahr- oder Wahrnehmungsnachweis.

Jede Vorbereitung bekommt neue Modulkennungen. Vor dem Plugin-Build ist compatibility.json aus genau dieser Vorbereitung als Plugin/HostCompatibility.json zu übernehmen. Das Plugin prüft das eingebettete Manifest gegen die tatsächlich geladenen vier Komponenten. Nur MautAssistent.dll auszutauschen reicht für diesen Entwurf nicht.

## Rollen

Der Rollmodus veröffentlicht bei belegter Freigabe tatsächlich Gas 0 und Bremse 0. Ein Beschleunigungsziel von null würde wegen Vorsteuerung und Reglerintegral nicht genügen.

Erforderlich sind eine kleine Überschreitung des Wunschtempos bis 6 km/h, mindestens 50 km/h, Full Self-Driving, ein vollständig abgedeckter gerichteter Korridor ohne langsamere Kurve, frische Spiel-/Verkehrsdaten und gültige eigene native Routeneingänge. Keine Maut-, Tracking-, Recovery- oder Fehlerbindung darf aktiv sein. Der native Pfad und die verwendeten Datencontainer müssen den aktuell bekannten Instanzen entsprechen. Leere Standardcontainer und unbekannte NaN-Gründe genügen nicht. Der regelmäßige erfolgreiche SDK-Reconnect behält nur den unveränderten letzten Snapshot innerhalb seiner 0,2-s-Frist; Fehler und fehlgeschlagenes Wiederöffnen löschen den Nachweis sofort.

Andere negative Fahrzeug-, Kurven-, Semaphoren- und Mautbegrenzungen bleiben vorrangig. Gefälle beziehungsweise zunehmende Geschwindigkeit verriegeln das Rollen bis zum Zieltempo oder einem neuen Ziel/einer neuen Sitzung. Die Verriegelung überlebt einen Threadwechsel. Die Oberfläche kann den Rollmodus abschalten.

Die SDK-Container haben keinen Produzentenzeitstempel. Erfolgreiche aktuelle Lesevorgänge plus fortschreitende Spieltelemetrie sind ein begrenzter Nachweis, keine vollständige unabhängige Verkehrswahrnehmung. Fehlende Nachweise belassen die native Regelung. Reale Autobahnfahrt mit dem neuen Paket steht aus. Das Fokusproblem der Standard-Pedalausgabe unter TruckersMP wird hiermit nicht behoben.

Vor einem späteren Einbau muss ETS2LA geschlossen sein. Alle vier Originalkomponenten und das bisherige Plugin sind zusammen zu sichern. Wiederherstellung ebenfalls nur bei geschlossenem ETS2LA. Der vorbereitete Entwurf wird nicht automatisch installiert.
