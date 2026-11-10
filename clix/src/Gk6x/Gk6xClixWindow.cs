// Extras for the Matrix Clix in G-Helper's shared keyboard window (Custom mode):
//   * drag across keys to paint them (not just one click per key)
//   * right-click a key: pick its colour, paint its row, the red keys, everything but the red keys,
//     letters, number row, or the whole board; the same group menu opens from a right-click on Fill All
// Everything goes through the window's own paint path (ClixPaint), so saving, Sync and the profile
// selector behave exactly as for a click on a key.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace GHelper.Peripherals.Keyboard.Gk6x
{
    public static class Gk6xClixWindow
    {
        private static readonly HashSet<string> RedKeys = new(StringComparer.Ordinal)
        {
            "Esc", "Tab", "Caps", "Shift", "Right Shift", "Ctrl", "Right Ctrl", "Win", "Alt", "Right Alt",
            "Menu", "Fn", "Enter", "\\", "Bksp",
        };

        /// <summary>(name, row) of each on-screen key, in the window's LED order (row by row).</summary>
        private static readonly List<(string Name, int Row)> Keys =
            Gk6xLayout.Rows.SelectMany((row, r) => row.Select(k => (k.Name, r))).ToList();

        public static IEnumerable<int> Group(Func<string, int, bool> match) =>
            Keys.Select((k, i) => (k, i)).Where(t => match(t.k.Name, t.k.Row)).Select(t => t.i);

        public static IEnumerable<int> RedKeyLeds => Group((n, _) => RedKeys.Contains(n));
        public static IEnumerable<int> OtherKeyLeds => Group((n, _) => !RedKeys.Contains(n));
        public static IEnumerable<int> LetterLeds => Group((n, _) => n.Length == 1 && n[0] >= 'A' && n[0] <= 'Z');
        public static IEnumerable<int> NumberRowLeds => Group((n, r) => r == 0 && n != "Esc" && n != "Bksp");
        public static IEnumerable<int> RowLeds(int row) => Group((_, r) => r == row);

        public static void Extend(AsusKeyboardSettings form)
        {
            form.Shown += (_, _) =>
            {
                try { Attach(form); }
                catch (Exception e) { Logger.WriteLine("GK6X: window extras not added: " + e.Message); }
            };
        }

        private static void Attach(AsusKeyboardSettings form)
        {
            Panel panel = form.ClixKeyPanel;
            var keys = panel.Controls.OfType<Button>().Where(b => b.Tag is int).ToList();
            if (keys.Count != Keys.Count)
            {
                Logger.WriteLine($"GK6X: window extras skipped ({keys.Count} keys on screen, layout has {Keys.Count})");
                return;
            }

            var tip = new ToolTip();
            tip.SetToolTip(form.ClixFillAllButton, "Right-click for key groups");

            bool dragging = false;
            Button? last = null, origin = null;
            Button? menuKey = null;

            var menu = new ContextMenuStrip();
            var pick = new ToolStripMenuItem("Use this key's colour", null, (_, _) =>
            {
                if (menuKey?.Tag is int led) form.ClixPaintColor = form.ClixKeyColor(led);
            });
            var row = new ToolStripMenuItem("Paint this row", null, (_, _) =>
            {
                if (menuKey?.Tag is int led) form.ClixPaint(RowLeds(Keys[led].Row));
            });
            menu.Items.Add(pick);
            menu.Items.Add(row);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Paint red keys", null, (_, _) => form.ClixPaint(RedKeyLeds)));
            menu.Items.Add(new ToolStripMenuItem("Paint all but red keys", null, (_, _) => form.ClixPaint(OtherKeyLeds)));
            menu.Items.Add(new ToolStripMenuItem("Paint letters", null, (_, _) => form.ClixPaint(LetterLeds)));
            menu.Items.Add(new ToolStripMenuItem("Paint number row", null, (_, _) => form.ClixPaint(NumberRowLeds)));
            menu.Items.Add(new ToolStripMenuItem("Paint all keys", null, (_, _) => form.ClixPaint(Enumerable.Range(0, Keys.Count))));
            menu.Opening += (_, e) =>
            {
                if (!form.ClixCanPaint) { e.Cancel = true; return; }
                pick.Visible = row.Visible = menuKey is not null;
                menu.Items[2].Visible = menuKey is not null;
            };

            foreach (var key in keys)
            {
                key.MouseDown += (_, e) =>
                {
                    if (e.Button == MouseButtons.Right) { menuKey = key; menu.Show(key, e.Location); return; }
                    if (e.Button == MouseButtons.Left) { dragging = true; last = origin = key; }
                };
                key.MouseMove += (_, e) =>
                {
                    if (!dragging || e.Button != MouseButtons.Left || !form.ClixCanPaint) return;
                    var under = panel.GetChildAtPoint(panel.PointToClient(Cursor.Position)) as Button;
                    if (under is null || ReferenceEquals(under, last) || under.Tag is not int) return;
                    // The key the drag started on only gets its own click if the button is released over it.
                    if (origin is not null) { origin.PerformClick(); origin = null; }
                    last = under;
                    under.PerformClick();
                };
                key.MouseUp += (_, _) => { dragging = false; last = origin = null; };
            }

            form.ClixFillAllButton.MouseUp += (_, e) =>
            {
                if (e.Button != MouseButtons.Right) return;
                menuKey = null;
                menu.Show(form.ClixFillAllButton, e.Location);
            };
        }
    }
}
