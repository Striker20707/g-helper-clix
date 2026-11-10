# GK6X-family keyboards (USB 1EA7:0907), e.g. Matrix Clix 60%

The Matrix Clix is registered as one of G-Helper's own keyboards (`Gk6xClix : AsusKeyboard`), so it gets the
same tile and the same settings window as ROG keyboards. Code: `app/Peripherals/Keyboard/Gk6x/`.

## Keyboard window (G-Helper's own)

| Control | Effect on the Clix |
|---|---|
| Lighting Mode: Single colour + Color | Every key that colour |
| Lighting Mode: Breathing / Colour cycle / Rainbow wave | Drawn live by G-Helper, with Animation Speed (Breathing fades Color to Color 2) |
| Lighting Mode: Per key | Per-key colours: pick a colour or palette swatch, click or drag across keys, or Fill All |
| Brightness (Lighting header slider) | 0 to 100 % of the colours |
| Sync with laptop | Copies the laptop keyboard's effect and colours: Static, Breathe, Color Cycle, Rainbow, Strobe, Star; streamed modes (Ambient, Heatmap, Audio...) show their live colour |

The on-screen keyboard is the Clix's 60% layout (61 keys, from GK6X `Data/device/655491246/data/keymap.js`).
Colours apply when you let go of the colour picker or click a key.

## Live lighting (driver mode, `clix_live`, default 1)

While G-Helper runs, one background thread keeps the keyboard in driver mode, the vendor app's "online"
profile: `0C` ping, `0B 05`, the normal key table for driver mode (`16 01` x 9, so typing is unchanged) and
software lighting (`17 01 240 1 18 3 0 0 0 0 15`). Each frame is `1A 01` x 10 (132 slots x R,G,B,100 by
LocationCode) and `1A 02`. A frame takes about 54 ms (hardware test 2026-11-10: 18 fps, typing fine).
Frames go out when the colours change and at up to 20 per second while an effect animates; a ping goes out
at least every 0.9 s (the keyboard leaves driver mode by itself when the pings stop).

Nothing in a frame is stored. The stored layers stay the fallback: 3 s after the last change the colours
are written to the colour layer (Fn+W/Fn+E) on the same open handle, without switching layers, and Fn+R is
kept black. If the keyboard refuses that in driver mode, the write waits until live mode ends.

G-Helper leaves driver mode itself (selects Fn+R when the lights should be off, else the colour layer) before
display off, sleep, shutdown and exit (`ProcessExit` / `Application.ApplicationExit`). After three failures in
a row live mode is off for the session and everything works as stored-only. Animated effects show their
main colour in the stored layer.

## Power (Extra > Laptop Backlight > "Matrix Clix" row)

| Option | Config key (default) | Unticked means |
|---|---|---|
| Awake | `clix_awake` (1) | Lights off while on AC |
| Battery | `clix_awake_bat` (= Awake) | Lights off while on battery |
| Sleep | `clix_sleep` (1) | Lights off at sleep and display off, back on at wake |
| Boot/Shutdown | `clix_boot` (1) | Lights off at shutdown, so the keyboard starts dark until G-Helper runs |

"Off" is a switch to Fn+R (`0B 04`, a few ms), which G-Helper gives the normal key table and black lighting
once, in the background after the first colour write (`clix_dark_layer`, default 1). "On" switches back.
Power events skip the colour debounce. "Turn off when the display turns off" (`clix_display_off`, default 1)
is in the keyboard window; it wins over the Sleep option, because Windows turns the display off before sleeping.

Other config keys: `clix_autoselect` (1, switch to the G-Helper layer on connect and after each write),
`clix_double_buffer` (1, alternate Fn+W / Fn+E so the keyboard's wipe-before-write is never visible),
`clix_chunk_delay` (10 ms). `"gk6x_keyboard": 0` turns all Clix support off.

## Profiles and painting

The window's profile selector is enabled for the Clix: "Default" and Profile 1-5 are six saved lighting
setups in G-Helper's config (`kb_Gk6xClix_p{n}_*`). The keyboard only ever holds the current one.
In Custom mode you can drag across keys to paint them, and right-click a key (or Fill All) for groups:
this row, red keys, everything but the red keys, letters, number row, all keys, or "Use this key's colour".

## Where the lighting lives

The keyboard has four onboard profiles: 1 = main (Fn+] blue breathing and the Fn+\ reactive effects),
2 = Fn+W, 3 = Fn+E, 4 = Fn+R. Profiles 2-4 are also **key layers**, per the Matrix manual and the vendor
driver's own data (`device/655491246/data/profile_offline_1-3.json` in Matrix Driver 6.0.0.11):

| Layer | Keys it changes | Everything else |
|---|---|---|
| Fn+W | `/` Up, Right Alt Left, Menu Down, Right Ctrl Right | normal |
| Fn+E | `1`..`=` become F1-F12 | normal |
| Fn+R | U PrtSc, I ScrLk, O Pause, J Ins, K Home, L PgUp, M Del, `,` End, `.` PgDn | normal |

