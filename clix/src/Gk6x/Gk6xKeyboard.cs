// One GK6X-family keyboard: identity handshake, one static colour into an onboard profile's lighting
// slot, and profile selection. No G-Helper dependencies; the transport is injected so tests can use a mock.

using System;
using System.Collections.Generic;

namespace GHelper.Peripherals.Keyboard.Gk6x
{
    /// <summary>Raw 65-byte report I/O for one HID collection. Implementations must enforce timeouts.</summary>
    public interface IGk6xTransport : IDisposable
    {
        string Path { get; }
        /// <summary>Writes one 65-byte output report. Throws on failure.</summary>
        void Write(byte[] report);
        /// <summary>Reads one 65-byte input report, or returns null on timeout. Throws on disconnect.</summary>
        byte[]? Read(int timeoutMs);
    }

    /// <summary>
    /// A (model, firmware) pair that has been verified on real hardware.
    /// AccentLeds: LED slots (GK6X LocationCode) under the coloured modifier keycaps, which can get their own colour.
    /// </summary>
    public sealed record Gk6xSupportedModel(uint ModelId, uint FirmwareId, string Name, int[] AccentLeds)
    {
        public Gk6xSupportedModel(uint ModelId, uint FirmwareId, string Name) : this(ModelId, FirmwareId, Name, Array.Empty<int>()) { }
    }

    public sealed class Gk6xException : Exception
    {
        public Gk6xException(string message) : base(message) { }
    }

    public sealed class Gk6xKeyboard : IDisposable
    {
        /// <summary>
        /// The red keycaps on the black/red Matrix Clix, as LocationCodes from GK6X Data/device/655491246/data/keymap.js:
        /// Esc 0, Backspace 14, Tab 22, \ 36, Caps Lock 44, Enter 58, Left Shift 66, Right Shift 80,
        /// Left Ctrl 88, Win 89, Left Alt 90, Right Alt 98, Menu 99, Right Ctrl 100, Fn 102.
        /// </summary>
        public static readonly int[] MatrixClixRedKeys = { 0, 14, 22, 36, 44, 58, 66, 80, 88, 89, 90, 98, 99, 100, 102 };

        // Declared before SupportedModels: static fields initialise in textual order.
        /// <summary>Allowlist. Writes are refused for anything not listed here.</summary>
        public static readonly List<Gk6xSupportedModel> SupportedModels = new()
        {
            // Matrix Clix 60% (mechanical, black/red). Probed 2026-11-10: model 655491246 (0x271200AE),
            // firmware 0x90206111 v1.33, buffers 15/8. Exact match for GK6X modellist.json "GK61-RGB-V2".
            // Hardware-verified 2026-11-10: lighting write to profile 2 (Fn+W) shows solid colour.
            new Gk6xSupportedModel(ModelId: 655491246, FirmwareId: 0x90206111, Name: "Matrix Clix", AccentLeds: MatrixClixRedKeys),
        };


        /// <summary>
        /// Onboard profiles: 1 = main (blue breathing, Fn+] and Fn+\ effects), 2 = Fn+W, 3 = Fn+E, 4 = Fn+R.
        /// Profiles 2-4 are also key layers (vendor data profile_offline_1-3.json): Fn+W = arrow lock
        /// (/ RAlt Menu RCtrl become arrows), Fn+E = F-key lock, Fn+R = command lock (U I O J K L M , .).
        /// G-Helper only ever writes lighting to profile 2. Profile 1 is never writable: that would erase the
        /// Fn+\ effect list. 3 and 4 are refused so a bug can't send the keyboard into a layer that changes keys.
        /// </summary>
        public const byte GHelperProfile = 2;
        public static readonly byte[] WritableLightingProfiles = { GHelperProfile };

        public const int ReplyTimeoutMs = 500;
        /// <summary>Unsolicited reports (e.g. Fn notifications) tolerated while waiting for one reply.</summary>
        public const int MaxSkippedReports = 8;
        /// <summary>Pause between lighting chunks, as the vendor app does (SLEEP_TO_WRITE, 10 ms).</summary>
        public const int ChunkDelayMs = 10;

