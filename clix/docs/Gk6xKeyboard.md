# GK6X-family keyboards (USB 1EA7:0907), e.g. Matrix Clix 60%

The Matrix Clix appears as its own peripheral tile in G-Helper, next to any ASUS mouse, keyboard or
headset. Clicking the tile opens its settings window. Code: `app/Peripherals/Keyboard/Gk6x/`.

## Settings window

| Setting | Config key (default) | Effect |
|---|---|---|
| Lights on | `clix_on` (1) | Off writes black to every key |
| Brightness | `clix_brightness` (100) | 0 to 100 % of the colour |
| Color | `clix_color` (white) | Colour of every key |
| Red keys | `clix_keys_mode` (0), `clix_keys_color` | Same as other keys / Off / Own color for the 15 red keycaps |
| Sync with laptop | `clix_sync` (0) | Uses the laptop's `aura_color` and backlight level (0/1/2/3 = 0/33/66/100 %) instead of the Clix's own colour and brightness |
| Switch to the G-Helper profile (Fn+W) when it connects | `clix_autoselect` (1) | Sends `0B 02` on connect and after every write |
| Apply now | | Rewrites the lighting and switches to Fn+W |

`"gk6x_keyboard": 0` in `%AppData%\GHelper\config.json` turns all Clix support off.

Red keys (LocationCode from GK6X `Data/device/655491246/data/keymap.js`): Esc 0, Backspace 14, Tab 22,
`\` 36, Caps Lock 44, Enter 58, Left Shift 66, Right Shift 80, Left Ctrl 88, Win 89, Left Alt 90,
Right Alt 98, Menu 99, Right Ctrl 100, Fn 102.

## Where the lighting lives

The keyboard has four onboard profiles: 1 = main (Fn+] blue breathing and the Fn+\ reactive effects),
2 = Fn+W, 3 = Fn+E, 4 = Fn+R. G-Helper writes lighting **only to profile 2**. Profile 1 cannot be
written: its lighting is a fixed firmware bank. Hardware test 2026-11-10: `21 01 03` and `24 01` were
refused with status F0. Profiles 1, 3 and 4 are refused in code.

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

* `Peripherals/PeripheralsProvider.cs`: Clix added to `AllPeripherals()` and `IsAnyPeripheralConnect()`; `Gk6xKeyboards.Detect()` in the HID device-change handler.
* `Program.cs`: `Detect()` at startup.
* `Settings.cs` `ButtonPeripheral_Click`: a Clix tile opens `Gk6xSettingsForm`.
* `USB/Aura.cs`: `OnLaptopLightingChanged()` after `ApplyAura` and after `ApplyBrightness` for the `"Slider"` and `"HotKey"` labels only. It only acts when Sync with laptop is ticked.
* `AutoUpdate/AutoUpdateControl.cs`: update check points at this fork.

No ASUS class, packet or detector is changed.
