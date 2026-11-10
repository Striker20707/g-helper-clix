// The Matrix Clix as one of G-Helper's own keyboards (AsusKeyboard), so it uses the same tile and the same
// settings window as ROG keyboards. Nothing here talks ASUS protocol to the Clix: every lighting call the
// shared window makes is turned into the Clix's LED table, which Gk6xKeyboards writes to the keyboard.
//
//   ApplyLighting (Static / Sync with laptop) -> every key one colour, scaled by brightness
//   SetLedColors  (Custom, per key)           -> the base class sends "C0 81" packets with (LED id, R, G, B),
//                                                already brightness-scaled. They are intercepted in
//                                                WriteForResponse and never reach the keyboard.
//   SaveLighting  ("50 55")                   -> acknowledged; the keyboard stores every write anyway.
//   ProfileSet    ("51 00")                   -> acknowledged; the profile's stored lighting is applied.
//   anything else (key bindings, battery queries) -> refused (null), nothing is sent.

using GHelper.USB;
using HidSharp;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace GHelper.Peripherals.Keyboard.Gk6x
{
    public sealed class Gk6xClix : AsusKeyboard
    {
        public const string LayoutKey = "MatrixClix";

        public Gk6xIdentity Identity { get; }
        public Gk6xSupportedModel Model { get; }
        public string DevicePath { get; }

        private readonly object slotLock = new();
        private readonly uint[] slots = new uint[Gk6xProtocol.StaticSlots];
        private bool disposed;

        static Gk6xClix()
        {
            AuraKeyboardLayouts.Data[LayoutKey] = new AuraKeyboardLayouts.LayoutData(Gk6xLayout.Rows, Gk6xLayout.LedIds);
        }

        public Gk6xClix(Gk6xIdentity identity, Gk6xSupportedModel model, string devicePath)
            : base(Gk6xProtocol.VendorId, Gk6xProtocol.ProductId, devicePath, 0x00)
        {
            Identity = identity;
            Model = model;
            DevicePath = devicePath;
            MigrateSettings();
        }

        public override string GetDisplayName() => Model.Name;

        // ---- What the shared window shows ----
        public override bool HasBattery() => false;
        public override bool HasAutoPowerOff() => false;
        public override bool HasLowBatteryWarning() => false;
        // Saved setups ("Default" + Profile 1-5) live in G-Helper's config, one set of lighting settings each,
        // using the shared window's own profile selector. Switching applies that profile's lighting.
        public override bool HasProfiles() => true;
        public override int ProfileCount() => 6;
        public override bool HasKeyBindings() => false;
        public override bool HasPerKeyRGB() => true;
        public override int MediaKeyCount() => 0;
        public override int MaxBrightness() => 100;
        public override KeyboardLightingMode[] SupportedLightingModes() => new[] { KeyboardLightingMode.Static, KeyboardLightingMode.Direct };
        public override bool SupportsColorSetting(KeyboardLightingMode mode) => mode == KeyboardLightingMode.Static;
        public override bool SupportsColor2Setting(KeyboardLightingMode mode) => false;
        public override bool SupportsColor3Setting(KeyboardLightingMode mode) => false;
        public override bool SupportsAnimationSpeed(KeyboardLightingMode mode) => false;
        protected override string? LayoutName() => LayoutKey;

        // ---- Connection: identified by Gk6xKeyboards.Detect(); no ASUS handshake ----
        public override void Connect()
        {
            RestoreProfile();
            SetDeviceReady(true);
            DeviceList.Local.Changed += DeviceList_Changed;
        }

        public override void SynchronizeDevice() => SetDeviceReady(IsDeviceConnected());

        public override bool IsDeviceConnected()
        {
            if (disposed) return false;
            try
            {
                foreach (var d in DeviceList.Local.GetHidDevices(Gk6xProtocol.VendorId, Gk6xProtocol.ProductId))
                    if (d.DevicePath == DevicePath) return true;
            }
            catch { }
            return false;
        }

        private void DeviceList_Changed(object? sender, DeviceListChangedEventArgs e) => CheckConnection();

        protected override void OnDisconnect()
        {
            Gk6xKeyboards.Disconnected(this);
            base.OnDisconnect();
        }

        public override void Dispose()
        {
            disposed = true;
            DeviceList.Local.Changed -= DeviceList_Changed;
            Gk6xKeyboards.Disconnected(this);
            base.Dispose();
        }

        // ---- Lighting ----
        public override bool ApplyLighting(KeyboardLightingMode mode, Color color, Color color2, AuraSpeed speed, int brightness)
        {
            var (r, g, b) = Gk6xProtocol.Scale(color.R, color.G, color.B, brightness);
            uint c = Gk6xProtocol.EncodeColour(r, g, b);
            lock (slotLock) Array.Fill(slots, c);
            Gk6xKeyboards.RequestWrite();
            return true;
        }

        protected override byte[]? WriteForResponse(byte[] packet)
        {
            if (packet.Length < 3) return null;
            byte[] ack = new byte[USBPacketSize()];
            Array.Copy(packet, ack, Math.Min(3, packet.Length));

            // Direct per-key: C0 81 count 00, then (led, R, G, B) per key
            if (packet[1] == 0xC0 && packet[2] == 0x81 && packet.Length >= 5)
            {
                int count = packet[3];
                lock (slotLock)
                {
                    for (int j = 0; j < count && 8 + j * 4 < packet.Length; j++)
                    {
                        int led = packet[5 + j * 4];
                        if (led < slots.Length)
                            slots[led] = Gk6xProtocol.EncodeColour(packet[6 + j * 4], packet[7 + j * 4], packet[8 + j * 4]);
                    }
                    FillUnmappedSlots();
                }
                Gk6xKeyboards.RequestWrite();
                return ack;
            }

            // Save lighting: every Clix write is already stored by the keyboard
            if (packet[1] == 0x50 && packet[2] == 0x55) return ack;

            // Profile switch (51 00 00 00 n): G-Helper sets Profile right after this returns, then the window
            // reloads; apply the new profile's stored lighting once that has happened.
            if (packet[1] == 0x51 && packet[2] == 0x00 && packet.Length >= 6)
            {
                int wanted = packet[5];
                System.Threading.Tasks.Task.Run(async () =>
                {
                    for (int i = 0; i < 20 && Profile != wanted; i++) await System.Threading.Tasks.Task.Delay(25);
                    if (Profile == wanted && !PeripheralsProvider.IsKeyboardAuraSync) ApplyStoredLighting();
                });
                return ack;
            }

            return null;
        }

        /// <summary>LEDs that have no key on screen (the side LEDs) follow the most common key colour.</summary>
        private void FillUnmappedSlots()
        {
            uint common = Gk6xLayout.LedIds.Select(i => slots[i]).GroupBy(v => v).OrderByDescending(g => g.Count()).First().Key;
            foreach (int led in Gk6xLayout.SideLeds) slots[led] = common;
        }

        internal uint[] SnapshotSlots()
        {
            lock (slotLock) return (uint[])slots.Clone();
        }

        /// <summary>
        /// One-time move of the settings from the earlier Clix window (clix_color, clix_brightness, clix_on and the
        /// red-key option) into the shared keyboard window's settings.
        /// </summary>
        private void MigrateSettings()
        {
            if (HasStoredLighting) return;
            Color main = Color.FromArgb(AppConfig.Get("clix_color", Color.White.ToArgb()));
            int brightness = AppConfig.Get("clix_on", 1) == 0 ? 0 : Math.Clamp(AppConfig.Get("clix_brightness", 100), 0, 100);
            int redMode = AppConfig.Get("clix_keys_mode", 0);
            Color red = redMode == 1 ? Color.Black : Color.FromArgb(AppConfig.Get("clix_keys_color", Color.White.ToArgb()));

            if (redMode is 1 or 2)
            {
                var accent = new HashSet<int>(Model.AccentLeds);
                StoreLighting(KeyboardLightingMode.Direct, main, Color.Black, Color.Black, AuraSpeed.Normal, brightness);
                StoreKeyColors(Gk6xLayout.LedIds.Select(id => accent.Contains(id) ? red : main).ToArray());
            }
            else
            {
                StoreLighting(KeyboardLightingMode.Static, main, Color.Black, Color.Black, AuraSpeed.Normal, brightness);
            }
            Logger.WriteLine("GK6X: settings moved to the keyboard window");
        }
    }
}
