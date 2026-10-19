# Spezifikation Formwerk (3D-Modellierer für den 3D-Druck)

Oct 18, 2026 · @Big Beans

## 1. Zweck und Leitprinzipien

Formwerk (Arbeitstitel) ist eine Windows-Desktopanwendung, mit der Einsteiger druckbare 3D-Modelle aus Grundformen zusammenbauen, bearbeiten und als 3MF/STL exportieren. Funktional liegt sie oberhalb von Microsoft 3D Builder und unterhalb von Fusion 360 oder AutoCAD; in der Bedienung soll sie so zugänglich sein wie Tinkercad, aber offline, schneller und mit echter, nachträglich änderbarer Historie.

Diese Spezifikation ist für eine KI-gestützte Entwicklung geschrieben: Jede Anforderung hat eine ID, ein Akzeptanzkriterium und eine Phase. Ein Coding-Agent soll Anforderungen einzeln umsetzen, testen und abhaken können.

**Leitprinzipien (gelten für jede Designentscheidung):**

1. **Direkt im Raum arbeiten.** Alles passiert im 3D-Viewport mit Griffen (Gizmos). Dialoge nur für exakte Zahlen.
2. **Ziehen oder tippen.** Jeder Wert, der per Maus gezogen werden kann, kann auch als Zahl eingegeben werden, und umgekehrt.
3. **Nichts ist endgültig, außer der Nutzer will es.** Boolesche Operationen, Gruppen, Fasen und Bohrungen bleiben bearbeitbar, bis explizit "Abflachen" gewählt wird.
4. **Druckbarkeit zuerst.** Millimeter als Einheit, Druckbett sichtbar, Warnungen bei nicht druckbarer Geometrie.
5. **Sinnvolle Standardwerte.** Snapping an, 45°-Rasterung an, Objekt landet auf dem Boden. Profis können abschalten, Einsteiger müssen nichts einstellen.
6. **Läuft ohne Installation von Laufzeiten.** Eine entpackte Ordnerkopie oder ein Installer reicht.

## 2. Zielgruppe, Scope und Nicht-Ziele

**Zielgruppe:** Hobby-Maker mit FDM- oder Resin-Drucker, die Halterungen, Gehäuse, Ersatzteile, Organizer und einfache Funktionsteile bauen wollen. Keine CAD-Vorkenntnisse vorausgesetzt.

**Im Scope:**

- Modellieren aus Grundformen und Bibliotheksformen (Constructive Solid Geometry, CSG)
- Bearbeiten importierter Meshes (STL, OBJ, 3MF), inklusive Reparatur
- Einfache, visuelle Parametrik (Maße, Variablen, Muster)
- Fasen, Rundungen, Bohrungen, Senkungen, Muster
- Export für Slicer

**Nicht-Ziele (bewusst ausgeschlossen, damit der Agent nicht ausufert):**

- Vollwertige 2D-Skizzen mit geometrischen Constraints wie in Fusion 360 (nur ein vereinfachter Skizzenmodus, siehe Abschnitt 5)
- Baugruppen mit Gelenken, Kinematik, Simulation (FEM)
- Technische Zeichnungen und Bemaßungsnormen
- Freiform-Sculpting, Organic Modeling
- Slicing und G-Code-Erzeugung (das macht der Slicer)
- Cloud-Konten, Online-Zwang, Telemetrie
- macOS/Linux in Version 1 (Architektur soll es aber nicht verbauen)

## 3. Technologie und Architektur

Empfehlung: C# auf .NET 10 (LTS), ausgeliefert als **self-contained** Build für win-x64. Die .NET-Laufzeit steckt dann im Programmordner, der Nutzer installiert nichts. Native AOT ist mit WPF nicht möglich und wird nicht verfolgt; self-contained reicht für das Ziel "keine Laufzeit installieren".

Die kritischste Entscheidung ist der Geometriekern. Er wird hinter einer Schnittstelle `IGeometryKernel` gekapselt, damit er austauschbar bleibt.

| Baustein | Empfehlung | Lizenz | Begründung | Vor Start prüfen |
| --- | --- | --- | --- | --- |
| Sprache/Runtime | C# 14, .NET 10 LTS, self-contained | MIT | Keine Laufzeitinstallation, guter KI-Support | nein |
| UI-Framework | WPF mit Fluent-Theme (Dark Mode) | MIT | Reif, Windows-nativ, viele Beispiele für Agenten | entschieden (D-02); Viewport per D3DImage einbetten, nicht per HwndHost |
| 3D-Viewport | Eigene Render-Schicht hinter `IRenderer` auf DirectX 11 über Vortice.Windows, alternativ Helix Toolkit (vorerst zurückgestellt: WPF-3D, siehe D-03) | MIT | Volle Kontrolle über Gizmos, Kantenhervorhebung, Ghost-Darstellung | Wartungsstatus |
| GPU-Compute | ComputeSharp oder DirectCompute über Vortice, immer mit CPU-Fallback (zurückgestellt, siehe D-03) | MIT | Volumetrische Operationen und Analysen beschleunigen (siehe N-GPU) | Nutzen per Benchmark belegen |
| Geometriekern | Manifold (C-API per P/Invoke), mehrkernig | Apache 2.0 | Robuste, schnelle Booleans auf Meshes, garantiert wasserdichte Ergebnisse | C-API-Umfang |
| B-Rep-Modul (optional) | OpenCascade (OCCT) über eigene C-Wrapper-DLL, als separate DLLs ausgeliefert | LGPL 2.1 mit Ausnahme, Auflagen siehe N-LIC-04 | Fasen/Rundungen an beliebigen Kanten (F-EDG-03) | Build-Aufwand, DLL-Größe |
| Mesh-Reparatur | Eigene Pipeline + geometry3Sharp; optional Windows-API `Printing3DModel.RepairAsync` (die Reparatur, die 3D Builder nutzt) | Boost; Windows-API ist Systembestandteil | Offline, steuerbar; Windows-API als zweite Engine | Verfügbarkeit der API unter Windows 11 |
| 2D-Geometrie (Skizzen, Offsets) | Clipper2 | Boost | Robuste Polygon-Booleans und Offsets für den Skizzenmodus | nein |
| Dateiformate | STL/OBJ selbst, 3MF über lib3mf oder selbst | BSD-2 | 3MF ist Standard für Farben und Mehrteiler | nein |
| MVVM | CommunityToolkit.Mvvm | MIT | Standard für WPF | nein |
| Tests (nicht ausgeliefert) | xUnit, Verify, FsCheck, FlaUI | Apache 2.0, MIT, BSD-3, MIT | Geometrie braucht Eigenschaftstests | nein |

**Lizenzziel: Ein späterer Verkauf darf nie blockiert sein.** Bevorzugt werden permissive Komponenten (MIT, BSD, Apache 2.0, Boost, zlib); sie erlauben Verkauf ohne Offenlegung des eigenen Codes, Pflicht ist nur das Mitliefern der Lizenztexte. LGPL ist erlaubt, wenn die Komponente als separate DLL eingebunden wird (OpenCascade); die Auflagen stehen in N-LIC-04. GPL, AGPL und Non-Commercial-Lizenzen sind ausgeschlossen. Die Lizenzangaben sind Stand der Recherche und vor dem Einbinden zu verifizieren; das ist keine Rechtsberatung.

**Modulschnitt (je Modul ein Projekt in der Solution):**