        // Write status (reply byte 1), observed on the Matrix Clix 2026-11-10:
        //   21 xx 06 clear accepted -> 00, 27 chunk accepted -> 01, unsupported request -> F0.
        public static bool ClearAccepted(byte status) => status == 0x00 || status == 0x01;
        public static bool ChunkAccepted(byte status) => status == 0x01;

        private readonly IGk6xTransport transport;
        private readonly Action<string> log;
        private readonly Action<int> sleep;
        private readonly object ioLock = new();

        public Gk6xIdentity? Identity { get; private set; }
        public Gk6xSupportedModel? Model { get; private set; }

        public Gk6xKeyboard(IGk6xTransport transport, Action<string>? log = null, Action<int>? sleep = null)
        {
            this.transport = transport;
            this.log = log ?? (_ => { });
            this.sleep = sleep ?? (ms => System.Threading.Thread.Sleep(ms));
        }

        public string Path => transport.Path;

        /// <summary>
        /// Sends the three read-only info queries (01 09, 01 01, 01 08) and validates each reply.
        /// These are output reports, but they only request information. Throws Gk6xException on any failure.
        /// </summary>
        public Gk6xIdentity Identify()
        {
            lock (ioLock)
            {
                byte[] p = Exchange(Gk6xProtocol.OpInfo, Gk6xProtocol.InfoInitBuffers, true);
                byte bufA = p[8], bufB = p[9];
                if (bufA == 0 || bufB == 0) throw new Gk6xException("bad buffer size reply");

                p = Exchange(Gk6xProtocol.OpInfo, Gk6xProtocol.InfoFirmwareId, true);
                uint fw = BitConverter.ToUInt32(p, 8);
                byte minor = p[12], major = p[13];
                if (fw == 0 || (minor == 0 && major == 0)) throw new Gk6xException("bad firmware id reply");

                p = Exchange(Gk6xProtocol.OpInfo, Gk6xProtocol.InfoModelId, true);
                uint model = BitConverter.ToUInt32(p, 8);
                ushort modelCrc = BitConverter.ToUInt16(p, 14);
                if (model == 0) throw new Gk6xException("bad model id reply");
                if (Gk6xProtocol.Crc16(p.AsSpan(8, 6)) != modelCrc) throw new Gk6xException("model id crc mismatch");

                Identity = new Gk6xIdentity(bufA, bufB, fw, major, minor, model);
                Model = SupportedModels.Find(m => m.ModelId == model && m.FirmwareId == fw);
                log("GK6X: " + Identity + (Model is null ? " - not in allowlist, writes disabled" : " - " + Model.Name));
                return Identity;
            }
        }

        public bool IsSupported => Model is not null;

        /// <summary>
        /// Replaces the lighting of one onboard profile with a single static colour (stored in the keyboard's
        /// flash). Sequence: 21 pp 06 (clear that profile's lighting list), 27 pp x22 (one static effect).
        /// Every reply must carry an accepted status; the first refusal aborts with no retries.
        /// </summary>
        public void SetStaticColour(byte profile, byte r, byte g, byte b) => SetStaticColours(profile, (r, g, b), null);

        /// <summary>
        /// Like SetStaticColour, but the model's accent keys (AccentLeds) get their own colour when accent is given.
        /// (0,0,0) turns a group off.
        /// </summary>
        public void SetStaticColours(byte profile, (byte r, byte g, byte b) main, (byte r, byte g, byte b)? accent)
        {
            uint[] slots = new uint[Gk6xProtocol.StaticSlots];
            Array.Fill(slots, Gk6xProtocol.EncodeColour(main.r, main.g, main.b));
            if (accent is { } a && Model is not null)
            {
                uint ac = Gk6xProtocol.EncodeColour(a.r, a.g, a.b);
                foreach (int led in Model.AccentLeds)
                    if (led >= 0 && led < slots.Length) slots[led] = ac;
            }
            SetLighting(profile, slots);
        }

