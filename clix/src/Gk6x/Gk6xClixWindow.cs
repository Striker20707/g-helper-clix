// Extras for the Matrix Clix in G-Helper's shared keyboard window:
//   * clearer names: "Single colour" / "Per key" instead of Static / Custom, "Colour cycle", "Rainbow wave"
//   * Animation Speed only for the animated modes (and not while synced: the laptop's speed is used), and
//     below it "Turn off when the display turns off"
//   * with "Sync with laptop" on: the colour swatch shows the laptop's colour, the paint row is hidden,
//     and the disabled mode box is greyed out so it doesn't look clickable
// In Per key mode:
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

        public const string SingleColourName = "Single colour";
        public const string PerKeyName = "Per key";

        private static readonly Dictionary<KeyboardLightingMode, string> ModeNames = new()
        {
            [KeyboardLightingMode.Static] = SingleColourName,
            [KeyboardLightingMode.Breathing] = "Breathing",
            [KeyboardLightingMode.ColorCycle] = "Colour cycle",
            [KeyboardLightingMode.Wave] = "Rainbow wave",
            [KeyboardLightingMode.Direct] = PerKeyName,
        };

        public static void Extend(AsusKeyboardSettings form)
        {
            form.ClixHidePaintWhenSynced = true;
            var state = new FormState();
            form.ClixLayoutRows = gap => LayoutRows(form, state, gap);
            form.ClixVisualized += () => { try { Fixup(form, state); } catch { } };
            Action laptop = () => { try { if (form.IsHandleCreated) form.BeginInvoke(() => Fixup(form, state)); } catch { } };
            Gk6xKeyboards.LaptopAuraApplied += laptop;
            form.FormClosed += (_, _) => Gk6xKeyboards.LaptopAuraApplied -= laptop;
            form.Shown += (_, _) =>
            {
                try { RenameModes(form); AddDisplayOption(form, state); form.ClixRelayout(); Fixup(form, state); Attach(form); }
                catch (Exception e) { Logger.WriteLine("GK6X: window extras not added: " + e.Message); }
            };
        }

        private sealed class FormState
        {
            public CheckBox? DisplayOff;
            public Color? OwnColour;
            public Color? ModeForeColor;
        }

        private static void RenameModes(AsusKeyboardSettings form)
        {
            ComboBox combo = form.ClixModeCombo;
            var modes = form.ClixKeyboard.SupportedLightingModes();
            for (int i = 0; i < combo.Items.Count && i < modes.Length; i++)
                if (ModeNames.TryGetValue(modes[i], out string? name)) combo.Items[i] = name;
        }

        private static void AddDisplayOption(AsusKeyboardSettings form, FormState state)
        {
            CheckBox sync = form.ClixSyncCheck;
            Control label = form.ClixSpeedLabel;
            var box = new CheckBox
            {
                Text = "Turn off when the display turns off",
                AutoSize = true,
                Font = sync.Font,
                ForeColor = sync.ForeColor,
                BackColor = sync.BackColor,
                Margin = sync.Margin,
                Padding = sync.Padding,
                UseVisualStyleBackColor = sync.UseVisualStyleBackColor,
                Checked = Gk6xSettings.OffWithDisplay,
            };
            box.CheckedChanged += (_, _) => { Gk6xSettings.OffWithDisplay = box.Checked; Gk6xKeyboards.PowerChanged(); };
            label.Parent!.Controls.Add(box);
            state.DisplayOff = box;
        }

        /// <summary>
        /// Called from the window's LayoutSections: lays out the rows under "Sync with laptop" and returns the
        /// bottom of the last one. Speed row only for an animated mode set here; then the display option.
        /// </summary>
        private static int LayoutRows(AsusKeyboardSettings form, FormState state, int gap)
        {
            Control label = form.ClixSpeedLabel, speed = form.ClixSpeedCombo;
            bool animated = Gk6xClix.IsAnimated(form.ClixSelectedMode) && !form.ClixSyncCheck.Checked;
            label.Visible = speed.Visible = animated;
            if (state.DisplayOff is not { } box) return animated ? speed.Bottom : label.Top - gap;

            int rowTop = animated ? speed.Bottom + gap : label.Top;
            box.Left = form.ClixSyncCheck.Left;
            box.Top = rowTop + (speed.Height - box.Height) / 2;
            box.Visible = true;
            return rowTop + speed.Height;
        }

        private static void Fixup(AsusKeyboardSettings form, FormState state)
        {
            bool synced = form.ClixSyncCheck.Checked;
            ComboBox mode = form.ClixModeCombo;
            state.ModeForeColor ??= mode.ForeColor;
            mode.ForeColor = mode.Enabled ? state.ModeForeColor.Value : SystemColors.GrayText;

            // The colour swatch shows the colour the keyboard actually has.
            var swatch = form.ClixLightingColorButton;
            if (synced)
            {
                state.OwnColour ??= swatch.SwatchColor;
                swatch.SwatchColor = Color.FromArgb(255, Color.FromArgb(AppConfig.Get("aura_color")));
            }
            else if (state.OwnColour is { } own)
            {
                swatch.SwatchColor = own;
                state.OwnColour = null;
            }
            swatch.Invalidate();
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
