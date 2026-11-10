# G-Helper Clix changes

Each release on this fork is named after the official G-Helper version it is built from (for example
v0.291), because G-Helper's updater compares that number. A rebuild of the same version replaces the
files in that release, so the "Clix build" number below is what tells builds apart.

## Clix build 4
- Lighting lives only in Fn+W. The Fn+E double buffer and the Fn+R "lights off" slot are gone: those
  layers also change keys (F-key lock, command lock), so using them broke typing.
- Lights off (Extra > Laptop Backlight > Matrix Clix) writes black into Fn+W; lights on writes the colours back.
- Fn+E and Fn+R can no longer be written at all, even by mistake.
- Icon badge keeps the original icon's frame order and format, and the build checks the result.

## Clix build 3
- The Clix uses G-Helper's own keyboard window: Static or Custom (per-key) colours, palette, Fill All, Sync with laptop.
- Power options row (Awake, Sleep, Boot/Shutdown, Battery).
- Pink-dot icon and the name "G-Helper Clix".

## Clix build 2
- Own tile and settings window, separate colour for the red keycaps, Fn+W selected automatically.
