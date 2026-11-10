# G-Helper + Matrix Clix lighting

This fork builds the **official** G-Helper releases with one addition: the Matrix Clix 60% keyboard
shows up in G-Helper's own keyboard window with its own lighting settings (single colour, per-key,
breathing, colour cycle, rainbow wave, brightness, saved profiles, on/off per power state, sync with the
laptop keyboard's effect). While G-Helper runs the lighting is live (instant, animated); the colours are
also stored in the keyboard's Fn+W profile so they stay when G-Helper isn't running. The Fn+\ reactive
effects are left alone. The keyboard is USB 1EA7:0907, a
GK6X-family board, probed as model 655491246, firmware 0x90206111.

Fn+W, Fn+E and Fn+R come from the factory as key layers (arrow keys, F-keys, navigation keys). G-Helper
writes the normal key table into them once, uses Fn+W/Fn+E for the stored colours and Fn+R as the black
lights-off layer. See `clix/docs/Gk6xKeyboard.md`.

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
   the same tag. The release is titled "G-Helper Clix vX (Clix build N)", and its notes hold the newest
   section of `clix/CHANGELOG.md` followed by the official G-Helper notes for that version. A forced
   rebuild of the same version replaces the files and the notes in that one release.

If any step fails, nothing is published, so the patched G-Helper never offers a broken build. GitHub
sends a failure email and the patch needs updating.

## Contents

| Path | Purpose |
|---|---|
| `clix/src/Gk6x/` | The new G-Helper module, copied to `app/Peripherals/Keyboard/Gk6x/` |
| `clix/apply_patch.py` | Copies the module and inserts the one-line hooks plus the updater URL |
| `clix/badge_icons.py` | Adds a small pink dot to the app and tray icons; the window title and tray tooltip read "G-Helper Clix" |
| `clix/CHANGELOG.md` | Clix build history; bump the build number for every release |
| `clix/tests/` | 176 tests: CRC, byte-for-byte comparison with GK6X, fail-closed cases, replay of the real keyboard replies, live-mode packets, effects, coalescing |
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
