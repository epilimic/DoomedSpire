# The status-bar face: which face shows when, and painting your own

The face in DOOM's status bar is one of 42 small pictures. The mod makes all 42 from the picture of the character you are playing (see `CLAUDE.md`), and you can paint over the result.

## Which health is which row

The rows are DOOM's own health, **not your Slay the Spire HP**: the DOOM player starts at 100 and the face follows that number. (From the engine: `StatusBar.cs`, `CalcPainOffset`: level = (100 - health) * 5 / 101, rounded down; health above 100 counts as 100.)

| Row (top to bottom) | DOOM health | What it should look like |
|---|---|---|
| 0 | 100 down to 80 | Fine, not a scratch |
| 1 | 79 down to 60 | Roughed up |
| 2 | 59 down to 40 | Hurt |
| 3 | 39 down to 20 | Badly hurt |
| 4 | 19 down to 1 | Nearly dead |

A last row has the two faces that do not depend on health: god mode and dead (health 0).

## Which face shows when (columns, left to right)

| Column | Face | When DOOM shows it |
|---|---|---|
| 1 | Straight | Nothing is happening. Columns 1, 2 and 3 take turns every half second, picked at random, so the face seems to glance around |
| 2 | Straight (glance) | same |
| 3 | Straight (glance) | same |
| 4 | Turn right | You were hit by something to your right, and the hit was not head-on |
| 5 | Turn left | The same from your left |
| 6 | Ouch | You lost more than 20 health in one hit. Shown for about a second |
| 7 | Evil grin | You just picked up a weapon. Shown for about two seconds |
| 8 | Rampage (angry) | You were hit head-on (within 45 degrees of straight ahead), or took damage from your own doing, or have held the fire button for about two seconds |
| last row, 1 | God mode | Invulnerable: the god mode cheat, or an invulnerability sphere |
| last row, 2 | Dead | Health is 0 |

Every health row has columns 1 to 8, so the same expression exists at all five levels.

## The painted faces

Every playable character has a painted sheet built into the mod: `assets/<character>-faces.png` (`defect`, `fool`, `ironclad`, `necrobinder`, `regent`, `silent`). It holds all 42 faces on a transparent background, one picture pixel per DOOM pixel (280 x 218), laid out as in the tables above: 5 rows of 8, then a row with the two last faces. The mod reads the one for the character you are playing and uses it instead of Doomguy's faces. A character with no sheet (a modded one, say) gets faces made from its portrait in the top bar, with paleness, redness and blood marks that grow with each health row.

To change a sheet, edit the file in `assets/` in an editor that can zoom right in (the faces are about 24 pixels wide) and that does not smooth or blur (pencil tools only), keep its size, and build. The mod no longer writes sheets to paint on or a preview of the faces in use.

Things to know:
- Any colour works; each pixel is matched to the nearest of DOOM's 256 colours, so very subtle shades may merge.
- A fully transparent pixel stays transparent (the status bar shows through). Mostly transparent ones count as transparent too (half or less opaque).
- You can paint anywhere inside a face's own box, but pixels outside the box a face has in the game are ignored. The turning faces' boxes are a few pixels wider than the straight ones, and the two glance faces (columns 2 and 3) are made one pixel wider on each side than the game's own, so a head moved a pixel is not cut off.
- The glance columns are the whole head moved one pixel to the left and to the right. The eyes do not move on their own, so paint them if you want them to glance. Sliding only what is inside the head instead looked like two faces, one still and one moving.
- A sheet in the data folder called `<character>-faces-painted.png` (`~/.local/share/SlayTheSpire2/DoomedSpire/data/`) is used instead of the built-in one, for trying a sheet before building it. Remove it to go back.
- You can save the sheet enlarged by a whole number (for example 6 times, each dot as a 6 x 6 block); the mod reads the middle of every block.
- A sheet of any other size is refused, and the log says what size it must be; the faces made from the portrait are used then.