| Projekt | Verantwortung | Darf abhängen von |
| --- | --- | --- |
| `Formwerk.Core` | Dokumentmodell, Szenengraph, Feature-Baum, Commands, Undo/Redo, Parameter | nichts |
| `Formwerk.Geometry` | `IGeometryKernel`, Mesh-Typen, Kernel-Adapter (Manifold, später OCCT) | Core |
| `Formwerk.Features` | Generatoren für Grundformen, Bohrungen, Muster, Fasen, Extrusion | Core, Geometry |
| `Formwerk.Repair` | Analyse und Reparatur von Meshes | Geometry |
| `Formwerk.IO` | Import/Export STL, OBJ, 3MF, Projektformat | Core, Geometry |
| `Formwerk.Library` | Formbibliothek, Vorschaubilder, Metadaten | Core, Features |
| `Formwerk.Viewport` | Rendering, Kamera, Picking, Gizmos, Snapping-Anzeige | Core, Geometry |
| `Formwerk.App` | WPF-Oberfläche, Panels, Themes, Tastenkürzel | alle |
| `Formwerk.Tests.*` | Tests je Modul | jeweiliges Modul |

Regel für den Agenten: `Core` kennt weder UI noch Kernel. Die Geometrieauswertung läuft im Hintergrund-Thread, die UI zeigt bis zum Ende das letzte gültige Ergebnis. Jede Änderung am Dokument läuft über ein Command-Objekt (Voraussetzung für Undo).

## 4. Funktionale Anforderungen

Jede Anforderung hat eine ID (für Commits, Tests und Rückfragen), ein prüfbares Akzeptanzkriterium (AK) und eine Phase (P1 bis P4, siehe Abschnitt 9). Einheit ist überall Millimeter, Winkel in Grad.

### 4.1 Import und Formbibliothek

| ID | Anforderung | Akzeptanzkriterium | Phase |
| --- | --- | --- | --- |
| F-IMP-01 | Import von STL (binär/ASCII), OBJ, 3MF per Dialog und Drag-and-drop | STL mit 1 Mio. Dreiecken in unter 3 s geladen und angezeigt | P1 |
| F-IMP-02 | Einheitenprüfung beim Import | Ist das Objekt kleiner als 1 mm oder größer als 1000 mm, fragt ein Dialog nach mm/cm/inch | P1 |
| F-IMP-03 | Automatische Analyse nach Import | Bei Fehlern erscheint ein Hinweisbanner mit Anzahl der Fehler und Knopf "Reparieren" | P2 |
| F-LIB-01 | Parametrische Grundformen: Quader, Zylinder, Kugel, Halbkugel, Kegel, Kegelstumpf, Pyramide, n-Eck-Prisma, Keil, Torus, Rohr, Kapsel | Jede Form hat ein Eigenschaftenpanel mit ihren Maßen; Ändern eines Maßes aktualisiert die Form in unter 100 ms | P1 |
| F-LIB-02 | Formbibliothek mit Kategorien (Grundformen, Verbindungselemente, Halterungen, Gehäuse, Mechanik, My Templates), Suche, Vorschaubildern, Favoriten | Suche nach "Schraube" findet alle Elemente mit diesem Schlagwort; mindestens 100 Elemente bis Ende P3 | P2 |
| F-LIB-03 | Eigene Bibliothekselemente als Templates (Details in 4.13) | Gespeichertes Element erscheint in "Meine Formen" und lässt sich mit seinen Parametern wieder einfügen | P2 |
| F-LIB-04 | Einfügen per Klick (Bettmitte, auf dem Boden) oder per Ziehen (an die Mausposition auf der getroffenen Fläche, ausgerichtet an deren Normale) | Gezogener Zylinder steht senkrecht auf einer schrägen Fläche, auf die er gezogen wurde | P2 |

### 4.2 Auswahl, Verschieben, Drehen

| ID | Anforderung | Akzeptanzkriterium | Phase |
| --- | --- | --- | --- |
| F-SEL-01 | Auswahl per Klick, Mehrfachauswahl mit Strg/Umschalt, Rechteckauswahl, Auswahl in der Objektliste | Auswahl ist zwischen Viewport und Objektliste immer synchron | P1 |
| F-TRF-01 | Verschieben-Gizmo mit 3 Achsenpfeilen und 3 Ebenengriffen, dazu freies Ziehen auf der Arbeitsebene | Ziehen am X-Pfeil ändert nur X | P1 |
| F-TRF-02 | Zahleneingabe für Position: absolut, relativ ("+10") und als Ausdruck ("20/2+3") | Eingabe "+10" verschiebt um genau 10 mm | P1 |
| F-ROT-01 | Drehen-Gizmo mit drei Ringen und Live-Winkelanzeige | Winkel wird während des Drehens am Cursor angezeigt | P1 |
| F-ROT-02 | Winkel-Snap: Standardschritt 15°, wählbar 1/5/15/45/90°; Umschalt hält Snap temporär aus | Mit Schritt 45° sind nur Vielfache von 45° erreichbar | P1 |
| F-ROT-03 | Drehpunkt wählbar: Objektmitte, Ecke des Begrenzungsrahmens, beliebiger Snap-Punkt, Weltursprung | Drehung um einen Eckpunkt lässt diesen Punkt unverändert | P2 |
| F-ROT-04 | Schnellaktionen: 90° um X/Y/Z; "Fläche auf Boden legen" (Fläche anklicken, Objekt dreht sich so, dass sie unten liegt) | Angeklickte Fläche liegt danach exakt auf Z = 0 | P1 |

### 4.3 Snapping und Ausrichten

| ID | Anforderung | Akzeptanzkriterium | Phase |
| --- | --- | --- | --- |
| F-SNP-01 | Snap-Punkte je Objekt: Ecken, Kantenmitten, Flächenmitten, Mittelpunkt des Begrenzungsrahmens, Kreismittelpunkte (Zylinder, Bohrungen) | Beim Überfahren werden Snap-Punkte als Marker angezeigt | P1 |
| F-SNP-02 | Beim Verschieben rastet das Objekt an Snap-Punkten, Kanten und Flächen anderer Objekte ein; Hilfslinien zeigen bündig/mittig (Prinzip PowerPoint-Hilfslinien) | Zwei Quader lassen sich ohne Zahleneingabe exakt mittig aufeinander setzen | P1 |
| F-SNP-03 | Fläche an Fläche: Objekt an einer Fläche greifen und an die Fläche eines anderen Objekts ziehen; Flächen liegen danach bündig, optional mit Spalt oder Überlappung | Spalt von 0,2 mm lässt sich als Option setzen und wird exakt eingehalten | P2 |
| F-SNP-04 | Ausrichten-Werkzeug für Mehrfachauswahl: je Achse min/mitte/max, gleichmäßig verteilen; Referenz = erstes gewähltes Objekt oder Auswahlrahmen | Drei Objekte werden in X gleichmäßig verteilt, äußere bleiben stehen | P2 |
| F-SNP-05 | Raster-Snap: 0,1/0,5/1/5/10 mm; Alt hält Snap temporär aus | Mit Raster 5 mm liegen Positionen auf Vielfachen von 5 | P1 |
| F-SNP-06 | Snap-Toleranz in Bildschirmpixeln (Standard 10 px), Prioritäten: Punkt vor Kante vor Fläche vor Raster | Toleranz ist zoomunabhängig gleich groß | P1 |

### 4.4 Skalieren

