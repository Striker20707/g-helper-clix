# G-Helper Clix changes

Each release on this fork is named after the official G-Helper version it is built from (for example
v0.291), because G-Helper's updater compares that number. A rebuild of the same version replaces the
files in that release, so the "Clix build" number below is what tells builds apart.

## Clix build 10
- Fix: opening G-Helper several times in a row (for example closing it and opening it again quickly, or
  opening the exe while it was already in the tray) was wrongly counted as failed starts. After two, the
  keyboard lighting was paused with a "crash" toast even though nothing had crashed, and after three the
  official G-Helper could have been started instead. Opening G-Helper while it already runs no longer
  counts, and a start now counts as good after 20 seconds instead of 45.
- Smooth hand-over: opening G-Helper Clix or official G-Helper while the other one is running now asks
  the running one to close normally first (up to 3 seconds), instead of it being cut off. The Clix leaves
  live mode at once and falls back to its stored Fn+W colours, which G-Helper Clix keeps up to date in the
  keyboard's memory, so the keyboard keeps working with the right colours under official G-Helper.
- The toast now says why lighting is paused: an actual crash in Clix code, or a start that didn't finish.

## Clix build 9
- Easier to tell apart: the app, taskbar and tray icons now have a thick purple ring instead of a small
  pink dot, so the Clix build stands out even at tray size and on light or dark taskbars. The version line
  in the main window reads "0.291 Clix 9".
- Updates: G-Helper's own updater now offers new Clix builds of the same official version (before, a Clix
  rebuild under v0.291 was never offered, because the version number didn't change). The prompt names the
  Clix build, and skipping one doesn't hide the next.
- Startup and charge-limit tasks: the Clix build now uses its own task names ("GHelperClix_..." and
  "GHelperClixCharge"). Before, it treated official G-Helper's "GHelper" task as its own, so after an update
  it could delete that task and point a new, enabled one at the Clix exe. Official G-Helper's tasks are never
  touched now.
- Crash safety: if G-Helper crashes in Clix code, it restarts at once with the Clix keyboard support off for
  this build (everything else keeps working, and the keyboard keeps its stored Fn+W colours). Only if the
  Clix build can't start at all, twice in a row, does it start the official G-Helper for that session
  (found from its own disabled startup task, or "clix_fallback_exe" in config.json). Details: clix/README.md.
- Ready for the next official G-Helper: upstream moved its per-key grid into a shared KeyGrid after v0.291,
  which would have broken the next Clix build. The patch now supports both layouts.
- Builds: every Clix change merged into the clix branch is built and released automatically when its build
  number goes up, and a daily check builds the patch against upstream's development code, so a breaking
  upstream change is caught before it's released.

## Clix build 8
- Live lighting: while G-Helper runs, the Clix shows G-Helper's colours directly, like the vendor app's
  online mode. Changes appear instantly (as fast as the laptop keyboard), with no flash and nothing written
  per change. A few seconds after the last change the colours are also stored in Fn+W, so the keyboard keeps
  them when G-Helper isn't running. Set `clix_live` to 0 in config.json to go back to stored-only (build 7).
- Sync with laptop now copies the laptop's effect, not just its colour: Breathe, Color Cycle, Rainbow,
  Strobe and Star are drawn on the Clix, restarting with the laptop's so they line up. Streamed laptop modes
  (Ambient, Heatmap, Battery, GPU, Audio, Gradient) show their live colour too.
- New Clix modes in the keyboard window: Breathing (colour 1 to colour 2), Colour cycle and Rainbow wave.
  Animation Speed is back, but only for those three; "Turn off when the display turns off" sits below it.
- Before display off, sleep, shutdown or quitting G-Helper, it leaves live mode itself and switches to Fn+R
  (lights off) or Fn+W (your stored colours), so there is no blue breathing. If live mode fails three times
  in a row it turns itself off for the session and the stored layers are used as in build 7.

## Clix build 7
- Lights on/off is now instant (sleep, display off, Awake/Battery ticks). Fn+R is prepared once in the
  background (normal keys, black lighting), and off is just a switch to it. Set `clix_dark_layer` to 0 to
  turn this off.
- New: "Turn off when the display turns off" (on by default), in the keyboard window where Animation
  Speed was. Animation Speed is gone, since every Clix mode here is solid.
- Modes are now called "Single colour" and "Per key".
- Sync with laptop follows every laptop lighting change (it missed some before). The colour swatch shows
  the laptop's colour while synced, the paint row hides, and the greyed-out mode box no longer looks clickable.

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
