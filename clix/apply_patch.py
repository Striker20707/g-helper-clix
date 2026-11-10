#!/usr/bin/env python3
"""Apply the Matrix Clix (GK6X) patch to a clean G-Helper source tree.

Usage: python clix/apply_patch.py <g-helper checkout> <owner/repo of this fork>

Instead of a context diff, every hook is inserted at an anchor line that must occur exactly once.
If upstream renames, moves or duplicates an anchor, or stops calling something the integration
relies on, this script exits non-zero. The build then fails loudly and no release is published.
"""
import pathlib
import shutil
import sys

HERE = pathlib.Path(__file__).resolve().parent


def fail(msg):
    print("CLIX PATCH FAILED: " + msg)
    sys.exit(1)


def read(path):
    if not path.exists():
        fail(f"{path} not found")
    return path.read_bytes().decode("utf-8")


def write(path, text):
    path.write_bytes(text.encode("utf-8"))


def insert_after(path, anchor, line):
    s = read(path)
    n = s.count(anchor)
    if n != 1:
        fail(f"anchor found {n} times (expected 1) in {path.name}: {anchor.strip()!r}")
    nl = "\r\n" if "\r\n" in anchor or "\r\n" in s[:2000] else "\n"
    indent = anchor[: len(anchor) - len(anchor.lstrip())].replace("\r", "").replace("\n", "")
    write(path, s.replace(anchor, anchor.rstrip("\r\n") + nl + indent + line + nl))
    print(f"hooked {path.name}: after {anchor.strip()!r}")


def insert_before(path, anchor, line):
    s = read(path)
    n = s.count(anchor)
    if n != 1:
        fail(f"anchor found {n} times (expected 1) in {path.name}: {anchor.strip()!r}")
    nl = "\r\n" if "\r\n" in anchor else "\n"
    indent = anchor[: len(anchor) - len(anchor.lstrip())].replace("\r", "").replace("\n", "")
    write(path, s.replace(anchor, indent + line + nl + anchor))
    print(f"hooked {path.name}: before {anchor.strip()!r}")


def insert_block_before(path, anchor, lines):
    """Inserts several lines (already indented) before an anchor line that must appear exactly once."""
    s = read(path)
    n = s.count(anchor)
    if n != 1:
        fail(f"anchor found {n} times (expected 1) in {path.name}: {anchor.strip()!r}")
    nl = "\r\n" if "\r\n" in s[:4000] else "\n"
    write(path, s.replace(anchor, nl.join(lines) + nl + anchor))
    print(f"hooked {path.name}: {len(lines)} lines before {anchor.strip()!r}")


def replace_once(path, old, new):
    s = read(path)
    n = s.count(old)
    if n != 1:
        fail(f"text found {n} times (expected 1) in {path.name}: {old!r}")
    write(path, s.replace(old, new))
    print(f"replaced in {path.name}: {old!r}")


def require(path, text, why):
    if text not in read(path):
        fail(f"{path.name} no longer contains {text!r} ({why})")


def find_line(path, needle):
    """Return the full source line (with its newline) containing needle; must be unique."""
    lines = read(path).splitlines(keepends=True)
    hits = [l for l in lines if needle in l]
    if len(hits) != 1:
        fail(f"{len(hits)} lines contain {needle!r} in {path.name} (expected 1)")
    return hits[0]