| ID | Anforderung | Akzeptanzkriterium | Phase |
| --- | --- | --- | --- |
| F-SCL-01 | Skalieren-Gizmo mit Griff je Achsenseite und Eckgriff für proportional | Eckgriff skaliert immer proportional | P1 |
| F-SCL-02 | Umschalter "verknüpft" (alle Achsen proportional) und "einzeln" (Schloss-Symbol) | Bei "verknüpft" ändert eine Maßeingabe alle drei Maße im gleichen Verhältnis | P1 |
| F-SCL-03 | Bezugspunkt je Achse: von einer Seite (gegenüberliegende Seite bleibt fix) oder von der Mitte; Standard beim Ziehen: gegenüberliegende Seite fix, Strg = von der Mitte | Bei "von links" bleibt die linke Kante beim Skalieren in X stehen | P1 |
| F-SCL-04 | Eingabe in mm (absolute Maße) oder Prozent | Eingabe "150%" ergibt das 1,5-fache Maß | P1 |
| F-SCL-05 | Parametrische Objekte ändern beim Skalieren ihre Parameter statt verzerrt zu werden; Option "Bohrungen und Fasen beim Skalieren beibehalten" | Zylinder mit M3-Bohrung, auf 200% skaliert: Bohrung bleibt 3,4 mm bei aktivierter Option | P2 |
| F-SCL-06 | Live-Maßanzeige B × T × H während des Ziehens | Anzeige folgt dem Cursor und zeigt mm mit einer Nachkommastelle | P1 |

### 4.5 Visuelle Parametrik

Ziel: Parametrik ohne Programmieren. Der Nutzer sieht Maße am Objekt, klickt darauf und tippt. Variablen sind optional und erst sichtbar, wenn man sie braucht.

| ID | Anforderung | Akzeptanzkriterium | Phase |
| --- | --- | --- | --- |
| F-PAR-01 | Eigenschaftenpanel zeigt die Parameter des gewählten Objekts (z. B. Zylinder: Durchmesser, Höhe, Segmente, Fase oben, Fase unten) | Jede Grundform zeigt ausschließlich ihre eigenen Parameter | P1 |
| F-PAR-02 | Maße direkt im Viewport: Maßpfeile am Objekt anklicken und Wert tippen (Prinzip Tinkercad) | Klick auf das Höhenmaß öffnet ein Eingabefeld direkt am Objekt | P1 |
| F-PAR-03 | Projektvariablen in einer Parametertabelle (z. B. `Wandstaerke = 2`, `Schraube = 3.4`); jedes Zahlenfeld akzeptiert Variable oder Ausdruck; verknüpfte Felder sind farbig markiert | Ändern von `Wandstaerke` aktualisiert alle verknüpften Objekte in einem Rechenlauf | P2 |
| F-PAR-04 | Feste Abstände: Maß zwischen zwei Snap-Punkten aufziehen und per Schloss fixieren; bewegt sich das Bezugsobjekt, folgt das abhängige | Bohrung mit fixiertem Abstand 5 mm zur Kante bleibt bei Größenänderung 5 mm von der Kante entfernt | P3 |
| F-PAR-05 | Formvarianten: Bibliothekselemente mit Parametern (z. B. "Box mit Deckel": Innenmaße, Wandstärke, Spiel) | Box mit Innenmaß 50 × 30 × 20 und Wand 2 ergibt Außenmaß 54 × 34 × 22 | P2 |

### 4.6 Kanten, Bohrungen, Senkungen

Fasen und Rundungen kommen in drei Stufen. Stufe 1 und 2 laufen auf dem Mesh-Kern (Manifold). Stufe 3 braucht exakte Flächen und Kanten (B-Rep) und kommt über ein optionales OpenCascade-Modul, das als separate DLL neben dem Programm liegt (siehe D-01).

| ID | Anforderung | Akzeptanzkriterium | Phase |
| --- | --- | --- | --- |
| F-EDG-01 | Stufe 1: Fase oder Rundung als Formparameter von Grundformen: alle Kanten, nur obere, nur untere; Maß in mm | Quader 20 × 20 × 10 mit Rundung 2 mm hat an allen 12 Kanten r = 2 | P2 |
| F-EDG-02 | Stufe 2: Fase oder Rundung an ausgewählten Kanten per Negativkörper: gerade Kanten zwischen zwei ebenen Flächen (Keil bzw. Keil minus Zylinder) und Kreiskanten zylindrischer Flächen (ringförmiger Negativkörper); der Negativkörper bleibt als Lochobjekt im Baum und ist änderbar | Obere Kante einer Box nach einer Differenz anklicken, Rundung 1 mm setzen: Ergebnis wasserdicht, Radius später änderbar; nicht unterstützte Kanten sind ausgegraut mit Hinweis | P3 |
| F-EDG-03 | Stufe 3 (optionales OpenCascade-Modul): Fase oder Rundung an beliebigen Kanten, auch gekrümmt und nach Booleschen Operationen | Rundung an der Durchdringungskante zweier schräg verbundener Zylinder; ohne installiertes Modul ist die Funktion ausgeblendet | P4 |
| F-HOL-01 | Bohrungswerkzeug: Fläche anklicken, Bohrung entsteht senkrecht zur Fläche am Klickpunkt; Snap auf Flächenmitte und Raster | Bohrung auf einer schrägen Fläche steht senkrecht zu dieser Fläche | P2 |
| F-HOL-02 | Bohrungstypen: zylindrisch, konisch (Anfangs-/Enddurchmesser), Kegelsenkung (Senkwinkel 90° Standard), Zylindersenkung/Gegenbohrung; durchgehend oder mit Tiefe | Alle Typen sind als Schnittbild in der Vorschau des Werkzeugs dargestellt | P2 |
| F-HOL-03 | Presets M2 bis M8: Durchgangsloch (fein/mittel/grob), Senkkopf, Zylinderkopf, Kernloch für selbstschneidende Schrauben, Gewindeeinsatz (Einpressmutter), Sechskant-Mutternfalle | Preset "M3 Senkkopf" erzeugt Durchgang und Senkung ohne weitere Eingabe | P2 |
| F-HOL-04 | Globale Drucktoleranz für Bohrungen und Passungen (Standard +0,2 mm, in den Einstellungen änderbar) | Ändern der Toleranz aktualisiert alle Bohrungen mit Preset | P2 |
| F-HOL-05 | Bohrungen sind Negativobjekte (siehe 4.10): jederzeit verschiebbar, änderbar, löschbar | Bohrung nach dem Erstellen per Gizmo verschieben, Körper wird live neu berechnet | P2 |

### 4.7 Kopieren, Muster, Spiegeln

| ID | Anforderung | Akzeptanzkriterium | Phase |
| --- | --- | --- | --- |
| F-CPY-01 | Kopieren, Einfügen, Duplizieren; Strg+D wiederholt den letzten Versatz (Duplikat einer Kopie landet im gleichen Abstand wie die vorige) | Dreimal Strg+D nach einmaligem Verschieben um 15 mm ergibt eine Reihe im Abstand 15 mm | P1 |
| F-PAT-01 | Kreismuster: Objekt oder Bohrung wählen, Anzahl, Winkelbereich, Mittelpunkt (Snap auf Zylinderachse), Radius oder "Abstand vom Außenrand" (gemessen bis Bohrungsmitte, umschaltbar auf Bohrungsrand) | Zylinder Ø 40, 3 Bohrungen, Abstand 5 mm vom Rand: Bohrungen bei 120° Teilung auf Radius 15 mm | P2 |
| F-PAT-02 | Lineares Muster 1D und 2D: Anzahl, Abstand oder Gesamtlänge, gleichmäßig verteilt | 4 × 3 Raster mit Abstand 10 mm erzeugt 12 Instanzen | P2 |
| F-PAT-03 | Muster bleibt Feature: Anzahl, Abstand und Quelle später änderbar; einzelne Instanzen lassen sich überspringen | Anzahl von 3 auf 6 ändern verteilt neu auf 60° Teilung | P2 |
| F-PAT-04 | Spiegeln an Ebene (X/Y/Z, Fläche): als verknüpfte Kopie oder unabhängig | Änderung am Original ändert die verknüpfte Spiegelung mit | P2 |

### 4.8 Mesh-Reparatur

