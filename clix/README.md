# G-Helper + Matrix Clix lighting

This fork builds the **official** G-Helper releases with one addition: the Matrix Clix 60% keyboard
shows up as its own device tile with its own lighting settings (colour, brightness, on/off, red-keycap
colour, optional sync with the laptop keyboard). It replaces the blue breathing with static colour in
the keyboard's Fn+W profile and leaves the Fn+\ reactive effects alone. The keyboard is USB 1EA7:0907, a
GK6X-family board, probed as model 655491246, firmware 0x90206111.

Nothing else in G-Helper changes, except that its built-in updater checks this fork's releases
instead of seerge's.

## How releases are made

`.github/workflows/clix-release.yml` runs daily (and can be started by hand from the Actions tab):

1. Reads the latest release tag of `seerge/g-helper`. It stops if this fork already has that tag.
2. Runs the mock-transport tests in `clix/tests`.
3. Applies the patch with `clix/apply_patch.py`. Every hook is inserted at an anchor line that must
   appear exactly once. The script also checks that the labels and methods the integration depends on
   still exist. Any mismatch fails the run.
4. Builds with upstream's own release command and publishes `GHelper.exe` and `GHelper.zip` under
   the same tag.

If any step fails, nothing is published, so the patched G-Helper never offers a broken build. GitHub
sends a failure email and the patch needs updating.

## Contents

| Path | Purpose |
|---|---|
| `clix/src/Gk6x/` | The new G-Helper module, copied to `app/Peripherals/Keyboard/Gk6x/` |
| `clix/apply_patch.py` | Copies the module and inserts the one-line hooks plus the updater URL |
| `clix/badge_icons.py` | Adds a small pink dot to the app and tray icons; the window title and tray tooltip read "G-Helper Clix" |
| `clix/tests/` | 123 tests: CRC, byte-for-byte comparison with GK6X, fail-closed cases, replay of the real keyboard replies, coalescing |
| `clix/probe/Gk6xProbe.ps1` | Read-only identity probe |
| `clix/docs/Gk6xKeyboard.md` | Wire format and behaviour |

## Use

1. Download `GHelper.exe` from this fork's latest release.
2. Quit G-Helper and keep a copy of your current exe.
3. Replace the exe and start G-Helper. A "Matrix Clix" tile appears when the keyboard is plugged in.
4. Click the tile and pick a colour. The keyboard switches to its Fn+W profile and shows it.
   Press Fn+W to go back to the keyboard's own effects, and press it again to return.

## Rollback

* Add `"gk6x_keyboard": 0` to `%AppData%\GHelper\config.json` to turn the integration off.
* Or run the official exe again. Nothing else was installed.
* The keyboard keeps its Fn+W colour either way. The Fn+] and Fn+\ effects were never touched.