def main():
    if len(sys.argv) != 3:
        fail("usage: apply_patch.py <g-helper dir> <owner/repo>")
    root = pathlib.Path(sys.argv[1]).resolve()
    fork = sys.argv[2]
    app = root / "app"

    # 1. New module (new files only, cannot conflict)
    dest = app / "Peripherals" / "Keyboard" / "Gk6x"
    if dest.exists():
        fail(f"{dest} already exists (upstream added its own Gk6x folder?)")
    shutil.copytree(HERE / "src" / "Gk6x", dest)
    (root / "docs").mkdir(exist_ok=True)
    shutil.copy(HERE / "docs" / "Gk6xKeyboard.md", root / "docs" / "Gk6xKeyboard.md")
    print(f"copied module to {dest}")

    K = "global::GHelper.Peripherals.Keyboard.Gk6x."
    aura = app / "USB" / "Aura.cs"
    prov = app / "Peripherals" / "PeripheralsProvider.cs"
    settings = app / "Settings.cs"
    program = app / "Program.cs"

    extra = app / "Extra.cs"

    # 2. The Clix is registered as one of G-Helper's own keyboards (Gk6xClix : AsusKeyboard), so the tile,
    #    the keyboard settings window and "Sync with laptop" are G-Helper's own. These are the members it
    #    overrides or calls; if upstream changes them the C# build fails too, this just says why earlier.
    kb = app / "Peripherals" / "Keyboard" / "AsusKeyboard.cs"
    for text, why in [
        ("public abstract class AsusKeyboard", "Gk6xClix derives from it"),
        ("protected AsusKeyboard(ushort vendorId, ushort productId, string path, byte reportId)", "Gk6xClix constructor"),
        ("protected virtual byte[]? WriteForResponse(byte[] packet)", "per-key colours are intercepted here"),
        ("public virtual bool ApplyLighting(KeyboardLightingMode mode, Color color, Color color2, AuraSpeed speed, int brightness)", "single colour"),
        ("public bool SetLedColors(Color[] colors)", "Custom (per-key) mode"),
        ("packet[1] = 0xC0;", "per-key packet layout read by Gk6xClix"),
        ("packet[2] = 0x81;", "per-key packet layout read by Gk6xClix"),
        ("packet[5 + j * 4] = LedId(map, i + j);", "per-key packet layout read by Gk6xClix"),
        ("new byte[] { reportId, 0x50, 0x55 }", "save command acknowledged by Gk6xClix"),
        ("public virtual KeyboardLightingMode[] SupportedLightingModes()", "Static + Custom"),
        ("protected virtual string? LayoutName()", "Matrix Clix 60% layout"),
        ("public virtual void Connect()", "no ASUS handshake for the Clix"),
        ("public virtual bool IsDeviceConnected()", "disconnect detection"),
        ("protected virtual void OnDisconnect()", "disconnect detection"),
        ("protected void SetDeviceReady(bool ready)", "tile becomes clickable"),
    ]:
        require(kb, text, why)
    require(app / "Peripherals" / "Keyboard" / "AuraKeyboardLayouts.cs",
            "public static readonly Dictionary<string, LayoutData> Data", "Matrix Clix layout is added at runtime")
    require(app / "Peripherals" / "Keyboard" / "AuraKeyboardLayouts.cs",
            "public record LayoutData(List<KeyDef[]> Rows, byte[] LedIds);", "layout format")
    require(prov, "public static void Connect(AsusKeyboard kb)", "registers the Clix as a keyboard")
    require(prov, "public static bool IsKeyboardAuraSync", "Sync with laptop")
    require(prov, "public static List<AsusKeyboard> ConnectedKeyboards", "checked after registering the Clix")
    require(settings, "ShowKeyboardSettings(kb);", "tile opens the shared keyboard window")
    require(app / "AppConfig.cs", "public static string GetString(string name", "used by Gk6xKeyboards")
    require(program, "public static ToastForm toast;", "used for failure notifications")

    # 3. Detection: HID device-change handler and startup
    insert_after(prov, _timer_anchor(prov), K + "Gk6xKeyboards.Detect();")
    insert_after(program, find_line(program, "PeripheralsProvider.DetectAllAsusHeadsets();"), K + "Gk6xKeyboards.Detect();")

    # 4. Power: sleep / wake (system and display, as G-Helper does for the laptop keyboard), shutdown, charger
    insert_after(settings, find_line(settings, 'Logger.WriteLine("System Suspend");'), K + "Gk6xKeyboards.OnSuspend();")
    insert_after(settings, find_line(settings, 'Logger.WriteLine("System Resume");'), K + "Gk6xKeyboards.OnResume();")
    insert_after(settings, find_line(settings, 'Logger.WriteLine("Monitor Power Off");'), K + "Gk6xKeyboards.OnDisplayOff();")
    insert_after(settings, find_line(settings, 'Logger.WriteLine("Monitor Power On");'), K + "Gk6xKeyboards.OnDisplayOn();")
    # Laptop lighting changed (every aura mode passes here): "Sync with laptop" follows it
    insert_after(app / "USB" / "Aura.cs", find_line(app / "USB" / "Aura.cs", 'SetColor2(AppConfig.Get("aura_color2"));'),
                 K + "Gk6xKeyboards.LaptopAuraChanged();")
    insert_after(program, _block_open(program, "private static void SystemEvents_SessionEnding("), K + "Gk6xKeyboards.OnShutdown();")
    # Laptop modes that stream a colour (Ambient, Heatmap, Battery, GPU, Audio...): shown live on the Clix too
    insert_before(prov, find_line(prov, "ForEachAsync(ConnectedKeyboards, kb => kb.WriteColorDirect(color));"),
                  K + "Gk6xKeyboards.StreamColor(color);")
    insert_after(program, find_line(program, "PowerLineStatus status = SystemInformation.PowerStatus.PowerLineStatus;"),
                 K + "Gk6xKeyboards.PowerChanged();")

    # 5. "Matrix Clix" row under Extra > Laptop Backlight (Awake / Sleep / Boot-Shutdown / Battery)
    for name in ("tableBacklight", "labelBacklightKeyboard", "checkAwake", "checkSleep", "checkBoot", "checkBattery"):
        require(app / "Extra.Designer.cs", f"{name} = new ", "template for the Matrix Clix power row")
    insert_before(extra, find_line(extra, "            InitTheme();"),
                  K + "Gk6xPowerRow.Add(tableBacklight, labelBacklightKeyboard, checkAwake, checkSleep, checkBoot, checkBattery);")

    # 5b. Matrix Clix extras in the shared keyboard window: drag to paint, key groups, colour picker from a key.
    #     A few internal members give the add-on access to the window's paint colour and key colours.
    kbs = app / "AsusKeyboardSettings.cs"
    for name, why in [("private Color[] keyColors", "per-key colours"), ("private Color paintColor;", "paint colour"),
                      ("private void RefreshKeyGrid()", "redraw after a group paint"), ("private KeyboardLightingMode SelectedMode()", "Custom mode check"),
                      ("buttonPaintColor.SwatchColor = c;", "paint colour swatch"), ("checkBoxSyncAura.Checked", "sync check"),
                      ("settingsChanged = true;", "save on close"),
                      ("labelAnimationSpeed", "replaced by the display option"), ("buttonLightingColor.SwatchColor", "colour swatch")]:
        require(kbs, name, why)
    insert_block_before(kbs, "        private void ButtonFillAll_Click(object? sender, EventArgs e)", [
        "        // G-Helper Clix: access for the Matrix Clix key grid extras (Gk6xClixWindow)",
        "        internal Panel ClixKeyPanel => panelKeys;",
        "        internal Control ClixFillAllButton => buttonFillAll;",
        "        internal ComboBox ClixModeCombo => comboBoxLightingMode;",
        "        internal Control ClixSpeedLabel => labelAnimationSpeed;",
        "        internal Control ClixSpeedCombo => comboBoxAnimationSpeed;",
        "        internal CheckBox ClixSyncCheck => checkBoxSyncAura;",
        "        internal GHelper.UI.RColorButton ClixLightingColorButton => buttonLightingColor;",
        "        internal GHelper.UI.RColorButton ClixPaintColorButton => buttonPaintColor;",
        "        internal bool ClixHidePaintWhenSynced;",
        "        internal AsusKeyboard ClixKeyboard => keyboard;",
        "        internal event Action? ClixVisualized;",
        "        internal Func<int, int>? ClixLayoutRows;",
        "        internal KeyboardLightingMode ClixSelectedMode => SelectedMode();",
        "        internal void ClixRelayout() => LayoutSections();",
        "        internal bool ClixCanPaint => !checkBoxSyncAura.Checked && SelectedMode() == KeyboardLightingMode.Direct;",
        "        internal Color ClixPaintColor { get => paintColor; set { paintColor = value; buttonPaintColor.SwatchColor = value; } }",
        "        internal Color ClixKeyColor(int led) => led >= 0 && led < keyColors.Length ? keyColors[led] : Color.Black;",
        "        internal void ClixPaint(IEnumerable<int> leds)",
        "        {",
        "            if (!ClixCanPaint) return;",
        "            bool changed = false;",
        "            foreach (int led in leds)",
        "                if (led >= 0 && led < keyColors.Length && keyColors[led].ToArgb() != paintColor.ToArgb()) { keyColors[led] = paintColor; changed = true; }",
        "            if (!changed) return;",
        "            settingsChanged = true;",
        "            RefreshKeyGrid();",
        "            Task.Run(() => { try { keyboard.SetLedColors(keyColors); } catch { } });",
        "        }",
        "",
    ])
    # after every refresh of the controls (mode change, sync toggle, load), let the Clix add-on adjust them
    insert_after(kbs, find_line(kbs, "            UpdatePreviewTimer();"), "ClixVisualized?.Invoke();")
    # with "Sync with laptop" on, the paint row is hidden instead of shown disabled (Clix only)
    replace_once(kbs, "bool custom = grid && SelectedMode() == KeyboardLightingMode.Direct;",
                 "bool custom = grid && SelectedMode() == KeyboardLightingMode.Direct && !(ClixHidePaintWhenSynced && checkBoxSyncAura.Checked);")
    # the rows under the lighting mode (speed only for animated modes, plus the display option) are laid out by the Clix add-on
    replace_once(kbs, "int bottom = comboBoxAnimationSpeed.Bottom + gap;",
                 "int bottom = (ClixLayoutRows?.Invoke(gap) ?? comboBoxAnimationSpeed.Bottom) + gap;")
    insert_after(kbs, find_line(kbs, "            FormClosing += AsusKeyboardSettings_FormClosing;"),
                 "if (keyboard is " + K + "Gk6xClix) " + K + "Gk6xClixWindow.Extend(this);")

    # 6. Point G-Helper's own updater at this fork's releases
    upd = app / "AutoUpdate" / "AutoUpdateControl.cs"
    replace_once(upd, '"https://api.github.com/repos/seerge/g-helper/releases/latest"',
                 f'"https://api.github.com/repos/{fork}/releases/latest"')
    replace_once(upd, '"https://github.com/seerge/g-helper/releases"', f'"https://github.com/{fork}/releases"')

    # 7. Show the name "G-Helper Clix" in the window title and tray tooltip. The exe stays GHelper.exe,
    #    because the updater and the auto-start task look for that file name.
    replace_once(settings, 'Text = "G-Helper " + (ProcessHelper.IsUserAdministrator()',
                 'Text = "G-Helper Clix " + (ProcessHelper.IsUserAdministrator()')
    replace_once(program, 'Text = "G-Helper",', 'Text = "G-Helper Clix",')

    # 8. Pink dot on the app and tray icons, so this build is easy to tell apart from official G-Helper
    icons = [app / "favicon.ico"] + [app / "Resources" / f"{n}.ico" for n in
             ("standard", "eco", "ultimate", "dark-standard", "dark-eco", "light-standard", "light-eco")]
    for ico in icons:
        if not ico.exists():
            fail(f"icon {ico.relative_to(root)} not found (upstream renamed its icons?)")
    require(app / "GHelper.csproj", "<ApplicationIcon>favicon.ico</ApplicationIcon>", "exe icon that gets the badge")
    sys.path.insert(0, str(HERE))
    import badge_icons
    for ico in icons:
        badge_icons.badge_file(ico, add_small=ico.name == "favicon.ico")

    print("CLIX PATCH APPLIED")


