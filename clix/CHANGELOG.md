# G-Helper Clix changes

Each release on this fork is named after the official G-Helper version it is built from (for example
v0.291), because G-Helper's updater compares that number. A rebuild of the same version replaces the
files in that release, so the "Clix build" number below is what tells builds apart.

## Clix build 6
- No more dark flash when you change a colour. Writes go to whichever of Fn+W / Fn+E isn't showing, then
  the keyboard switches to it. Lights off works the same way, so it's instant too.
- Before first use, G-Helper gives Fn+W and Fn+E the normal key table once, so typing is identical in
  both (factory Fn+W = arrow keys on / RAlt Menu RCtrl, Fn+E = F1-F12 on the number row). Fn+R is not touched.
  Set `clix_double_buffer` to 0 in config.json to use Fn+W only.
- Profiles: the window's profile selector (Default + Profile 1-5) saves a full lighting setup each;
  switching applies it.
- Painting: drag across keys to paint them. Right-click a key for "Use this key's colour", "Paint this
  row", red keys, everything but the red keys, letters, number row, all keys. Right-click Fill All for
  the same groups.

## Clix build 5
- App icon: the original icon's bitmap headers are kept byte for byte, and 48/32/24/16 px sizes are
  added so Windows doesn't have to shrink the 64 px one for the taskbar.

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
