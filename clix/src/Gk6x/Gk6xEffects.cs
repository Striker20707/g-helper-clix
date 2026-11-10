// Live lighting effects for the Matrix Clix, drawn in software and sent as frames in driver mode.
// The same effects the laptop keyboard runs in G-Helper (Breathe, Colour cycle, Rainbow, Strobe, Star), so
// "Sync with laptop" looks the same on both. Pure code: no I/O, no G-Helper dependencies (unit tested).

using System;

namespace GHelper.Peripherals.Keyboard.Gk6x
{
    public enum Gk6xEffectKind { Solid, Breathing, ColorCycle, Wave, Strobe, Star }

    /// <summary>
    /// What the keyboard should show. Solid uses Slots (one colour per LED slot, EncodeColour format,
    /// brightness included); the animated kinds use Colour1/Colour2 and Brightness. Speed: 0 slow, 1 normal, 2 fast
    /// (G-Helper's AuraSpeed values).
    /// </summary>
    public sealed record Gk6xEffect(Gk6xEffectKind Kind, uint[] Slots, (byte R, byte G, byte B) Colour1,
                                    (byte R, byte G, byte B) Colour2, int Speed, int Brightness)
    {
        public bool Animated => Kind != Gk6xEffectKind.Solid;

        public static Gk6xEffect Solid(uint[] slots) =>
            new(Gk6xEffectKind.Solid, slots, (0, 0, 0), (0, 0, 0), 1, 100);
    }

    public static class Gk6xEffects
    {
        /// <summary>Horizontal position (0 = left edge, 1 = right edge) of every LED slot, from the on-screen layout.</summary>
        private static readonly double[] SlotX = BuildSlotX();

        private static double[] BuildSlotX()
        {
            double[] x = new double[Gk6xProtocol.StaticSlots];
            for (int i = 0; i < x.Length; i++) x[i] = (i % 22) / 21.0; // GK6X LocationCode grid: 22 per row
            int n = 0;
            foreach (var row in Gk6xLayout.Rows)
            {
                double total = 0;
                foreach (var k in row) total += k.Gap + k.Width;
                double left = 0;
                foreach (var k in row)
                {
                    int led = Gk6xLayout.LedIds[n++];
                    left += k.Gap;
                    x[led] = (left + k.Width / 2) / total;
                    left += k.Width;
                }
            }
            foreach (int led in Gk6xLayout.SideLeds) x[led] = led % 2 == 1 ? 0.0 : 1.0; // 17/39/61 left, 18/40/62 right
            return x;
        }

        /// <summary>Seconds per cycle for G-Helper's Slow / Normal / Fast.</summary>
        public static double Period(Gk6xEffectKind kind, int speed)
        {
            speed = Math.Clamp(speed, 0, 2);
            return kind switch
            {
                Gk6xEffectKind.Breathing => new[] { 6.0, 4.0, 2.5 }[speed],
                Gk6xEffectKind.ColorCycle => new[] { 15.0, 9.0, 5.0 }[speed],
                Gk6xEffectKind.Wave => new[] { 6.0, 4.0, 2.0 }[speed],
                Gk6xEffectKind.Strobe => new[] { 1.0, 0.6, 0.3 }[speed],
                Gk6xEffectKind.Star => new[] { 2.4, 1.6, 0.9 }[speed],
                _ => 1.0,
            };
        }

        /// <summary>Fills slots (StaticSlots long) with the frame at time t (seconds since the effect started).</summary>
        public static void Render(Gk6xEffect e, double t, uint[] slots)
        {
            if (e.Kind == Gk6xEffectKind.Solid)
            {
                Array.Copy(e.Slots, slots, Math.Min(e.Slots.Length, slots.Length));
                return;
            }

            double period = Period(e.Kind, e.Speed);
            double phase = Frac(t / period);
            switch (e.Kind)
            {
                case Gk6xEffectKind.Breathing:
                {
                    // Smooth in and out between colour 1 and colour 2 (black = classic breathing).
                    double k = 0.5 - 0.5 * Math.Cos(phase * 2 * Math.PI);
                    Array.Fill(slots, Encode(Mix(e.Colour2, e.Colour1, k), e.Brightness));
                    break;
                }
                case Gk6xEffectKind.ColorCycle:
                    Array.Fill(slots, Encode(Hue(phase), e.Brightness));
                    break;
                case Gk6xEffectKind.Wave:
                    for (int i = 0; i < slots.Length; i++) slots[i] = Encode(Hue(Frac(SlotX[i] - phase)), e.Brightness);
                    break;
                case Gk6xEffectKind.Strobe:
                    Array.Fill(slots, Encode(phase < 0.5 ? e.Colour1 : e.Colour2, e.Brightness));
                    break;
                case Gk6xEffectKind.Star:
                    // Each key twinkles once per cycle at its own moment: colour 1 fading back to colour 2.
                    for (int i = 0; i < slots.Length; i++)
                    {
                        double p = Frac(phase + Scatter(i));
                        double k = p < 0.25 ? Math.Sin(p / 0.25 * Math.PI) : 0;
                        slots[i] = Encode(Mix(e.Colour2, e.Colour1, k), e.Brightness);
                    }
                    break;
            }
        }

        private static double Frac(double v) => v - Math.Floor(v);

        /// <summary>Fixed pseudo-random offset per slot, so the twinkle pattern does not look like a wave.</summary>
        private static double Scatter(int i)
        {
            uint h = (uint)i * 2654435761u;
            h ^= h >> 15;
            return (h % 1000) / 1000.0;
        }

        private static (byte, byte, byte) Mix((byte R, byte G, byte B) a, (byte R, byte G, byte B) b, double k) =>
            ((byte)Math.Round(a.R + (b.R - a.R) * k), (byte)Math.Round(a.G + (b.G - a.G) * k), (byte)Math.Round(a.B + (b.B - a.B) * k));

        /// <summary>Fully saturated hue, 0..1.</summary>
        public static (byte, byte, byte) Hue(double h)
        {
            h = Frac(h) * 6;
            double r = Math.Clamp(Math.Abs(h - 3) - 1, 0, 1);
            double g = Math.Clamp(2 - Math.Abs(h - 2), 0, 1);
            double b = Math.Clamp(2 - Math.Abs(h - 4), 0, 1);
            return ((byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255));
        }

        private static uint Encode((byte R, byte G, byte B) c, int brightness)
        {
            var (r, g, b) = Gk6xProtocol.Scale(c.R, c.G, c.B, brightness);
            return Gk6xProtocol.EncodeColour(r, g, b);
        }
    }
}