Fn+Q, or the same combo again, goes back to profile 1. G-Helper writes the main layer's key table into
Fn+W and Fn+E once (`21 pp 01`, then 9 x `22 pp` with 120 key values from the vendor's
`profile.json`; hardware test 2026-11-10: every reply status 00), stamped in `gk6x_keys_p2/p3`, and then
uses both for lighting. Fn+R gets the same keys and stays black (the lights-off layer); Fn+R is never used for colours. Undo: `experiments/Gk6xFnWKeysTest.ps1 -Arrows`
(Fn+W) or Fn+Esc+Q (everything). Profile 1 cannot be written: its lighting is a fixed firmware bank (hardware test
2026-11-10: `21 01 03` and `24 01` refused with F0), and it is refused in code too.

The vendor driver writes these same per-layer lighting tables (it calls them "ModeLE") and switches
layers with the same select command, so G-Helper is doing what the official app does, in C#.

The keyboard stores the colour and the selected profile itself. Both survive unplugging and rebooting
(confirmed by the owner 2026-11-10). With `clix_live` 0, G-Helper holds no handle and runs no thread
between writes, and it does no polling.

Writes are coalesced (120 ms debounce). A write is skipped when the stamp in `gk6x_stamp_p2`
(model, firmware, colours) matches. A failed write clears the stamp. The first write after each
G-Helper launch happens anyway, in case the keyboard was reset (Fn+Esc+Q). Every write wipes the
layer first (the keyboard refuses a write without it), so a change shows a brief dark moment.
Failures appear in the settings window and the log, plus one toast per session.

Allowlisted: Matrix Clix 60%, model 655491246 (0x271200AE), firmware 0x90206111 v1.33. This is an
exact match for GK6X's "GK61-RGB-V2". Any other board only receives the three information queries.

## Wire format

Sources:
* pixeltris/GK6X @ 1348eafa (MIT)
* Vendor app behaviour observed in VladTop46/Skyloong-GK6Plus-Linux @ 9a86c850 (research only, no code copied)

Reports and replies:
* Collection: VID 1EA7, PID 0907, usage page 0xFF00, input and output reports both 65 bytes. Exactly one must exist.
* Report layout: report id 0, then a 64-byte packet: `[0] op, [1] sub, [2..5] args/offset/length, [6..7] CRC16 LE, [8..63] data`.
* CRC16/CCITT-FALSE (poly 0x1021, init 0xFFFF) over the packet with bytes 6..7 zeroed.
* A reply must match the op and pass the CRC check. Info replies must also echo the sub and carry result 1 at byte 2.
* Unrelated reports (Fn notifications, key reports in driver mode) are skipped, up to 64 per request. Timeout is 500 ms.

| Step | Bytes | Required reply status (byte 1) |
|---|---|---|
| Identify | `01 09`, `01 01`, `01 08` | result 1, valid model CRC |
| Clear profile 2 lighting | `21 02 06` | 00 or 01 (F0 = refused, abort) |
| Write | `27 02` × 22 chunks, 10 ms apart | 01 for every chunk |
| Select / reload | `0B 02` | echoes 02 |
| Live: ping | `0C` | any (00) |
| Live: driver mode | `0B 05` | echoes 05 |
| Live: key table | `16 01` x 9 (length at byte 5) | 00 or 01 |
| Live: lighting config | `17 01` + 9 data bytes | 00 or 01 |
| Live: frame | `1A 01` x 10 (length at byte 5), then `1A 02` | 00/01, then 02 |

The 1219-byte lighting blob consists of:
* A 32 × 16-byte effect table: entry 0 = {512, 1, 0, 0}, the rest -1.
* u16 type 0 and u16 704.
* 176 × u32 colour, little-endian R,G,B,A (A = FF, or 0 for black), indexed by LocationCode.

As in GK6X, the final byte is not sent.

Only opcodes 01, 0B, 21, 27, 22 (stored lighting and key table) and 0C, 16, 17, 1A (live mode) can be built
(`Gk6xProtocol.AllowedOpcodes`). Restart, macro and Fn writes, and firmware commands are unreachable.

## Hooks (inserted by `clix/apply_patch.py`)

* `Peripherals/PeripheralsProvider.cs` HID device-change handler and `Program.cs` startup: `Gk6xKeyboards.Detect()`,
  which registers the Clix through `PeripheralsProvider.Connect()`.
* `Settings.cs` system suspend/resume and display off/on, `Program.cs` session ending and power-line change: power states.
* `Extra.cs`: the "Matrix Clix" power row.
* `USB/Aura.cs` after each laptop apply: `Gk6xKeyboards.LaptopAuraChanged()`; `PeripheralsProvider.StreamKeyboardColor`:
  `Gk6xKeyboards.StreamColor()` (streamed laptop modes).
* `AsusKeyboardSettings.cs`: internal accessors for the Clix window extras, and the rows under the lighting
  mode are laid out by `Gk6xClixWindow` (speed only for animated modes, then the display option).
* `AutoUpdate/AutoUpdateControl.cs`: update check points at this fork. Window title, tray tooltip and icons are marked.

No ASUS class, packet or detector is changed. The Clix never receives ASUS packets: `Gk6xClix` intercepts them.
