// "Matrix Clix" row in Extra > Laptop Backlight, next to Keyboard / Lightbar: Awake, Sleep, Boot/Shutdown, Battery.
// Built from the window's own Keyboard row so it looks the same (font, padding, theme).

using System;
using System.Windows.Forms;

namespace GHelper.Peripherals.Keyboard.Gk6x
{
    public static class Gk6xPowerRow
    {
        public static void Add(TableLayoutPanel table, Label labelTemplate, CheckBox awakeTemplate, CheckBox sleepTemplate,
                               CheckBox bootTemplate, CheckBox batteryTemplate)
        {
            if (!Gk6xSettings.Enabled || !Gk6xKeyboards.IsConnected) return;

            int row = table.RowCount;
            table.RowCount = row + 1;
            table.RowStyles.Add(new RowStyle());

            var label = new Label
            {
                Text = "Matrix Clix",
                AutoSize = true,
                Anchor = labelTemplate.Anchor,
                Font = labelTemplate.Font,
                Margin = labelTemplate.Margin,
                Padding = labelTemplate.Padding,
            };
            table.Controls.Add(label, 0, row);

            CheckBox Box(CheckBox template, bool value, Action<bool> save)
            {
                var c = new CheckBox
                {
                    Text = template.Text,
                    AutoSize = true,
                    Margin = template.Margin,
                    Padding = template.Padding,
                    UseVisualStyleBackColor = true,
                    Checked = value,
                    AccessibleName = template.Text + " Matrix Clix",
                };
                c.CheckedChanged += (_, _) => { save(c.Checked); Gk6xKeyboards.PowerChanged(); };
                return c;
            }

            table.Controls.Add(Box(awakeTemplate, Gk6xSettings.Awake, v => Gk6xSettings.Awake = v), 1, row);
            table.Controls.Add(Box(sleepTemplate, Gk6xSettings.Sleep, v => Gk6xSettings.Sleep = v), 2, row);
            table.Controls.Add(Box(bootTemplate, Gk6xSettings.BootShutdown, v => Gk6xSettings.BootShutdown = v), 3, row);
            table.Controls.Add(Box(batteryTemplate, Gk6xSettings.AwakeOnBattery, v => Gk6xSettings.AwakeOnBattery = v), 4, row);
        }
    }
}
