# GK6X-family keyboards (USB 1EA7:0907), e.g. Matrix Clix 60%

The Matrix Clix is registered as one of G-Helper's own keyboards (`Gk6xClix : AsusKeyboard`), so it gets the
same tile and the same settings window as ROG keyboards. Code: `app/Peripherals/Keyboard/Gk6x/`.

## Keyboard window (G-Helper's own)

| Control | Effect on the Clix |
|---|---|
| Lighting Mode: Static + Color | Every key that colour |
| Lighting Mode: Custom | Per-key colours: pick a colour or palette swatch, click keys, or Fill All |
| Brightness (Lighting header slider) | 0 to 100 % of the colours |
| Sync with laptop | Uses the laptop keyboard's colour (G-Helper's shared setting for all external keyboards) |

The on-screen keyboard is the Clix's 60% layout (61 keys, from GK6X `Data/device/655491246/data/keymap.js`).
Colours apply when you let go of the colour picker or click a key.

## Power (Extra > Laptop Backlight > "Matrix Clix" row)

| Option | Config key (default) | Unticked means |
|---|---|---|
| Awake | `clix_awake` (1) | Lights off while on AC |
| Battery | `clix_awake_bat` (= Awake) | Lights off while on battery |
| Sleep | `clix_sleep` (1) | Lights off at sleep and display off, back on at wake |
| Boot/Shutdown | `clix_boot` (1) | Lights off at shutdown, so the keyboard starts dark until G-Helper runs |

"Off" is the Fn+R profile (4), written black once (`gk6x_dark_written`). Turning off or on is then just a
profile switch (`0B 04` / `0B 02`): instant, with no flash write.

Other config keys: `clix_autoselect` (1, switch to the G-Helper profile on connect), `clix_double_buffer` (1, alternate Fn+W / Fn+E so the keyboard's wipe-before-write is never visible),
`clix_chunk_delay` (10 ms). `"gk6x_keyboard": 0` turns all Clix support off.

## Where the lighting lives

The keyboard has four onboard profiles: 1 = main (Fn+] blue breathing and the Fn+\ reactive effects),
2 = Fn+W, 3 = Fn+E, 4 = Fn+R. G-Helper writes its lighting to profile 2 and, once, black to profile 4 (the off state). Profile 1
cannot be written: its lighting is a fixed firmware bank. Hardware test 2026-11-10: `21 01 03` and
`24 01` were refused with status F0. Profile 1 is refused in code.

The keyboard stores the colour and the selected profile itself. Both survive unplugging and rebooting
(confirmed by the owner 2026-11-10). G-Helper holds no handle and runs no thread between writes, and
it does no polling.

Writes are coalesced (400 ms debounce, at least 1.5 s apart). A write is skipped when the stamp in
`gk6x_last_written` (model, firmware, profile, colours) matches. A failed write clears the stamp. The
first sync after each G-Helper launch rewrites once anyway, in case the keyboard was reset
(Fn+Esc+Q). Failures appear in the settings window and the log, plus one toast per session.

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
* Unrelated reports (Fn notifications) are skipped, up to 8 per request. Timeout is 500 ms.

| Step | Bytes | Required reply status (byte 1) |
|---|---|---|
| Identify | `01 09`, `01 01`, `01 08` | result 1, valid model CRC |
| Clear profile 2 lighting | `21 02 06` | 00 or 01 (F0 = refused, abort) |
| Write | `27 02` × 22 chunks, 10 ms apart | 01 for every chunk |
| Select / reload | `0B 02` | echoes 02 |

The 1219-byte lighting blob consists of:
* A 32 × 16-byte effect table: entry 0 = {512, 1, 0, 0}, the rest -1.
* u16 type 0 and u16 704.
* 176 × u32 colour, little-endian R,G,B,A (A = FF, or 0 for black), indexed by LocationCode.

As in GK6X, the final byte is not sent.

Only opcodes 01, 0B, 21 and 27 can be built (`Gk6xProtocol.AllowedOpcodes`). Restart, realtime
lighting, key, macro and Fn writes, and firmware commands are unreachable.

## Hooks (inserted by `clix/apply_patch.py`)

* `Peripherals/PeripheralsProvider.cs` HID device-change handler and `Program.cs` startup: `Gk6xKeyboards.Detect()`,
  which registers the Clix through `PeripheralsProvider.Connect()`.
* `Settings.cs` system suspend/resume and display off/on, `Program.cs` session ending and power-line change: power states.
* `Extra.cs`: the "Matrix Clix" power row.
* `AutoUpdate/AutoUpdateControl.cs`: update check points at this fork. Window title, tray tooltip and icons are marked.

No ASUS class, packet or detector is changed. The Clix never receives ASUS packets: `Gk6xClix` intercepts them.
