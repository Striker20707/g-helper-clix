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

    # 2. Laptop colour changes (only matter when "Sync with laptop" is ticked)
    insert_after(aura, find_line(aura, 'Logger.WriteLine($"AuraMode: {Mode}");'),
                 K + "Gk6xKeyboards.OnLaptopLightingChanged();")

    # 3. Laptop brightness changes by the user (slider + hotkey), never lid/sleep/timeout/ambient
    insert_after(aura, find_line(aura, "if (brightness == 0) backlight = false;"),
                 'if (log == "Slider" || log == "HotKey") ' + K + "Gk6xKeyboards.OnLaptopLightingChanged();")
    # The brightness hook depends on these exact labels. If upstream renames them the hook would
    # silently stop working, so refuse to build instead.
    require(app / "Extra.cs", 'Aura.ApplyBrightness(sliderBrightness.Value, "Slider")', "brightness slider label")
    require(app / "Input" / "InputDispatcher.cs", 'Aura.ApplyBrightness(backlight, "HotKey")', "brightness hotkey label")
    require(app / "Input" / "InputDispatcher.cs", "public static int GetBacklight()", "used by Gk6xKeyboards.DesiredColour")
    require(app / "AppConfig.cs", "public static string GetString(string name", "used by Gk6xSettings")
    require(app / "UI" / "RColorPicker.cs", "public event Action<Color>? ColorChanged;", "used by Gk6xSettingsForm")
    require(app / "UI" / "RColorPicker.cs", "public RColorPicker(Color initial", "used by Gk6xSettingsForm")
    require(app / "Peripherals" / "IPeripheral.cs", "public void ReadBattery();", "Gk6xPeripheral implements IPeripheral")
    require(settings, "public void VisualizePeripherals()", "used to refresh the device tiles")
    require(program, "public static ToastForm toast;", "used for failure notifications")

    # 4. Device tile: include the Clix in the peripheral list and "any peripheral connected"
    insert_after(prov, find_line(prov, "l.AddRange(ConnectedHeadsets);"), "l.AddRange(" + K + "Gk6xKeyboards.Connected);")
    replace_once(prov, "return IsMouseConnected() || IsKeyboardConnected() || IsHeadsetConnected();",
                 "return IsMouseConnected() || IsKeyboardConnected() || IsHeadsetConnected() || " + K + "Gk6xKeyboards.Connected.Count > 0;")

    # 5. Detection: HID device-change handler and startup
    insert_after(prov, _timer_anchor(prov), K + "Gk6xKeyboards.Detect();")
    insert_after(program, find_line(program, "PeripheralsProvider.DetectAllAsusHeadsets();"), K + "Gk6xKeyboards.Detect();")

    # 6. Tile click opens the Clix settings window (before the ASUS-specific branches)
    insert_before(settings, find_line(settings, "if (iph.DeviceType() == PeripheralType.Mouse)"),
                  "if (iph is " + K + "Gk6xPeripheral clix) { " + K + "Gk6xSettingsForm.Toggle(clix); return; }")

    # 7. Point G-Helper's own updater at this fork's releases
    upd = app / "AutoUpdate" / "AutoUpdateControl.cs"
    replace_once(upd, '"https://api.github.com/repos/seerge/g-helper/releases/latest"',
                 f'"https://api.github.com/repos/{fork}/releases/latest"')
    replace_once(upd, '"https://github.com/seerge/g-helper/releases"', f'"https://github.com/{fork}/releases"')

    # 8. Show the name "G-Helper Clix" in the window title and tray tooltip. The exe stays GHelper.exe,
    #    because the updater and the auto-start task look for that file name.
    replace_once(settings, 'Text = "G-Helper " + (ProcessHelper.IsUserAdministrator()',
                 'Text = "G-Helper Clix " + (ProcessHelper.IsUserAdministrator()')
    replace_once(program, 'Text = "G-Helper",', 'Text = "G-Helper Clix",')

    # 9. Pink dot on the app and tray icons, so this build is easy to tell apart from official G-Helper
    icons = [app / "favicon.ico"] + [app / "Resources" / f"{n}.ico" for n in
             ("standard", "eco", "ultimate", "dark-standard", "dark-eco", "light-standard", "light-eco")]
    for ico in icons:
        if not ico.exists():
            fail(f"icon {ico.relative_to(root)} not found (upstream renamed its icons?)")
    require(app / "GHelper.csproj", "<ApplicationIcon>favicon.ico</ApplicationIcon>", "exe icon that gets the badge")
    sys.path.insert(0, str(HERE))
    import badge_icons
    for ico in icons:
        badge_icons.badge_file(ico)

    print("CLIX PATCH APPLIED")


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
