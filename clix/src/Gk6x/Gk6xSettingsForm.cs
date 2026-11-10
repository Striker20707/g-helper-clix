// Settings window for the Matrix Clix, opened from its peripheral tile. Code-only (no designer file).

using GHelper.UI;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace GHelper.Peripherals.Keyboard.Gk6x
{
    public sealed class Gk6xSettingsForm : RForm
    {
        private static Gk6xSettingsForm? open;

        private readonly Gk6xPeripheral device;
        private readonly RCheckBox checkOn = new();
        private readonly Slider sliderBrightness = new();
        private readonly Label labelBrightnessValue = new();
        private readonly RButton buttonColour = new();
        private readonly RComboBox comboKeys = new();
        private readonly RButton buttonKeysColour = new();
        private readonly RCheckBox checkSync = new();
        private readonly RCheckBox checkAutoSelect = new();
        private readonly RButton buttonSelect = new();
        private readonly Label labelStatus = new();
        private bool loading = true;

        /// <summary>Opens the window, or closes it if it is already open (same behaviour as the ASUS device windows).</summary>
        public static void Toggle(Gk6xPeripheral device)
        {
            if (open is not null && !open.IsDisposed) { open.Close(); return; }
            open = new Gk6xSettingsForm(device);
            open.TopMost = AppConfig.Is("topmost");
            open.FormClosed += (_, _) => open = null;
            open.Show();
        }

        private Gk6xSettingsForm(Gk6xPeripheral device)
        {
            this.device = device;

            Text = device.GetDisplayName();
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            var table = new TableLayoutPanel
            {
                ColumnCount = 2,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(12),
                Dock = DockStyle.Fill,
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));

            Label Header(string t) => new() { Text = t, AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 8, 0, 6) };
            Label Caption(string t) => new() { Text = t, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 8, 8) };

            table.Controls.Add(Header("Lighting"), 0, 0);
            table.SetColumnSpan(table.GetControlFromPosition(0, 0), 2);

            checkOn.Text = "Lights on";
            checkOn.AutoSize = true;
            table.Controls.Add(checkOn, 0, 1);
            table.SetColumnSpan(checkOn, 2);

            table.Controls.Add(Caption("Brightness"), 0, 2);
            var brightnessRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            sliderBrightness.Min = 0;
            sliderBrightness.Max = 100;
            sliderBrightness.Step = 5;
            sliderBrightness.Size = new Size(200, 36);
            labelBrightnessValue.AutoSize = true;
            labelBrightnessValue.Anchor = AnchorStyles.Left;
            labelBrightnessValue.Margin = new Padding(6, 10, 0, 0);
            brightnessRow.Controls.Add(sliderBrightness);
            brightnessRow.Controls.Add(labelBrightnessValue);
            table.Controls.Add(brightnessRow, 1, 2);

            table.Controls.Add(Caption("Color"), 0, 3);
            buttonColour.Text = "Color";
            buttonColour.Size = new Size(120, 34);
            buttonColour.Secondary = true;
            table.Controls.Add(buttonColour, 1, 3);

            table.Controls.Add(Caption("Red keys"), 0, 4);
            var keysRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            comboKeys.DropDownStyle = ComboBoxStyle.DropDownList;
            comboKeys.Items.AddRange(new object[] { "Same as other keys", "Off", "Own color" });
            comboKeys.Size = new Size(150, 30);
            comboKeys.Margin = new Padding(0, 6, 6, 0);
            buttonKeysColour.Text = "Color";
            buttonKeysColour.Size = new Size(90, 34);
            buttonKeysColour.Secondary = true;
            keysRow.Controls.Add(comboKeys);
            keysRow.Controls.Add(buttonKeysColour);
            table.Controls.Add(keysRow, 1, 4);

            checkSync.Text = "Sync with laptop (use the laptop keyboard's color and backlight level)";
            checkSync.AutoSize = true;
            checkSync.MaximumSize = new Size(410, 0);
            table.Controls.Add(checkSync, 0, 5);
            table.SetColumnSpan(checkSync, 2);

            table.Controls.Add(Header("Keyboard profile"), 0, 6);
            table.SetColumnSpan(table.GetControlFromPosition(0, 6), 2);

            checkAutoSelect.Text = "Switch the keyboard to the G-Helper profile (Fn+W) when it connects";
            checkAutoSelect.AutoSize = true;
            checkAutoSelect.MaximumSize = new Size(410, 0);
            table.Controls.Add(checkAutoSelect, 0, 7);
            table.SetColumnSpan(checkAutoSelect, 2);

            buttonSelect.Text = "Apply now";
            buttonSelect.Size = new Size(120, 34);
            buttonSelect.Secondary = true;
            table.Controls.Add(buttonSelect, 1, 8);

            var note = new Label
            {
                Text = "G-Helper's lighting lives in the Fn+W profile. Press Fn+W on the keyboard to switch between it " +
                       "and the keyboard's own effects (Fn+] and Fn+\\ still work there). \"Apply now\" rewrites the lighting and " +
                       "switches to Fn+W, e.g. after a factory reset (Fn+Esc+Q).",
                AutoSize = true,
                MaximumSize = new Size(410, 0),
                Margin = new Padding(0, 8, 0, 0),
            };
            table.Controls.Add(note, 0, 9);
            table.SetColumnSpan(note, 2);

            labelStatus.AutoSize = true;
            labelStatus.MaximumSize = new Size(410, 0);
            labelStatus.Margin = new Padding(0, 10, 0, 0);
            table.Controls.Add(labelStatus, 0, 10);
            table.SetColumnSpan(labelStatus, 2);

            Controls.Add(table);

            InitTheme(true);
            LoadSettings();

            checkOn.CheckedChanged += (_, _) => { if (loading) return; Gk6xSettings.LightsOn = checkOn.Checked; Changed(); };
            sliderBrightness.ValueChanged += (_, _) =>
            {
                labelBrightnessValue.Text = sliderBrightness.Value + "%";
                if (loading) return;
                Gk6xSettings.Brightness = sliderBrightness.Value;
                Changed();
            };
            buttonColour.Click += ButtonColour_Click;
            comboKeys.SelectedIndexChanged += (_, _) =>
            {
                if (loading) return;
                Gk6xSettings.AccentMode = (Gk6xAccentMode)comboKeys.SelectedIndex;
                UpdateEnabled();
                Changed();
            };
            buttonKeysColour.Click += ButtonKeysColour_Click;
            checkSync.CheckedChanged += (_, _) => { if (loading) return; Gk6xSettings.SyncWithLaptop = checkSync.Checked; UpdateEnabled(); Changed(); };
            checkAutoSelect.CheckedChanged += (_, _) => { if (loading) return; Gk6xSettings.AutoSelectProfile = checkAutoSelect.Checked; };
            buttonSelect.Click += (_, _) => Gk6xKeyboards.RequestSelectProfile();

            Gk6xKeyboards.StatusChanged += OnStatusChanged;
            FormClosed += (_, _) => Gk6xKeyboards.StatusChanged -= OnStatusChanged;
            Shown += (_, _) => PlaceNextToMainWindow();

            loading = false;
        }

        private void LoadSettings()
        {
            loading = true;
            checkOn.Checked = Gk6xSettings.LightsOn;
            sliderBrightness.Value = Gk6xSettings.Brightness;
            labelBrightnessValue.Text = Gk6xSettings.Brightness + "%";
            checkSync.Checked = Gk6xSettings.SyncWithLaptop;
            checkAutoSelect.Checked = Gk6xSettings.AutoSelectProfile;
            ShowColour(buttonColour, Gk6xSettings.Colour);
            comboKeys.SelectedIndex = (int)Gk6xSettings.AccentMode;
            ShowColour(buttonKeysColour, Gk6xSettings.AccentColour);
            UpdateEnabled();
            OnStatusChanged();
            loading = false;
        }

        private void UpdateEnabled()
        {
            bool own = !checkSync.Checked;
            checkOn.Enabled = own;
            sliderBrightness.Enabled = own;
            buttonColour.Enabled = own;
            buttonKeysColour.Enabled = Gk6xSettings.AccentMode == Gk6xAccentMode.OwnColour;
        }

        private static void ShowColour(RButton button, Color c)
        {
            button.FlatAppearance.BorderColor = c;
            button.BorderColor = c;
            button.Invalidate();
        }

        private void ButtonColour_Click(object? sender, EventArgs e)
        {
            var dlg = new RColorPicker(Gk6xSettings.Colour);
            dlg.ColorChanged += c =>
            {
                Gk6xSettings.Colour = c;
                ShowColour(buttonColour, c);
                Changed();
            };
            dlg.ShowDialog(this);
        }

        private void ButtonKeysColour_Click(object? sender, EventArgs e)
        {
            var dlg = new RColorPicker(Gk6xSettings.AccentColour);
            dlg.ColorChanged += c =>
            {
                Gk6xSettings.AccentColour = c;
                ShowColour(buttonKeysColour, c);
                Changed();
            };
            dlg.ShowDialog(this);
        }

        private static void Changed() => Gk6xKeyboards.RequestSync();

        private void OnStatusChanged()
        {
            if (IsDisposed) return;
            void Update()
            {
                labelStatus.Text = string.IsNullOrEmpty(Gk6xKeyboards.Status) ? "" : "Status: " + Gk6xKeyboards.Status;
                labelStatus.ForeColor = Gk6xKeyboards.LastFailed ? colorTurbo : foreMain;
            }
            if (InvokeRequired) BeginInvoke((Action)Update); else Update();
        }

        private void PlaceNextToMainWindow()
        {
            var main = Program.settingsForm;
            if (main is null) return;
            Top = main.Top;
            Left = Math.Max(0, main.Left - Width - 5);
        }
    }
}
