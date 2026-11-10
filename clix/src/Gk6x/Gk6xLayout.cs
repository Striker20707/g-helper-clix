// On-screen layout of the Matrix Clix 60% for G-Helper's keyboard window (AuraKeyboardLayouts format).
// Rows, widths and LED ids come from GK6X Build/Data/device/655491246/data/keymap.js (the vendor's own
// key positions for this model): widths = (Width + 4 px gap) / 54 px per key, LED id = LocationCode.

namespace GHelper.Peripherals.Keyboard.Gk6x
{
    public static class Gk6xLayout
    {
        public static readonly System.Collections.Generic.List<KeyDef[]> Rows = new()
        {
            new KeyDef[] { new("Esc"), new("1"), new("2"), new("3"), new("4"), new("5"), new("6"), new("7"), new("8"), new("9"), new("0"), new("-"), new("="), new("Bksp", 2.0f, 0.0f) },
            new KeyDef[] { new("Tab", 1.5f, 0.0f), new("Q"), new("W"), new("E"), new("R"), new("T"), new("Y"), new("U"), new("I"), new("O"), new("P"), new("["), new("]"), new("\\", 1.5f, 0.0f) },
            new KeyDef[] { new("Caps", 1.75f, 0.0f), new("A"), new("S"), new("D"), new("F"), new("G"), new("H"), new("J"), new("K"), new("L"), new(";"), new("'"), new("Enter", 2.25f, 0.0f) },
            new KeyDef[] { new("Shift", 2.25f, 0.0f), new("Z"), new("X"), new("C"), new("V"), new("B"), new("N"), new("M"), new(","), new("."), new("/"), new("Right Shift", 2.75f, 0.0f) },
            new KeyDef[] { new("Ctrl", 1.25f, 0.0f), new("Win", 1.25f, 0.0f), new("Alt", 1.25f, 0.0f), new("Space", 6.25f, 0.0f), new("Right Alt", 1.25f, 0.0f), new("Menu", 1.25f, 0.0f), new("Right Ctrl", 1.25f, 0.0f), new("Fn", 1.25f, 0.0f) },
        };

        /// <summary>LED slot (LocationCode) of each on-screen key, in row order.</summary>
        public static readonly byte[] LedIds =
        {
            0, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14,
            22, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36,
            44, 46, 47, 48, 49, 50, 51, 52, 53, 54, 55, 56, 58,
            66, 68, 69, 70, 71, 72, 73, 74, 75, 76, 77, 80,
            88, 89, 90, 94, 98, 99, 100, 102,
        };

        /// <summary>LED slots the vendor data lists beside the keys (Led-left/right-1..3); not drawn on screen.</summary>
        public static readonly int[] SideLeds = { 17, 18, 39, 40, 61, 62 };
    }
}