| ID | Anforderung | Akzeptanzkriterium | Phase |
| --- | --- | --- | --- |
| F-REP-01 | Analyse: offene Kanten, nicht-mannigfaltige Kanten und Punkte, falsch orientierte Normalen, Selbstüberschneidungen, degenerierte Dreiecke, doppelte Punkte, lose Teile | Bericht mit Anzahl je Fehlerart; Klick auf eine Fehlerart hebt die Stellen im Viewport hervor | P2 |
| F-REP-02 | Ein-Klick-Reparatur: Punkte verschweißen, Normalen vereinheitlichen, Löcher schließen, Degenerierte entfernen, Selbstüberschneidungen auflösen, lose Kleinteile unter Schwellwert entfernen | Testkorpus (Abschnitt 10) ist nach Reparatur zu mindestens 95% wasserdicht und mannigfaltig | P2 |
| F-REP-03 | Optionale zweite Engine "Windows-Reparatur" über die Windows-3D-Druck-API (wie 3D Builder) | Ist die API nicht verfügbar, ist der Menüpunkt ausgegraut mit Hinweis | P3 |
| F-REP-04 | Vorher/Nachher-Ansicht und Undo der Reparatur | Umschalter zeigt beide Versionen überlagert | P2 |
| F-REP-05 | Vereinfachen (Dreiecksanzahl reduzieren) mit Zielwert oder Prozent | 1 Mio. Dreiecke auf 10% reduziert, Form bleibt erkennbar, Ergebnis bleibt wasserdicht | P3 |

### 4.9 Darstellung, Farben, Dark Theme

| ID | Anforderung | Akzeptanzkriterium | Phase |
| --- | --- | --- | --- |
| F-VIS-01 | Farbe je Objekt (Palette und frei wählbar); Farben werden in 3MF exportiert | In Bambu Studio/OrcaSlicer kommen die Objekte mit ihren Farben an | P2 |
| F-VIS-02 | Schattierung mit Kantenhervorhebung: Kanten ab einem Flächenwinkel von 30° (einstellbar) als Linien; optional Umgebungsverdeckung (SSAO) | Kanten eines Quaders sind bei jeder Farbe und jedem Blickwinkel klar erkennbar | P1 |
| F-VIS-03 | Darstellungsmodi: schattiert, schattiert mit Kanten, Drahtgitter, transparent (Röntgen) | Umschalten per Taste und Menü | P1 |
| F-VIS-04 | Negativobjekte als halbtransparente, schraffierte Geister; ein-/ausblendbar | Geister sind auswählbar, auch wenn sie im Körper liegen | P1 |
| F-VIS-05 | Druckbett mit Raster und Maßen aus den Einstellungen (F-SET-01), Achsenkreuz, ViewCube | Objekt außerhalb des Druckraums wird markiert | P1 |
| F-VIS-06 | Kamera: Orbit, Schwenken, Zoom zum Cursor, "Auswahl einpassen" (F), Standardansichten, perspektivisch/orthografisch | Doppelklick auf ViewCube-Fläche wechselt animiert in diese Ansicht | P1 |
| F-VIS-07 | Dark Theme als Standard, helles Theme optional; Viewport-Hintergrund mit leichtem Verlauf | Alle Dialoge, Menüs und Tooltips sind im Dark Theme gestaltet, keine weißen Fenster | P1 |

### 4.10 Boolesche Operationen (nicht-destruktiv)

Prinzip wie im Bambu-Slicer: Das Ergebnis ist sofort sichtbar, die Operanden bleiben im Modellbaum erhalten und änderbar.

| ID | Anforderung | Akzeptanzkriterium | Phase |
| --- | --- | --- | --- |
| F-BOO-01 | Jedes Objekt hat eine Rolle: Körper oder Loch (Negativobjekt); Umschalter im Panel und per Taste | Ein Zylinder, auf "Loch" gestellt und mit einem Quader gruppiert, schneidet diesen sofort | P1 |
| F-BOO-02 | Operationen Vereinigung, Differenz, Schnittmenge; Ergebnis ist ein Operationsknoten im Modellbaum, die Operanden sind seine Kinder | Operanden lassen sich im Baum einzeln auswählen, verschieben und ändern | P1 |
| F-BOO-03 | Live-Neuberechnung beim Bearbeiten eines Operanden | Verschieben eines Lochs aktualisiert das Ergebnis flüssig, bei typischen Modellen unter 200 ms | P1 |
| F-BOO-04 | Gruppe öffnen (Doppelklick): Bearbeitungsmodus innerhalb der Gruppe, übrige Szene abgedunkelt | Esc verlässt den Gruppenmodus | P1 |
| F-BOO-05 | Fehlerfall: Scheitert eine Operation, bleibt die letzte gültige Geometrie sichtbar, der Knoten wird rot markiert, Meldung mit Vorschlag (z. B. "Operand reparieren") | Kein Absturz, kein Datenverlust | P1 |

### 4.11 Gruppieren, Verschmelzen, Zerschneiden, Abflachen

Begriffe angelehnt an Cricut Design Space, übertragen auf 3D. Alles außer "Abflachen" ist umkehrbar.

| Aktion (UI-Name) | Wirkung | Umkehrbar | Tastenkürzel |
| --- | --- | --- | --- |
| Gruppieren (Group) | Objekte bewegen sich gemeinsam, Geometrie bleibt unverändert | ja, Ungroup | Ctrl+G |
| Verschmelzen (Merge) | Vereinigung der Körper, Löcher schneiden alle Körper der Gruppe; Einzelobjekte bleiben im Baum | ja, Separate stellt alle Objekte mit ihren Positionen wieder her | Ctrl+M |
| Zerschneiden (Slice) | Objekt A teilt Objekt B in Stücke (B ohne A, B in A); Stücke sind einzeln bewegbar, bleiben aber von A und B abgeleitet | ja, solange nicht abgeflacht | Ctrl+K |
| Abflachen (Flatten) | Knoten wird zu einem einzigen Mesh-Objekt, Historie entfällt; Bestätigungsdialog mit Hinweis | nur per Undo | Ctrl+Shift+F |

| ID | Anforderung | Akzeptanzkriterium | Phase |
| --- | --- | --- | --- |
| F-GRP-01 | Gruppieren und Lösen beliebig verschachtelt | 3 Ebenen tief gruppiert, jede Ebene einzeln lösbar | P1 |
| F-GRP-02 | Verschmelzen und Trennen | Nach Trennen sind alle Objekte an der Position, an der sie vor dem Trennen im Verbund standen | P1 |
| F-GRP-03 | Zerschneiden | Zerschneiden eines Quaders mit einer Kugel ergibt Quader mit Mulde und Kugelsegment als zwei bewegbare Teile | P2 |
| F-GRP-04 | Abflachen | Abgeflachtes Objekt ist ein einzelnes Mesh, Undo stellt den Baum vollständig wieder her | P1 |

### 4.12 Extrudieren, Aufteilen, Auf den Boden

| ID | Anforderung | Akzeptanzkriterium | Phase |
| --- | --- | --- | --- |
| F-EXT-01 | Fläche extrudieren: ebene Fläche anklicken, Pfeil ziehen oder Maß tippen; positiv fügt Material hinzu, negativ schneidet ein; Ergebnis ist ein Feature | Deckfläche eines Quaders um 5 mm extrudiert: Höhe +5 mm, Feature im Baum änderbar | P3 |
| F-EXT-02 | Bei Meshes gilt als Fläche die zusammenhängende Menge koplanarer Dreiecke (Toleranz einstellbar) | Deckfläche einer importierten STL-Box wird als eine Fläche erkannt | P3 |
| F-SPL-01 | Aufteilen per Ebene (Achsenebene, Fläche, 3 Punkte) mit Vorschau; Schnittflächen werden geschlossen; beide Teile sind eigene Objekte | Beide Teile sind wasserdicht | P2 |
| F-SPL-02 | Optional Verbindungselemente beim Aufteilen: Passstifte mit Löchern, Toleranz aus den Einstellungen | Stifte werden als eigene Objekte angelegt | P3 |
| F-SPL-03 | In lose Teile zerlegen (getrennte Schalen) | Import mit 5 Einzelteilen ergibt 5 Objekte | P2 |
| F-FLR-01 | Auf den Boden legen: tiefster Punkt auf Z = 0, automatisch nach Einfügen und Import; globale Option "Objekte immer auf den Boden" | Nach Drehen um 30° liegt das Objekt auf Wunsch sofort wieder auf | P1 |