def _block_open(path, signature):
    """The signature line plus the '{' line after it, so a line can be inserted at the top of that method."""
    s = read(path)
    if s.count(signature) != 1:
        fail(f"{signature!r} found {s.count(signature)} times in {path.name} (expected 1)")
    i = s.index(signature)
    line_start = s.rfind("\n", 0, i) + 1
    brace = s.find("{", i)
    line_end = s.find("\n", brace) + 1
    anchor = s[line_start:line_end]
    if s[s.find("\n", i) + 1:brace].strip() != "":
        fail(f"unexpected text between {signature!r} and its opening brace in {path.name}")
    return anchor


def _timer_anchor(prov):
    """The DetectAllAsusHeadsets() call inside DeviceTimer_Elapsed (the HID device-change handler)."""
    s = read(prov)
    start = s.find("private static void DeviceTimer_Elapsed(")
    if start < 0:
        fail("DeviceTimer_Elapsed not found in PeripheralsProvider.cs")
    end = s.find("\n        }", start)
    body = s[start:end]
    key = "DetectAllAsusHeadsets();"
    if body.count(key) != 1:
        fail("DetectAllAsusHeadsets() not found exactly once in DeviceTimer_Elapsed")
    line_start = s.rfind("\n", 0, start + body.index(key)) + 1
    line_end = s.find("\n", start + body.index(key)) + 1
    anchor = s[line_start:line_end]
    if s.count(anchor) != 1:
        # same line text exists elsewhere (e.g. startup): make anchor unique by including the previous line
        prev_start = s.rfind("\n", 0, line_start - 1) + 1
        anchor = s[prev_start:line_end]
        if s.count(anchor) != 1:
            fail("could not build a unique anchor in DeviceTimer_Elapsed")
    return anchor


if __name__ == "__main__":
    main()
