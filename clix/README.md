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

## How releases and updates work

`.github/workflows/clix-release.yml` builds official G-Helper with this patch and publishes it here.

| When | What happens |
|---|---|
| Daily | If seerge/g-helper has a new official release, it is patched, tested, built and published under the same tag (for example v0.292). |
| Push to `clix` | If `clix/CHANGELOG.md` has a higher "## Clix build N" than the published release, it is built and published. A push that doesn't raise N is only built and tested. |
| Pull request to `clix` | Built and tested against the current official release and upstream's development code. Never published; the exe is kept as a run artifact for 7 days. |
| Daily (upstream-main job) | The patch is applied to and built against seerge/g-helper's development branch, as an early warning for the next official release. |
| By hand | Actions > Clix patched release > Run workflow: optionally an upstream tag, or "force" to rebuild. |

Every step must pass: the tests in `clix/tests`, the patch (`clix/apply_patch.py` inserts each hook at an
anchor line that must appear exactly once, and checks every upstream member the integration uses), and
upstream's own release build. If anything fails, nothing is published, so the installed Clix build keeps
working and simply isn't updated. GitHub emails the failure, and an issue is opened here if issues are
enabled on the fork.

Releases are titled "G-Helper Clix vX (Clix build N)". The patched updater checks this fork's releases and
offers a download when the official version is newer, or when it's the same version with a higher Clix
build. It asks first, exactly like official G-Helper.

## Contents

| Path | Purpose |
|---|---|
| `clix/src/Gk6x/` | The new G-Helper module, copied to `app/Peripherals/Keyboard/Gk6x/` |
| `clix/apply_patch.py` | Copies the module and inserts the one-line hooks plus the updater URL |
| `clix/badge_icons.py` | Puts a purple ring around the app and tray icons; the window title and tray tooltip read "G-Helper Clix" |
| `clix/CHANGELOG.md` | Clix build history; bump the build number for every release |
| `clix/tests/` | 184 tests: CRC, byte-for-byte comparison with GK6X, fail-closed cases, replay of the real keyboard replies, live-mode packets, effects, coalescing |
| `clix/probe/Gk6xProbe.ps1` | Read-only identity probe |
| `clix/docs/Gk6xKeyboard.md` | Wire format and behaviour |

## Use

1. Download `GHelper.exe` from this fork's latest release.
2. Quit G-Helper and keep a copy of your current exe.
3. Replace the exe and start G-Helper. A "Matrix Clix" tile appears when the keyboard is plugged in.
4. Click the tile and pick a colour. The keyboard switches to its Fn+W profile and shows it.
   Press Fn+W to go back to the keyboard's own effects, and press it again to return.

## Startup tasks

The Clix build uses its own scheduled tasks, `GHelperClix_<your user id>` (start at sign-in) and
`GHelperClixCharge` (battery charge limit at boot). Official G-Helper's `GHelper...` tasks are never read,
changed or deleted by it, apart from reading the official exe's path for the fallback below. Tick or untick
"Run on Startup" in the Clix build to create or remove its tasks.

If you made Clix copies of the official tasks by hand, keep either those or the ones "Run on Startup"
creates, not both (two start-up tasks would start G-Helper twice; the second one closes the first).

## Crash safety and fallback to official G-Helper

The Clix build is used whenever it works at all:

1. **Crash in Clix code:** G-Helper restarts at once with the Clix keyboard support off ("safe mode").
   Laptop controls, fans, charge limit and the updater keep working, and the keyboard keeps its stored Fn+W
   colours. A toast says so. Safe mode lasts until the next Clix build is installed.
2. **The Clix build can't start at all** (crashed during startup twice in a row, the second time already in
   safe mode): it starts the official G-Helper for that session instead. The official exe is the one in
   official G-Helper's own startup task (even while that task is disabled), or set `"clix_fallback_exe":
   "C:\\path\\to\\GHelper.exe"` in config.json. At the next sign-in the Clix build tries again, in safe mode,
   so a fixed update can still arrive.

A start counts as good once G-Helper has run for 20 seconds or is closed normally. Opening G-Helper while it
is already running (which replaces the running copy) never counts as a failed start.

State is kept in `%AppData%\GHelper\clix-guard.txt`. Delete it to reset (for example to leave safe mode
without waiting for an update). The official exe must be kept in a different folder from the Clix one.

## Rollback

* Add `"gk6x_keyboard": 0` to `%AppData%\GHelper\config.json` to turn the integration off.
* Or go back to official G-Helper: untick "Run on Startup" in the Clix build (removes its tasks), quit it,
  then run the official exe and tick "Run on Startup" there (or re-enable its own tasks in Task Scheduler).
  Both builds share the same config.json, so all settings carry over.
* The keyboard keeps its Fn+W colour either way. The Fn+] and Fn+\ effects were never touched.