### 4.13 Templates (eigene Geometrien speichern und laden)

Jede Auswahl lässt sich unter einem Namen als Template speichern und jederzeit wieder einfügen. Ein Template speichert den vollständigen Knotenbaum, nicht nur das Mesh: Parameter, Löcher, Muster und Boolesche Operationen bleiben nach dem Laden bearbeitbar.

| ID | Anforderung | Akzeptanzkriterium | Phase |
| --- | --- | --- | --- |
| F-TPL-01 | Auswahl als Template speichern ("Save as Template"): Name ist Pflicht, dazu optional Beschreibung, Kategorie und Schlagwörter; Vorschaubild wird automatisch erzeugt | Speichern ohne Namen ist nicht möglich; doppelte Namen in derselben Kategorie werden abgelehnt mit Vorschlag "Name (2)" | P1 |
| F-TPL-02 | Template laden: aus der Bibliothek unter "My Templates" per Klick oder Ziehen einfügen; eingefügtes Objekt trägt den Template-Namen | Eingefügtes Template ist geometrisch identisch zum gespeicherten und liegt auf dem Boden | P1 |
| F-TPL-03 | Struktur bleibt erhalten: Parameter, Löcher, Muster und Boolesche Knoten sind nach dem Laden bearbeitbar | Lochdurchmesser in einem geladenen Template ändern aktualisiert die Geometrie live | P1 |
| F-TPL-04 | Verwalten: umbenennen, Beschreibung ändern, duplizieren, löschen (mit Bestätigung), Kategorie wechseln | Umbenennen in der Bibliothek ändert keine bereits eingefügten Objekte | P2 |
| F-TPL-05 | Template aktualisieren: geänderte Instanz als neue Version des Templates speichern ("Update Template") oder als neues Template | Bestehende Projekte bleiben unverändert, neue Einfügungen nutzen die neue Version | P2 |
| F-TPL-06 | Freigegebene Parameter: beim Speichern wählen, welche Parameter beim Einfügen abgefragt werden (z. B. Länge, Lochdurchmesser) | Einfügen zeigt ein kleines Formular nur mit diesen Parametern, vorbelegt mit den gespeicherten Werten | P2 |
| F-TPL-07 | Austausch: Template als einzelne Datei (`.fwt`) exportieren und importieren; Ablage im Benutzerordner `Documents\Formwerk\Templates` | Exportiertes Template lässt sich auf einem anderen Rechner importieren und einfügen | P2 |

### 4.14 Einstellungen

| ID | Anforderung | Akzeptanzkriterium | Phase |
| --- | --- | --- | --- |
| F-SET-01 | Einstellungen für Bauraum (X × Y × Z in mm), Düsendurchmesser und Standardtoleranz; eine Konfiguration, keine Profilverwaltung | Geänderter Bauraum wird sofort im Viewport angezeigt | P1 |
| F-SET-02 | Einstellungen für Snapping, Rastergröße, Winkelschritt, Kantenwinkel der Hervorhebung, Autosave-Intervall, Pfad zum Slicer | Einstellungen bleiben nach Neustart erhalten | P1 |

## 5. Ergänzende Funktionen (übernommen)

Übernommen sind zehn der vorgeschlagenen Ergänzungen; die IDs bleiben zur Nachverfolgung erhalten. Nicht übernommen: E-02 (Modellbaum mit Komfortfunktionen; Auswahl, Ein-/Ausblenden und Umbenennen bleiben als Teil von F-SEL-01 und F-BOO-02), E-04 (ersetzt durch die einfache Einstellung F-SET-01), E-08, E-09, E-10, E-11 (Oberfläche nur Englisch), E-14, E-18, E-19.

| ID | Funktion | Akzeptanzkriterium | Phase |
| --- | --- | --- | --- |
| E-01 | Undo/Redo unbegrenzt, mit sichtbarem Verlauf (History-Panel) | Jede Aktion aus Abschnitt 4 ist rückgängig zu machen; Klick auf einen Verlaufseintrag springt zu diesem Stand | P1 |
| E-03 | Export 3MF (mehrere Objekte, Farben, Namen), STL, OBJ; "Open in Slicer" für Bambu Studio, OrcaSlicer, PrusaSlicer, Cura (Pfad konfigurierbar) | Klick öffnet den Slicer mit der exportierten 3MF-Datei | P1 |
| E-05 | Autosave alle 2 Minuten (einstellbar) und Wiederherstellung nach Absturz | Nach erzwungenem Beenden bietet der nächste Start die Wiederherstellung an | P1 |
| E-06 | Schnittansicht: Schnittebene entlang X/Y/Z oder an einer Fläche durch das Modell schieben; Schnittfläche farbig gefüllt | Bohrungen und Senkungen sind im Schnitt in Echtzeit sichtbar | P2 |
| E-07 | Messwerkzeug: Punkt zu Punkt, Fläche zu Fläche, Winkel, Durchmesser; nutzt die Snap-Punkte | Abstand zweier paralleler Flächen wird auf 0,01 mm genau angezeigt | P2 |
| E-12 | Druckbarkeitsprüfung: Überhänge über 45° einfärben (Winkel einstellbar), Mindestwandstärke, Details kleiner als die Düse, schwebende Teile, Bauraum überschritten | Prüfung läuft auf Knopfdruck, Fundstellen sind im Viewport markiert und in einer Liste anklickbar | P3 |
| E-13 | Vereinfachter Skizzenmodus: Rechteck, Kreis, Polylinie, Bogen auf einer Fläche oder Achsenebene zeichnen, dann extrudieren oder drehen (Rotationskörper); keine geometrischen Constraints | Rechteckprofil, um eine Achse um 360° gedreht, ergibt einen Ring; Skizze bleibt nachträglich änderbar | P3 |
| E-15 | Aushöhlen ("Hollow"/"Shell"): Körper mit Wandstärke aushöhlen, optional mit offener Fläche; Grundformen exakt, beliebige Meshes über volumetrisches Verfahren (SDF) mit GPU-Beschleunigung | Quader 40 × 40 × 40 mit Wand 2 mm hat Innenraum 36 × 36 × 36; bei Meshes Wandstärke innerhalb ±1 Voxel | P3 |
| E-16 | Generatoren in der Bibliothek: Zahnräder (Stirnrad, Modul und Zähnezahl), Scharniere (Print-in-place), Schnappverbindungen, Toleranz-Testkörper | Zwei Zahnräder mit gleichem Modul greifen bei Achsabstand nach Formel ineinander | P3 |
| E-17 | Metrische Gewinde M3 bis M20: Bolzen, Mutter, Innengewinde in Bohrungen; druckoptimiertes Profil mit Toleranz aus den Einstellungen | Gedruckte M8-Schraube passt in gedruckte M8-Mutter | P4 |

## 6. Datenmodell und Projektformat

Das Dokument ist ein Baum aus Knoten, dessen Auswertung einen gerichteten azyklischen Graphen (DAG) bildet. Geometrie wird nie gespeichert, wenn sie aus Parametern berechenbar ist; gespeichert werden Parameter und Operationen. Nur importierte Meshes und abgeflachte Objekte liegen als Mesh vor.

