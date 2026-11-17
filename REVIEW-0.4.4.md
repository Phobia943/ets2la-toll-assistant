# 0.4.4 – gebundene Zahlungsbestätigung und Eingabediagnose

Die neue Fassung ergänzt eine einmalige automatische Spielinteraktion an belegten manuellen Mautspuren. Hintergrundpedale unter TruckersMP bleiben offen. Codeprüfung, Paketprüfung und eine tatsächlich ohne Eingriff durchfahrene Mautstelle sind getrennte Nachweise.

## Manuelle Mautspuren

Automatische Abrechnung bleibt die bevorzugte Spurwahl. Bei einer manuellen Spur erhält der Helfer jetzt auch das gerichtete Zahlrechteck aus dem bestätigten Spielmodell. Gate-ID, kanonische Route, Platzierung, Geometrie und Revision bleiben gebunden. Eine reine Längsdistanz genügt nicht; Seitenabstand, Höhe, Fahrtrichtung, frische Spiel-/Schrankendaten und Stillstand müssen ebenfalls passen.

Der bestehende Sicherheitsabstand wird nicht verringert. Liegt die Modellzone näher als der sichere Haltepunkt, fehlen unterstützte Triggerdaten oder ist die Zuordnung mehrdeutig, bleibt die Zahlung manuell. Die Position des Lkw-Messpunkts innerhalb des Modellrechtecks ist eine ausdrücklich zu prüfende Annahme über den Aktivierungsreferenzpunkt des Spiels; erst die reale Zahlung bestätigt die Wirkung an einem konkreten Modell.

Nach mindestens 0,4 Sekunden bestätigtem Stillstand und einer dauerhaft gespeicherten Versuchsreservierung darf genau ein semantischer `activate`-Impuls angefordert werden. Dies ist keine Windows-Taste und kein Enter an den Chat. Die Spielinstallation und ihr lebender Prozess müssen eindeutig geprüft sein, das Spiel vorne und ungepaust bleiben. Eine separate Aussage „TruckersMP-Chat geschlossen“ wird nicht erfunden.

Der Host prüft Versuch, Fahr-/Quellenepoche, ursprüngliche Fristen und den aktuellen Kontext erneut direkt vor der Ausgabe. Die Host-Ausgabeschleife begrenzt den Impuls auf eine Frist von 100 ms ab seiner ursprünglichen Anforderung und setzt den Eingang beim ersten folgenden Ausgabeframe zurück. Das Legacy-SDK hat dafür keine eigenständige Ablaufzeit oder Empfangsbestätigung: Bei einem abgestürzten oder vollständig angehaltenen Host ist diese Rücksetzung nicht zugesichert. Generische Toggle-Aufgaben und Reset-Schleifen dürfen diesen Eingang nicht erneut setzen. F9, Controllerbremse, Fokusverlust, neue Route, Abdriften, Quellenverlust und eine nicht mehr geschlossene eigene Schranke entziehen den Impuls. Verzögerte Verarbeitung verlängert seine Frist nicht.

Eine Ausgabe ist kein Zahlungsbeleg. Erst die frisch beobachtete eigene Schranke und die unveränderte Öffnungswartezeit erlauben die weitere Fahrt. Ein Nachbartor oder ein unspezifisches Zahlungsereignis genügt nicht. Bei ausbleibender Öffnung gibt es keinen blinden Wiederholungsversuch und keine zusätzliche Notbremsung allein wegen der Zahlung; der normale Schrankenhalt bleibt bestehen.

## Speichern ohne Fahrblockade

Ein begrenzter eigener Schreiber speichert Versuche vor jeder Interaktion. Weder der Fahrzyklus noch der Ausgabepfad wartet auf Dateizugriff oder `Flush`. Eine verspätete Speicherbestätigung ist nur ein Datensatz mit Anforderungskennung; sie aktiviert selbst nichts. Abgebrochene Anforderungen bleiben widerrufen. Konkurrierende Schreiber lesen den neuesten Stand unter einer Dateitransaktionssperre, bevor sie atomar ersetzen. Beschädigte Daten oder eine volle Warteschlange ergeben keine Interaktionsfreigabe.

F9/F8 und ein Neustart am selben Tor löschen keinen gespeicherten Versuch. Ein späterer Besuch darf erst nach zwei Sekunden fortlaufender frischer Beobachtung mehr als 100 m außerhalb dieser Zahlstelle freigegeben werden; die bedingte Entfernung gehört ebenfalls zum Hintergrundschreiber. Datenlücken, Prozesswechsel und Positionssprünge beginnen diese Beobachtung neu. Das ist kein Nachweis eines durchgehend gefahrenen Wegs vom Tor: Nach Bergung oder Teleport kann die erneute Beobachtung außerhalb ebenfalls einen späteren Besuch vorbereiten.

