# Abnahme Phase 2: Durchklicken in der echten App

Stand 2026-10-23. Die Tests sind grün (2605), aber die Teile unten wurden **nicht** in der echten App geprüft (Datei-Dialoge und
Drag-and-drop lassen sich hier nicht zuverlässig automatisieren). Bitte einmal von Hand durchgehen. Programm: `binary\Formwerk.exe`.
Beschädigte Beispieldateien: `testdata\repair\` (mit **Datei > Importieren…** laden).

Pro Punkt steht, was passieren soll. Was nicht stimmt, bitte kurz notieren (Punkt-Nummer + was du siehst).

## A. Reparatur (F-REP-01/02/04)

| # | Aktion | Erwartung |
|---|---|---|
| A1 | `box-with-hole.stl` importieren | Gelbes Banner, Knopf „Open edges (3)“ |
| A2 | Auf „Open edges (3)“ klicken | 3 rote Balken am Loch; zweiter Klick blendet sie aus |
| A3 | „Repair“ klicken | Banner verschwindet, Meldung nennt das geschlossene Loch |
| A4 | Ansicht > „Show Version Before Repair“ ankreuzen | Alte Version als schraffierter Geist über dem Körper |
| A5 | Strg+Z | Reparatur zurück, Banner wieder da, Menüpunkt aus |
| A6 | `overlapping-boxes.stl` importieren | Banner mit „Cuts through itself“; Klick zeigt rote Schnittlinien |
| A7 | „Repair“ | Ein Körper, Meldung „united 1 overlapping bod(ies)“ |
| A8 | `bow-tie.stl` importieren | „Touching points (1)“, Klick zeigt einen roten Würfel; **kein** funktionierender Repair-Knopf |
| A9 | `doubled-triangle.stl`, `turned-triangle.stl`, `speck.stl` | Je eine passende Art im Banner („Doubled triangles“, „Turned triangles“, „Specks“); Repair macht das Banner leer |

## B. Bibliothek (F-LIB-02/03/04, F-PAR-05)

| # | Aktion | Erwartung |
|---|---|---|
| B1 | Suche: „Schraube“ | Drei „Hex Bolt …“ Kacheln mit Bild |
| B2 | Stern bei einer Kachel, Programm neu starten | Stern bleibt; Kategorie „Favorites“ zeigt sie |
| B3 | Kachel „Box with Lid“ anklicken | Formular „Insert Box with Lid“ mit 5 Feldern (50, 30, 20, 2, 0.2); OK fügt Box und Deckel nebeneinander ein |
| B4 | Im Formular Wand = 3 oder eine Projektvariable eintragen | Außenmaß = Innenmaß + 2 × Wand; mit Variable folgt das Element einer Änderung der Variable |
| B5 | Kachel „Cylinder“ mit der Maus auf die Schräge eines Keils ziehen | Schraffierter Geist folgt der Maus; beim Loslassen steht der Zylinder senkrecht auf der Schräge |
| B6 | Auf leeren Boden ziehen | Element steht aufrecht an der Mausposition (Raster) |
| B7 | Objekte wählen, Bearbeiten > „Save as Template…“, Namen eingeben | Template erscheint als Kachel mit Bild („My Templates“), Rechtsklick-Menü wie bisher |
| B8 | Eine Gruppe öffnen (Doppelklick), dann ein Element hineinziehen | Es landet in der Gruppe |

## C. Drehen und Skalieren (F-ROT-03, F-SCL-05)

| # | Aktion | Erwartung |
|---|---|---|
| C1 | Box, Werkzeug „Rotate“ | Unten Panel „Pivot“ |
| C2 | Pivot „Corner of the bounding box“ / „Left front bottom“ | Drehgriffe sitzen in der Ecke; Drehen um 90° lässt die Ecke stehen |
| C3 | Pivot „A point I pick“, Ecke eines Körpers anklicken | Griffe springen auf den Punkt (rastet an Ecken ein) |
| C4 | Zylinder + „Drill Hole“ (3.4 mm), beides gruppieren, „Scale“-Werkzeug, Eckgriff auf 200 % | Zylinder 40 mm, Bohrung bleibt 3.4 mm, wenn „Keep holes and chamfers“ an ist; aus: Bohrung 6.8 mm |

## D. Schneiden und Teilen (F-GRP-03, F-SPL-01/03)

| # | Aktion | Erwartung |
|---|---|---|
| D1 | Box und Kugel (Kugel halb in der Box), beide wählen, Bearbeiten > „Cut Apart“ | „Box“ und „Box piece“, Kugel weg |
| D2 | Ansicht > „Section View“, Panel „Split Here“ | Zwei geschlossene Teile „A“ und „B“ |
| D3 | Panel „3 points“, drei Punkte anklicken | Schnittebene geht durch die drei Punkte |
| D4 | Gruppe aus getrennten Körpern, Bearbeiten > „Split into Parts“ | Ein Objekt pro Körper |

## E. Nicht hier prüfbar

* **Farben im Slicer** (F-VIS-01): Objekte einfärben, **3MF exportieren**, in Bambu Studio / OrcaSlicer öffnen. Die Farben stehen in der
  Datei (per Test geprüft), ob der Slicer sie anzeigt, kann nur dort gesehen werden.
* **Fenster im Vordergrund:** Die Skripte zur Bedienung der App scheitern, wenn ein anderes Fenster den Fokus hält.