**Gemeinsame Eigenschaften jedes Knotens:** `Id` (GUID), `Name`, `Transform` (Position, Rotation als Quaternion, Skalierung), `Role` (Körper oder Loch), `Color`, `Visible`, `Locked`, `Children`.

| Knotentyp | Inhalt | Ergebnis |
| --- | --- | --- |
| `PrimitiveNode` | Generator-ID (z. B. `cylinder`) und Parameter (Durchmesser, Höhe, Segmente, Fase, Rundung) | Mesh aus dem Generator |
| `MeshNode` | Verweis auf gespeichertes Mesh | Das Mesh |
| `GroupNode` | Kinder, keine Operation | Kinder unverändert, gemeinsame Transformation |
| `BooleanNode` | Operation (Vereinigung, Differenz, Schnittmenge), Kinder als Operanden | Ein Körper |
| `MergeNode` | Kinder; Körper werden vereinigt, Löcher abgezogen | Ein Körper, Kinder bleiben erhalten |
| `SliceNode` | Schneidendes Objekt und Ziel | Mehrere Teile, je als abgeleiteter Ausgang |
| `HoleNode` | Bohrungstyp, Preset, Maße, Tiefe; immer Rolle "Loch" | Negativkörper |
| `PatternNode` | Quelle, Muster-Typ (linear, kreisförmig), Anzahl, Abstand, übersprungene Instanzen | Instanzen der Quelle |
| `MirrorNode` | Quelle, Ebene, verknüpft ja/nein | Gespiegelte Kopie |
| `FeatureNode` | Extrusion, Fase/Rundung an Kanten (Stufe 2), Aushöhlen; mit Referenz auf Flächen/Kanten | Veränderter Körper |

**Parameterwerte** sind immer Ausdrücke (`"20"`, `"Wandstaerke*2"`, `"Schraube+Toleranz"`). Ein kleiner, sicherer Ausdrucksparser (nur Zahlen, + − × ÷, Klammern, Variablen, `min`/`max`/`round`) wertet sie aus. Kein `eval`, kein Skriptcode.

**Auswertung:** Jeder Knoten hat einen Hash über seine Eingaben (Parameter, Transform, Hashes der Kinder). Ergebnisse werden pro Hash gecacht. Bei einer Änderung werden nur Knoten mit geändertem Hash neu berechnet, in einem Hintergrund-Thread, abbrechbar bei neuer Änderung.

**Topologische Referenzen** (eine Bohrung auf "dieser Fläche") werden nicht über Dreiecksindizes gespeichert, sondern über Platzierung relativ zum Elternobjekt (Position, Normale). So bleiben Bohrungen stabil, wenn sich das Elternobjekt neu berechnet.

**Projektdatei `.fwp`:** ZIP-Container mit

- `document.json`: Knotenbaum, Parameter, Variablen, projektbezogene Einstellungen (Bauraum, Toleranz), Schemaversion
- `meshes/<id>.bin`: importierte und abgeflachte Meshes (binär, komprimiert)
- `thumbnail.png`: Vorschaubild für den Datei-Explorer und die Bibliothek

Das Schema ist versioniert; jede Schemaänderung kommt mit einer Migration und einem Test, der eine Datei der Vorversion lädt. Optional (P4, nur bei geringem Aufwand, siehe D-06): Projektdatei zusätzlich als gültige 3MF mit eigenem XML-Namespace für die Historie speichern, damit Slicer sie direkt öffnen können.

**Template-Datei `.fwt`:** gleiches Format wie die Projektdatei, enthält genau einen Teilbaum plus Metadaten (Name, Beschreibung, Kategorie, Schlagwörter, freigegebene Parameter, Version, Vorschaubild). Dadurch nutzen Projekte und Templates denselben Lade- und Speichercode.

## 7. UI/UX-Konzept

Der Viewport nimmt mindestens 70% der Fläche ein; alle Panels sind einklappbar. Der Nutzer arbeitet direkt am Objekt, Panels dienen nur für exakte Werte und Überblick.

**Sprache der Oberfläche: ausschließlich Englisch.** Alle Menüs, Tooltips, Meldungen, Dialoge und die Hinweiszeile sind englisch. Die deutschen Begriffe in dieser Spezifikation beschreiben die Funktion; wo der englische UI-Name feststeht, steht er in Klammern. Alle Texte liegen zentral in einer Ressourcendatei, damit sie einheitlich bleiben.

**Bedienregeln:**

1. **Hinweiszeile:** Unten steht immer, was das aktive Werkzeug als Nächstes erwartet ("Click a face to place a hole").
2. **Mini-Werkzeugleiste an der Auswahl:** Die häufigsten Aktionen (Loch/Körper, Farbe, Duplizieren, Auf den Boden) schweben neben dem gewählten Objekt.
3. **Esc bricht immer ab**, Enter bestätigt immer, Rechtsklick öffnet ein Kontextmenü.
4. **Snap-Schalter sichtbar** in der Statusleiste: Raster, Objekte, Winkel; mit aktuellem Wert.
5. **Keine modalen Dialoge** für Bearbeitungen; Bestätigung nur bei Abflachen und Löschen großer Gruppen.
6. **Fehlermeldungen in Alltagssprache** mit Lösungsvorschlag, nie Stacktraces.

**Tastenkürzel (Standard, frei belegbar):**

| Taste | Aktion (UI-Name) | Taste | Aktion (UI-Name) |
| --- | --- | --- | --- |
| W | Move | Ctrl+Z / Ctrl+Y | Undo / Redo |
| E | Rotate | Ctrl+D | Duplicate |
| R | Scale | Ctrl+G | Group |
| H | Toggle Solid/Hole | Ctrl+M | Merge |
| B | Drop to Floor | Ctrl+K | Slice |
| F | Frame Selection | Del | Delete |
| Shift (halten) | Winkel-Snap aus | Alt (halten) | Raster-Snap aus |
| Ctrl (beim Skalieren) | Scale from Center | Ctrl+Shift+T | Save as Template |
| 1 bis 7 | Standardansichten | Ctrl+Shift+F | Flatten |