Native Routenproben bleiben außerhalb des Fahrlocks. Ankommende und tatsächlich angewendete Routen werden getrennt gezählt; jede tatsächliche Pfadänderung entzieht zuerst die alte Zahlungsfreigabe. Ein verspäteter alter Pfad kann sich daher nicht mit der Kennung einer neueren Route ausweisen. Beim Pluginwechsel entfernt ein atomarer Vergleich nur den eigenen registrierten Zahlungskontext, niemals den einer neueren Instanz.

## Eingabekontext

Die zusätzliche Diagnose prüft den exakten vom Host erkannten Spielpfad und hält ein nur lesendes Handle auf den ursprünglichen Prozess. Prozesssuche und Pfadabfragen laufen getrennt vom Fahrzyklus. Der schnelle Ausgabepfad liest nur den aktuellen Vordergrundprozess und die Lebensdauer der bereits bestätigten Instanz. PID-Wiederverwendung, Prozessende, verspätete Abfragen und ein alter Hintergrundworker erzeugen keine neue Freigabe.

Aufzeichnungen unterscheiden nun ursprünglichen Befehl, späteren logischen Spieleingang, effektive Pedalwirkung, Kupplung/Gang/Drehzahl und Fensterkontext. Virtuelle Spieleingaben werden nicht als physische Controllerachsen bezeichnet. Ein Fokuswechsel plus fehlender Empfang belegt eine Korrelation, nicht automatisch die Ursache.

Die erneut geprüfte offizielle SCS-Input-API bietet keinen Schalter für garantierte Hintergrundaktivität. Auch die Rückmeldung des neueren ETS2LA-Controllers bestätigt zunächst nur die Abholung durch dessen Callback, nicht die tatsächliche Pedalwirkung. Daher wurde weder der Einzelspieler-Fokusworkaround eingebaut noch die bestehende Pedalausgabe blind umgestellt. [Details und Primärquellen](BACKGROUND_INPUT.md).

## Prüfung und verbleibende Arbeit

Die Endprüfung besteht: 217 Integrations-Testgruppen, 99 unabhängige Logikgruppen, 299 isolierte Host-/Produktassertionen gegen die versiegelte DLL, 70 Aufnahmeprüfungen und drei Inventurtests. Diese Ebenen überschneiden sich; ihre Summe ist keine Anzahl unabhängig geprüfter Fahrfälle. Gehaltene Dateischreiber, verspätete Speicherbestätigungen, vertauschte echte Routenrückrufe und blockierte native Proben sind gezielt enthalten. Die Hostprüfung führt den echten Regler und die veränderte Ausgabekette gegen isolierte Senken aus, ohne Live-Speicher oder Spielausgabe zu öffnen.

Der kritische Review fand und bestätigte unter anderem die Korrektur der Markerrotation, der Routen-Generationsübergabe, einer versehentlich ausgedehnten Routensperre und des nicht atomaren Callback-Abbaus. Anschließend wurden der erzeugte Installer und seine rein lesende Paketprüfung gegengeprüft.

0.4.4 wurde am 17. November bei geschlossenen Anwendungen installiert. Alle fünf neuen Dateien und alle fünf Sicherungen wurden anschließend unabhängig per SHA256 geprüft. Spiel-SDK und Fahrzeugmessmodul bleiben unverändert; keine Anwendung wurde gestartet und keine Pedaleinstellung geändert. Pluginversion `0.4.4.0`, SHA256 `D202F22E466C645060DF55FFE75B0C8C520122D22333093473E01946ED6F1E82`, MVID `8f60ca39-22e0-4548-bd71-25ec5b2913e9`. Das zusammengehörige Hostmanifest ist [im Repository](Plugin/HostCompatibility.json) festgehalten; seine vorbereitete Paketkennzeichnung ist keine Laufzeitmeldung. Private Installations- und Prüfnachweise bleiben außerhalb des Repositorys.

Erforderliche Liveprüfungen: Start/Parken, F9 und Controllerbremse, eigene Geschwindigkeitsregelung, manuelle Zahlstelle samt Öffnung und Ausfahrt, sowie getrennte Messung von Gas und Bremse im Vorder-/Hintergrund. Allgemeine Baustellen-/Hinderniserkennung und sämtliche Mautmodelle aller Länder sind dadurch nicht als gelöst nachgewiesen.