        /// <summary>
        /// Writes one static effect with a colour per LED slot (index = GK6X LocationCode, values from
        /// Gk6xProtocol.EncodeColour). clearFirst sends 21 pp 06 before the data, as GK6X and the vendor app do;
        /// chunkDelayMs is the pause after each chunk.
        /// </summary>
        public void SetLighting(byte profile, uint[] slots, bool clearFirst = true, int chunkDelayMs = ChunkDelayMs)
        {
            lock (ioLock)
            {
                if (Model is null) throw new Gk6xException("keyboard model not verified; refusing lighting write");
                if (Array.IndexOf(WritableLightingProfiles, profile) < 0)
                    throw new Gk6xException($"profile {profile} is not writable by G-Helper");
                if (slots.Length != Gk6xProtocol.StaticSlots) throw new Gk6xException("wrong number of LED slots");

                byte[] p;
                if (clearFirst)
                {
                    p = Exchange(Gk6xProtocol.OpLayerResetDataType, profile, false, Gk6xProtocol.DataTypeLighting);
                    if (!ClearAccepted(p[1])) throw new Gk6xException($"keyboard refused lighting clear (status 0x{p[1]:X2})");
                }

                byte[] payload = Gk6xProtocol.BuildStaticLightingPayload(slots);
                int n = 0;
                foreach (byte[] report in Gk6xProtocol.BuildChunkedReports(Gk6xProtocol.OpLayerSetLightValues, profile, payload))
                {
                    n++;
                    p = SendAndAck(report, Gk6xProtocol.OpLayerSetLightValues);
                    if (!ChunkAccepted(p[1])) throw new Gk6xException($"keyboard refused lighting chunk {n} (status 0x{p[1]:X2})");
                    if (chunkDelayMs > 0) sleep(chunkDelayMs);
                }
            }
        }

        /// <summary>Marks a keyboard identified earlier (same device path) as verified without re-sending the info queries.</summary>
        public void AssumeIdentity(Gk6xIdentity identity, Gk6xSupportedModel model)
        {
            Identity = identity;
            Model = SupportedModels.Contains(model) ? model : null;
        }

        /// <summary>Switches the keyboard to an onboard profile (same as pressing Fn+W/E/R). Reply echoes the profile.</summary>
        public void SelectProfile(byte profile)
        {
            lock (ioLock)
            {
                if (Model is null) throw new Gk6xException("keyboard model not verified; refusing profile switch");
                if (profile < 1 || profile > 4) throw new Gk6xException($"profile {profile} out of range");
                byte[] p = Exchange(Gk6xProtocol.OpSetLayer, profile, false);
                if (p[1] != profile) throw new Gk6xException($"profile switch not confirmed (reply 0x{p[1]:X2})");
            }
        }

        private byte[] Exchange(byte op, byte sub, bool isInfo, byte arg3 = 0)
        {
            byte[] report = Gk6xProtocol.BuildReport(op, sub, arg3);
            return SendAndAck(report, op, isInfo ? sub : null, isInfo);
        }

        private byte[] SendAndAck(byte[] report, byte op, byte? sub = null, bool requireResultOk = false)
        {
            transport.Write(report);
            for (int i = 0; i <= MaxSkippedReports; i++)
            {
                byte[]? reply = transport.Read(ReplyTimeoutMs);
                if (reply is null) throw new Gk6xException($"timeout waiting for reply to 0x{op:X2}");
                if (reply.Length != Gk6xProtocol.ReportLength) throw new Gk6xException($"unexpected report length {reply.Length}");
                if (reply[1] != op) continue; // unsolicited report (Fn/layer notification etc.)
                byte[]? p = Gk6xProtocol.ValidateReply(reply, op, sub, requireResultOk);
                if (p is null) throw new Gk6xException($"invalid reply to 0x{op:X2} (id/sub/result/crc)");
                return p;
            }
            throw new Gk6xException($"no reply to 0x{op:X2} after {MaxSkippedReports} unrelated reports");
        }

        public void Dispose() => transport.Dispose();
    }
}