**Dark Theme:** Grautöne statt Schwarz (UI-Hintergrund etwa #1E1F22, Panels #2B2D30, Text #DFE1E5), eine Akzentfarbe für Auswahl und aktive Werkzeuge, Gizmo-Achsen in den üblichen Farben X rot, Y grün, Z blau. Kontrast mindestens WCAG AA. UI-Skalierung 100% bis 200% für hochauflösende Bildschirme. Alle Funktionen sind auch per Tastatur erreichbar.

## 8. Nicht-funktionale Anforderungen

| ID | Bereich | Anforderung | Messung |
| --- | --- | --- | --- |
| N-PRF-01 | Performance | (zurückgestellt, siehe D-03; Ersatzziel 20 fps bei 1 Mio. Dreiecken) Viewport mit mindestens 60 fps bei 2 Mio. Dreiecken auf Mittelklasse-GPU | Benchmark-Szene im Testprojekt |
| N-PRF-02 | Performance | Neuberechnung nach Parameteränderung unter 200 ms bei Modellen mit bis zu 50 Knoten | Automatisierter Benchmark, Regression bei mehr als 20% Verschlechterung |
| N-PRF-03 | Performance | Start bis bedienbar unter 3 s auf SSD | Messung in CI auf Referenz-VM |
| N-ROB-01 | Robustheit | UI friert nie ein; jede Geometrieoperation läuft im Hintergrund und ist abbrechbar | Test: Boolean mit 2 Mio. Dreiecken, UI reagiert weiter |
| N-ROB-02 | Robustheit | Kein Datenverlust bei Absturz (Autosave, siehe E-05); Projektdateien werden atomar geschrieben (temporäre Datei, dann umbenennen) | Test: Prozess während Speichern beenden, Datei bleibt lesbar |
| N-SEC-01 | Sicherheit | Dateiparser behandeln jede Datei als nicht vertrauenswürdig: Größen- und Dreieckslimits, Prüfung von Headerangaben gegen Dateigröße, keine Abstürze bei Fuzzing | Fuzzing der Parser (STL, OBJ, 3MF, Projekt) in CI |
| N-SEC-02 | Sicherheit | 3MF/ZIP: Schutz vor ZIP-Bomben (Entpackgröße und Kompressionsrate begrenzen) und Zip-Slip (keine Pfade außerhalb des Zielordners) | Negativtests mit präparierten Dateien |
| N-SEC-03 | Sicherheit | XML-Parser ohne DTD-Verarbeitung und externe Entitäten (Schutz vor XXE) | Negativtest mit XXE-Payload |
| N-SEC-04 | Sicherheit | Ausdrücke in Parametern werden von einem eigenen Parser ausgewertet, nie als Code ausgeführt | Code-Review-Regel, Test |
| N-PRV-01 | Datenschutz | Keine Telemetrie, kein Online-Zwang, keine Konten; optionale Update-Prüfung nur nach Zustimmung | Netzwerkmonitor zeigt ohne Zustimmung keine Verbindungen |
| N-DEP-01 | Deployment | Self-contained Build für win-x64 als portable ZIP und als Installer ohne Adminrechte (Benutzerinstallation) | Installation auf frischem Windows ohne .NET erfolgreich |
| N-DEP-02 | Deployment | Zielplattform Windows 10 22H2 und Windows 11, x64; GPU mit DirectX 11; Fallback auf Software-Rendering (WARP) | Start in VM ohne GPU funktioniert, wenn auch langsam |
| N-DEP-03 | Deployment | Signierte Binärdateien (Code Signing), Drittanbieter-Lizenzen in "Über" gelistet | Release-Checkliste |
| N-LOG-01 | Betrieb | Lokale Logdatei mit Rotation, Crash-Report lokal speichern, Senden nur manuell | Log vorhanden nach Fehlerfall |

### GPU-Nutzung

**Stand 2026-10-19 (Entscheidung D-03):** Es gibt vorerst keinen GPU-Renderer; der Viewport läuft auf WPF-3D. Die mit "zurückgestellt" markierten Anforderungen gelten erst wieder, wenn ein DirectX-Renderer gebaut wird. Bis dahin gilt als Ersatzziel: mindestens 20 fps beim Drehen mit 1 Mio. Dreiecken, flüssiges Ziehen auch bei großen Meshes, Auswahl per CPU-Raycast.

Die GPU wird überall dort eingesetzt, wo sie messbar schneller ist; jede GPU-Funktion hat einen CPU-Fallback mit gleichem Ergebnis. Boolesche Operationen und Reparatur bleiben auf der CPU (mehrkernig), weil es dafür keine ausgereiften, permissiv lizenzierten GPU-Verfahren gibt.

| ID | Anforderung | Messung |
| --- | --- | --- |
| N-GPU-01 | (zurückgestellt, siehe D-03) Rendering vollständig auf der GPU (DirectX 11); Muster und Duplikate per Instancing statt kopierter Meshes | 1.000 Instanzen eines Musters ohne fps-Einbruch unter 60 |
| N-GPU-02 | (zurückgestellt, siehe D-03) Auswahl (Picking) über einen GPU-ID-Puffer statt CPU-Raycast | Auswahl reagiert unter 16 ms bei 2 Mio. Dreiecken |
| N-GPU-03 | (zurückgestellt, siehe D-03) Darstellungsanalysen als Shader: Überhangfärbung (E-12), Schnittansicht (E-06), Kantenhervorhebung | Echtzeit beim Drehen der Kamera |
| N-GPU-04 | (zurückgestellt, siehe D-03) Volumetrische Operationen per GPU-Compute: Aushöhlen beliebiger Meshes (E-15), Wandstärkenanalyse (E-12) | GPU- und CPU-Ergebnis stimmen innerhalb einer Voxelgröße überein; GPU mindestens 3-mal schneller, sonst bleibt CPU der Standard |
| N-GPU-05 | Geometrieberechnung auf der CPU parallel über alle Kerne (Manifold, eigene Generatoren) | Booleans eines Musters mit 50 Löchern nutzen mehr als einen Kern |
| N-GPU-06 | (zurückgestellt, siehe D-03) GPU-Auswahl automatisch, Umschalter "Use GPU acceleration" in den Einstellungen | Ohne geeignete GPU läuft alles über CPU, ohne Fehlermeldung |

### Lizenzen

| ID | Anforderung | Messung |
| --- | --- | --- |
| N-LIC-01 | Ausgelieferte Abhängigkeiten unter MIT, BSD, Apache 2.0, Boost oder zlib; LGPL nur als separate DLL nach N-LIC-04; GPL, AGPL und Non-Commercial nie | Automatische Lizenzprüfung aller NuGet- und nativen Pakete in CI, Build schlägt bei nicht erlaubten Lizenzen fehl |
| N-LIC-02 | Datei `THIRD-PARTY-NOTICES.txt` mit allen Lizenztexten, im Programm unter "About" einsehbar | Datei wird im Build erzeugt und ist vollständig |
| N-LIC-03 | Bibliotheksformen, Icons und Schriften der Oberfläche sind selbst erstellt oder permissiv lizenziert | Herkunft jeder Datei in `assets/SOURCES.md` dokumentiert |
| N-LIC-04 | LGPL-Auflagen für OpenCascade: (1) nur dynamisch als separate DLLs, nie statisch in die EXE; (2) Lizenztext und Hinweis auf OpenCascade mitliefern; (3) Quellcode der verwendeten OpenCascade-Version inklusive eigener Änderungen daran bereitstellen (Download-Link oder ZIP); (4) Austausch der DLLs durch Nutzer nicht technisch verhindern und in der eigenen Lizenz nicht verbieten | Release-Checkliste; Test: Programm startet mit selbst gebauten OpenCascade-DLLs gleicher Version |

## 9. Roadmap

P1 liefert ein nutzbares Programm, mit dem sich einfache Teile bauen und drucken lassen; jede weitere Phase baut darauf auf. Die Phasen sind nach Umfang, nicht nach Datum geplant. Eine Phase gilt erst als abgeschlossen, wenn ihr Gate erfüllt ist.

&#91;embedded content: Roadmap · 4 Phasen, 3 Gates\]

Die Phasenzuordnung jeder einzelnen Anforderung steht in den Tabellen der Abschnitte 4 und 5.

## 10. Vorgaben für die KI-gestützte Entwicklung

Der Agent arbeitet in kleinen, prüfbaren Schritten: eine Anforderungs-ID pro Arbeitspaket, Akzeptanzkriterium zuerst als Test, dann Implementierung. Diese Spezifikation liegt im Repo als `docs/spec.md` und ist die einzige Quelle für Anforderungen.

**Repo-Struktur:**

- `AGENTS.md` bzw. `CLAUDE.md`: Kurzfassung dieses Abschnitts, Build- und Testbefehle, Modulregeln aus Abschnitt 3
- `docs/spec.md`: diese Spezifikation
- `docs/adr/`: Architekturentscheidungen (eine Datei je Entscheidung, nummeriert)
- `src/` mit den Projekten aus Abschnitt 3, `tests/` gespiegelt dazu
- `testdata/meshes/`: Testkorpus (heile und gezielt defekte Meshes, nur selbst erzeugt oder mit klarer freier Lizenz)

**Arbeitsablauf je Arbeitspaket:**

1. Anforderung und betroffene Abschnitte lesen; Unklarheiten als Frage in `docs/open-questions.md` notieren statt zu raten.
2. Akzeptanzkriterium als fehlschlagenden Test schreiben.
3. Implementieren, bis der Test grün ist; keine Änderungen außerhalb des Arbeitspakets.
4. Commit-Nachricht beginnt mit der ID, z. B. `F-HOL-02: Kegelsenkung und Zylindersenkung`.
5. Kurzer Bericht: was umgesetzt, was offen, welche Annahmen.

**Regeln für den Code:**

- Nullable Reference Types an, Warnungen als Fehler, `TreatWarningsAsErrors`
- MVVM in der App (CommunityToolkit.Mvvm); keine Logik in Code-Behind
- Jede Dokumentänderung als Command mit `Do`/`Undo`
- Native Interop (Manifold, später OCCT) nur in einem eigenen Projekt, mit `SafeHandle` und Tests auf Speicherlecks
- Lange Operationen asynchron mit `CancellationToken`
- Keine Netzwerkzugriffe im Code außer der optionalen Update-Prüfung

**Portabilitätsregel (wegen D-02):** Alle Projekte außer `Formwerk.App` und `Formwerk.Viewport` zielen auf `net10.0` ohne `-windows` und nutzen keine Windows-APIs. Windows-spezifische Adapter (Windows-Reparatur F-REP-03, GPU-Compute über DirectX) liegen in eigenen Projekten `*.Windows` und implementieren Schnittstellen aus den portablen Projekten. Ein späterer Mac-Port ersetzt dann nur Oberfläche, Renderer und Adapter. CI baut die portablen Projekte zusätzlich auf einem Linux-Runner, um Verstöße früh zu erkennen.

**Neue Abhängigkeiten** nur mit ADR: Lizenz (bevorzugt MIT, BSD, Apache 2.0, Boost, zlib; LGPL nur als separate DLL nach N-LIC-04; GPL, AGPL und Non-Commercial-Lizenzen nie, damit ein Verkauf nie blockiert ist), Wartungsstatus (letztes Release unter 12 Monaten), Größe.

**Teststrategie Geometrie:**

| Testart | Beispiel |
| --- | --- |
| Invarianten | Jedes Ergebnis ist wasserdicht und mannigfaltig, Normalen zeigen nach außen |
| Analytische Werte | Zylinder r = 10, h = 20 mit 128 Segmenten: Volumen innerhalb 0,5% von π·r²·h |
| Eigenschaftsbasiert (FsCheck) | Beliebige Verschiebung und Drehung ändert das Volumen nicht; Differenz A minus B hat höchstens das Volumen von A |
| Golden Files | Export von Referenzprojekten nach STL/3MF wird mit gespeicherten Dateien verglichen |
| Reparatur-Korpus | Alle defekten Testmeshes werden nach F-REP-02 repariert und geprüft |
| Roundtrip | Projekt speichern, laden, speichern: Dateien identisch |
| UI | ViewModel-Tests für alle Werkzeuge; wenige End-to-End-Tests mit FlaUI |

**CI (z. B. GitHub Actions, Windows-Runner):** Build, alle Tests, Kurz-Fuzzing der Parser, Performance-Benchmarks, self-contained Publish als Artefakt.

**Definition of Done je Anforderung:**

- [ ] Akzeptanzkriterium als automatisierter Test vorhanden und grün
- [ ] Undo/Redo funktioniert für die neue Aktion
- [ ] Funktion per Maus und per Zahleneingabe bedienbar (Leitprinzip 2)
- [ ] Dark Theme korrekt, alle UI-Texte englisch und in der Ressourcendatei
- [ ] Projekt speichern und laden erhält den neuen Zustand
- [ ] Keine neuen Warnungen, CI grün

## 11. Offene Entscheidungen und Annahmen

Die wichtigste offene Entscheidung ist der Geometriekern: Er bestimmt, ob Fasen und Rundungen an beliebigen Kanten (F-EDG-02) überhaupt machbar sind. Alle Entscheidungen werden als ADR festgehalten.

| Nr. | Entscheidung | Optionen | Empfehlung | Spätestens |
| --- | --- | --- | --- | --- |
| D-01 | Geometriekern | (a) nur Manifold, (b) Manifold plus OpenCascade-Modul, (c) nur OpenCascade | **Entschieden: (b).** Manifold ist der Kern für alles; Fasen und Rundungen der Stufen 1 und 2 laufen darüber. OpenCascade kommt in P4 als optionales Modul (separate DLLs, LGPL-Auflagen nach N-LIC-04) für F-EDG-03. `IGeometryKernel` muss das von Anfang an zulassen | erledigt |
| D-02 | UI-Framework | WPF oder Avalonia | **Entschieden: WPF**, nur Windows. Damit ein späterer Mac-Port möglich bleibt, gilt die Portabilitätsregel aus Abschnitt 10: Windows-Code nur in `App`, `Viewport` und Adapter-Projekten `*.Windows` | erledigt |
| D-03 | Render-Bibliothek | Helix Toolkit, Vortice.Windows mit eigenem Renderer, oder vorerst WPF-3D | **Entschieden (Stand 2026-10-19): vorerst kein GPU-Renderer.** Der Viewport bleibt auf WPF-3D (`Viewport3D`), kein neues Paket. Messung auf einer Intel-Iris-Xe-Laptop-GPU: Drehen mit 54 fps bei 100.000, 42 fps bei 300.000, 24 fps bei 1 Mio. und 9 fps bei 2 Mio. Dreiecken (1,4 GB Speicher bei 1 Mio.); Ziehen großer Meshes ruckelt, weil die Szene je Bild neu aufgebaut wird. Folge: N-PRF-01 und N-GPU-01 bis N-GPU-04 sowie N-GPU-06 sind zurückgestellt (siehe Abschnitt 8, GPU-Nutzung); Ersatzziel: mindestens 20 fps beim Drehen mit 1 Mio. Dreiecken und flüssiges Ziehen. Die Schnittstelle des Viewports (Objekte hinein, Klicks heraus) bleibt renderer-unabhängig, ein DirectX-Renderer kann später nachgerüstet werden, sobald Nutzer an die Grenzen stoßen (ADR 0005) | erledigt (vorläufig) |
| D-04 | Vertriebsmodell | kostenpflichtig, kostenlos, Open Source | Vorerst kein Verkauf geplant, Verkauf soll aber jederzeit möglich bleiben; daraus folgt N-LIC-01 | erledigt |
| D-05 | Herkunft der Bibliotheksformen | selbst generiert (parametrisch), externe Modelle | Selbst generiert; externe nur mit kommerziell nutzbarer Lizenz (N-LIC-03) | P2 |
| D-06 | Projektdatei zusätzlich als 3MF | ja, nein | **Nur wenn es einfach ist:** umsetzen, wenn sich die Historie mit lib3mf als eigener Namespace bzw. Zusatzdatei in die 3MF schreiben lässt, ohne das Projektformat umzubauen. Sonst entfällt es; der normale 3MF-Export (E-03) bleibt davon unberührt | P4 |
| D-07 | Endgültiger Name | Formwerk ist Arbeitstitel; Vorschläge: Snapform, Partwright, Shapekit, Stackform, Primwork, Jigsmith, Plinth | Markenrecherche vor Veröffentlichung | vor Release |

**Annahmen:**

- Entwicklung durch eine Person mit KI-Coding-Agenten, keine festen Termine
- Version 1 nur Windows x64, Oberfläche nur Englisch; Einheit Millimeter; das Programm soll verkauft werden dürfen
- Zieldrucker sind FDM-Drucker; Resin funktioniert über denselben Export, bekommt aber keine eigenen Prüfungen
- "Reparaturfunktion wie 3D Builder" heißt: gleiche Fehlerklassen werden behoben; die Windows-API ist eine optionale Zusatz-Engine, keine Pflichtabhängigkeit
