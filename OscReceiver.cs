using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace BYEFrameBlink
{
    /// <summary>Where eye data comes from. Only one source drives the game at a time (see OscReceiver's priority chain).</summary>
    internal enum EyeSource
    {
        None = -1,
        Sim = 0,       // /bye/lid, /bye/gaze from local test tooling; always top priority
        FrameEyeOsc,   // frameeyeosc and other VRCFT-style senders: .../FT/v2/EyeLidLeft, EyeLeftX ...
        SteamVR,       // Steam Link's built-in OSC: /avatar/parameters/LeftEyeLid (0 open .. 1 shut), LeftEyeX ...
        Babble,        // Baballonia / Babble Frame Runtime: /LeftEyeLid (openness), /LeftEyeX ... (port 8888)
        VRCFT,         // VRCFaceTracking: /avatar/parameters/v2/EyeOpenLeft, EyeLeftX, EyeY ...
    }

    /// <summary>
    /// Minimal OSC-over-UDP listener. One background thread per port stores the latest values per source; the main
    /// thread reads the active source's values via the static properties. The active source is the highest-priority
    /// one that is sending eyelid data (Plugin.SourcePriority), so two trackers are never mixed.
    /// </summary>
    internal static class OscReceiver
    {
        private sealed class SourceData
        {
            public float LeftLid = 1f, RightLid = 1f;
            public long LidTicks, AliveSinceTicks;
            public float GazeLX, GazeLY, GazeRX, GazeRY;
            public long GazeTicks;
        }

        private enum Kind { LidL, LidR, LidBoth, GazeLX, GazeLY, GazeRX, GazeRY, GazeX, GazeY }

        private const int SourceCount = 5;
        /// <summary>A higher-priority source must send for this long before it takes over from a working one.</summary>
        private const double TakeOverSeconds = 0.5;
        /// <summary>Per-source gaze scale on top of Plugin.GazeDegreesPerUnit (frameeyeosc/VRCFT: 1 = 45 degrees).</summary>
        private static readonly float[] GazeScale = { 1f, 1f, 1f, 1f, 1f };
        /// <summary>Per-source vertical sign: Steam Link's LeftEyeY/RightEyeY are + down (measured 2026-11-02 on a Steam Frame).</summary>
        private static readonly float[] GazeYSign = { 1f, 1f, -1f, 1f, 1f };

        private static readonly List<UdpClient> Clients = new List<UdpClient>();
        private static volatile bool _running;
        [ThreadStatic] private static bool _fromLoopback; // per receive thread, for the packet being handled

        private static readonly object Lock = new object();
        private static readonly SourceData[] Sources = NewSources();
        private static EyeSource _active = EyeSource.None;
        private static long _packetCount;
        private static string _lastAddress = "";

        private static string _priorityText;
        private static EyeSource[] _priority = { EyeSource.FrameEyeOsc, EyeSource.SteamVR, EyeSource.Babble, EyeSource.VRCFT };

        // Simulation channel (/bye/...)
        private static readonly float[] _simHead = new float[6];
        private static long _simHeadTicks;
        private static volatile bool _simRecenterRequested;

        private static SourceData[] NewSources()
        {
            var s = new SourceData[SourceCount];
            for (int i = 0; i < SourceCount; i++) s[i] = new SourceData();
            return s;
        }

        public static float LeftLid { get { lock (Lock) { Select(); return Current?.LeftLid ?? 1f; } } }
        public static float RightLid { get { lock (Lock) { Select(); return Current?.RightLid ?? 1f; } } }
        public static long PacketCount => Interlocked.Read(ref _packetCount);
        public static string LastAddress { get { lock (Lock) return _lastAddress; } }

        public static EyeSource ActiveSource { get { lock (Lock) { Select(); return _active; } } }

        /// <summary>Active source's name while it is sending, or null when no source has fresh eyelid data.</summary>
        public static string ActiveSourceLabel
        {
            get
            {
                lock (Lock)
                {
                    Select();
                    if (_active == EyeSource.None || Age(LiveTicks(_active)) >= Timeout) return null;
                    return Label(_active);
                }
            }
        }

        public static string Label(EyeSource s)
        {
            switch (s)
            {
                case EyeSource.Sim: return "test";
                case EyeSource.FrameEyeOsc: return "frameeyeosc";
                case EyeSource.SteamVR: return "SteamVR";
                case EyeSource.Babble: return "Babble";
                case EyeSource.VRCFT: return "VRCFT";
                default: return "none";
            }
        }

        public static double SecondsSinceLidData { get { lock (Lock) { Select(); return Current == null ? double.PositiveInfinity : Age(Current.LidTicks); } } }

        public static double SecondsSinceGazeData { get { lock (Lock) { Select(); return Current == null ? double.PositiveInfinity : Age(Current.GazeTicks); } } }

        /// <summary>Average of both eyes' gaze from the active source, in VRCFT units (1 = 45 degrees, +x right, +y up).</summary>
        public static void GetGaze(out float x, out float y)
        {
            lock (Lock)
            {
                Select();
                var d = Current;
                if (d == null) { x = y = 0f; return; }
                float k = GazeScale[(int)_active];
                x = (d.GazeLX + d.GazeRX) * 0.5f * k;
                y = (d.GazeLY + d.GazeRY) * 0.5f * k * GazeYSign[(int)_active];
            }
        }

        private static SourceData Current => _active == EyeSource.None ? null : Sources[(int)_active];

        private static double Timeout => Plugin.DataTimeoutSeconds?.Value ?? 1f;

        private static double Age(long ticks) =>
            ticks == 0 ? double.PositiveInfinity : (DateTime.UtcNow.Ticks - ticks) / (double)TimeSpan.TicksPerSecond;

        /// <summary>
        /// When a source last sent data that counts as "it's working": eyelids, since blinks are what the mod runs on.
        /// The test channel also counts gaze, as the gaze tests send no lids. Caller holds Lock.
        /// </summary>
        private static long LiveTicks(EyeSource s)
        {
            var d = Sources[(int)s];
            return s == EyeSource.Sim ? Math.Max(d.LidTicks, d.GazeTicks) : d.LidTicks;
        }

        /// <summary>Pick the active source (caller holds Lock).</summary>
        private static void Select()
        {
            RefreshPriority();
            double timeout = Timeout;
            bool activeAlive = _active != EyeSource.None && Age(LiveTicks(_active)) < timeout;

            EyeSource next = _active;
            // Sim first (it never needs to wait), then the configured chain.
            for (int i = -1; i < _priority.Length; i++)
            {
                EyeSource s = i < 0 ? EyeSource.Sim : _priority[i];
                if (s == _active) break; // nothing above the active one is ready
                if (Age(LiveTicks(s)) >= timeout) continue;
                if (activeAlive && s != EyeSource.Sim && Age(Sources[(int)s].AliveSinceTicks) < TakeOverSeconds) continue;
                next = s;
                break;
            }
            if (next == _active && !activeAlive)
            {
                // Active one went quiet (or was dropped from the chain): fall back to whoever is still sending.
                foreach (var s in _priority)
                    if (Age(LiveTicks(s)) < timeout) { next = s; break; }
            }
            if (next == _active && _active != EyeSource.None && _active != EyeSource.Sim && Array.IndexOf(_priority, _active) < 0)
                next = EyeSource.None; // removed from Source Priority

            if (next == _active) return;
            string why = _active == EyeSource.None ? "first data" : activeAlive ? Label(_active) + " outranked" : Label(_active) + " went quiet";
            _active = next;
            Plugin.Log?.LogInfo($"Eye source: {Label(next)} ({why}).");
        }

        private static void RefreshPriority()
        {
            string text = Plugin.SourcePriority?.Value;
            if (text == null || ReferenceEquals(text, _priorityText)) return;
            _priorityText = text;
            var list = new List<EyeSource>();
            foreach (string raw in text.Split(','))
            {
                string name = raw.Trim().ToLowerInvariant();
                if (name.Length == 0) continue;
                EyeSource s;
                switch (name)
                {
                    case "frameeyeosc": s = EyeSource.FrameEyeOsc; break;
                    case "steamvr": case "steamlink": s = EyeSource.SteamVR; break;
                    case "babble": s = EyeSource.Babble; break;
                    case "vrcft": case "vrcfacetracking": s = EyeSource.VRCFT; break;
                    default: Plugin.Log?.LogWarning($"Source Priority: unknown source '{raw.Trim()}' ignored."); continue;
                }
                if (!list.Contains(s)) list.Add(s);
            }
            _priority = list.ToArray();
        }

        public static bool TryGetSimHead(out float[] pose)
        {
            pose = new float[6];
            lock (Lock)
            {
                if (_simHeadTicks == 0) return false;
                Array.Copy(_simHead, pose, 6);
                return true;
            }
        }

        public static bool ConsumeRecenterRequest()
        {
            if (!_simRecenterRequested) return false;
            _simRecenterRequested = false;
            return true;
        }

        public static void Start(int port, string extraPorts)
        {
            _running = true;
            Open(port, true);
            foreach (string p in (extraPorts ?? "").Split(',', ';', ' '))
            {
                if (p.Trim().Length == 0) continue;
                if (int.TryParse(p.Trim(), out int n) && n > 0 && n < 65536 && n != port) Open(n, false);
                else if (n != port) Plugin.Log.LogWarning($"Extra Ports: '{p.Trim()}' is not a port number.");
            }
        }

        private static void Open(int port, bool main)
        {
            try
            {
                var client = new UdpClient(new IPEndPoint(IPAddress.Any, port));
                Clients.Add(client);
                new Thread(() => Run(client)) { IsBackground = true, Name = "BYEFrameBlink OSC " + port }.Start();
                Plugin.Log.LogInfo($"Listening for OSC on UDP port {port}.");
            }
            catch (Exception e)
            {
                if (main) Plugin.Log.LogError($"Failed to open UDP port {port} for OSC (is another app using it?): {e.Message}");
                else Plugin.Log.LogWarning($"Couldn't open extra OSC port {port} (is another app using it?): {e.Message}");
            }
        }

        public static void Stop()
        {
            _running = false;
            foreach (var c in Clients) { try { c.Close(); } catch { } }
        }

        private static void Run(UdpClient client)
        {
            var remote = new IPEndPoint(IPAddress.Any, 0);
            while (_running)
            {
                try
                {
                    byte[] data = client.Receive(ref remote);
                    // Regression tests drive the plugin from localhost; ignore the real headset (LAN) meanwhile.
                    if (Plugin.TestMode.Value && !IPAddress.IsLoopback(remote.Address)) continue;
                    _fromLoopback = IPAddress.IsLoopback(remote.Address);
                    Interlocked.Increment(ref _packetCount);
                    HandlePacket(data, 0, data.Length);
                }
                catch (SocketException) { if (!_running) return; }
                catch (ObjectDisposedException) { return; }
                catch (Exception e) { Plugin.Log.LogWarning($"OSC parse error: {e.Message}"); }
            }
        }

        private static void HandlePacket(byte[] data, int offset, int length)
        {
            if (length < 4) return;
            if (data[offset] == (byte)'#')
            {
                // "#bundle\0" + 8-byte timetag, then [int32 size][element] ...
                int pos = offset + 16;
                int end = offset + length;
                while (pos + 4 <= end)
                {
                    int size = ReadInt(data, pos);
                    pos += 4;
                    if (size <= 0 || pos + size > end) return;
                    HandlePacket(data, pos, size);
                    pos += size;
                }
                return;
            }
            HandleMessage(data, offset, length);
        }

        private static void HandleMessage(byte[] data, int offset, int length)
        {
            int end = offset + length;
            int pos = offset;
            string address = ReadString(data, ref pos, end);
            if (address == null || pos >= end) return;
            string tags = ReadString(data, ref pos, end);
            if (tags == null || tags.Length == 0 || tags[0] != ',') return;

            var args = new List<float>(6);
            var strings = new List<string>(1);
            for (int i = 1; i < tags.Length; i++)
            {
                char tag = tags[i];
                switch (tag)
                {
                    case 'f':
                        if (pos + 4 > end) return;
                        args.Add(ReadFloat(data, pos)); pos += 4; break;
                    case 'i':
                        if (pos + 4 > end) return;
                        args.Add(ReadInt(data, pos)); pos += 4; break;
                    case 'd':
                        if (pos + 8 > end) return;
                        args.Add((float)ReadDouble(data, pos)); pos += 8; break;
                    case 'T': args.Add(1f); break;
                    case 'F': args.Add(0f); break;
                    case 's':
                        strings.Add(ReadString(data, ref pos, end)); args.Add(float.NaN); break;
                    default:
                        return; // unsupported type, stop parsing this message
                }
            }

            Dispatch(address, args, strings);
        }

        private static readonly Dictionary<string, float> SeenAddresses = new Dictionary<string, float>();

        public static KeyValuePair<string, float>[] GetSeenAddresses()
        {
            lock (Lock) return new List<KeyValuePair<string, float>>(SeenAddresses).ToArray();
        }

        private static void Dispatch(string address, List<float> args, List<string> strings)
        {
            lock (Lock) _lastAddress = address;

            if (address.StartsWith("/bye/", StringComparison.Ordinal))
            {
                // Debug/test commands (invoke, cfg, goto, ...) are for local tooling only; the port is open to the LAN.
                if (!_fromLoopback) return;
                HandleSimMessage(address, args, strings);
                return;
            }

            if (args.Count < 1 || float.IsNaN(args[0])) return;

            lock (Lock) SeenAddresses[address] = args[0];

            if (!Classify(address, out EyeSource source, out Kind kind)) return;
            float v = args[0];
            if (IsLid(kind) && LidInverted(source)) v = 1f - v;
            Store(source, kind, v);
        }

        private static bool IsLid(Kind k) => k == Kind.LidL || k == Kind.LidR || k == Kind.LidBoth;

        private static bool LidInverted(EyeSource s)
        {
            switch (s)
            {
                case EyeSource.FrameEyeOsc: return Plugin.LidValuesInverted.Value;
                case EyeSource.SteamVR: return true; // Steam Link's LeftEyeLid/RightEyeLid: 0 = open, 1 = shut
                default: return false;
            }
        }

        private static void Store(EyeSource source, Kind kind, float v)
        {
            long now = DateTime.UtcNow.Ticks;
            lock (Lock)
            {
                var d = Sources[(int)source];
                switch (kind)
                {
                    case Kind.LidL: d.LeftLid = v; break;
                    case Kind.LidR: d.RightLid = v; break;
                    case Kind.LidBoth: d.LeftLid = d.RightLid = v; break;
                    case Kind.GazeLX: d.GazeLX = v; break;
                    case Kind.GazeLY: d.GazeLY = v; break;
                    case Kind.GazeRX: d.GazeRX = v; break;
                    case Kind.GazeRY: d.GazeRY = v; break;
                    case Kind.GazeX: d.GazeLX = d.GazeRX = v; break;
                    case Kind.GazeY: d.GazeLY = d.GazeRY = v; break;
                }
                if (Age(LiveTicks(source)) >= Timeout) d.AliveSinceTicks = now; // (re)started sending
                if (IsLid(kind)) d.LidTicks = now;
                else d.GazeTicks = now;
            }
        }

        /// <summary>Which source an address comes from, and what it carries. By address shape, not port.</summary>
        private static bool Classify(string address, out EyeSource source, out Kind kind)
        {
            source = EyeSource.FrameEyeOsc;
            kind = Kind.LidL;
            int slash = address.LastIndexOf('/');
            string leaf = slash >= 0 ? address.Substring(slash + 1) : address;

            // Explicitly configured addresses always mean frameeyeosc (or whatever the user pointed them at).
            if (address == Plugin.LeftLidAddress.Value) { kind = Kind.LidL; return true; }
            if (address == Plugin.RightLidAddress.Value) { kind = Kind.LidR; return true; }

            bool avatar = address.StartsWith("/avatar/parameters/", StringComparison.Ordinal);
            bool ft = address.IndexOf("/FT/", StringComparison.Ordinal) >= 0;

            if (!ft && address.StartsWith("/avatar/parameters/v2/", StringComparison.Ordinal))
            {
                source = EyeSource.VRCFT;
                switch (leaf)
                {
                    case "EyeOpenLeft": case "EyeLidLeft": kind = Kind.LidL; return true;
                    case "EyeOpenRight": case "EyeLidRight": kind = Kind.LidR; return true;
                    case "EyeLid": kind = Kind.LidBoth; return true;
                    default: return VrcftGaze(leaf, out kind);
                }
            }

            // Steam Link (under /avatar/parameters/) and Babble (bare) share the SRanipal-era names.
            if (!ft && SranipalLeaf(leaf, out kind))
            {
                source = avatar ? EyeSource.SteamVR : EyeSource.Babble;
                return true;
            }

            // frameeyeosc (/FT/v2/...) and any other VRCFT-style sender.
            source = EyeSource.FrameEyeOsc;
            if (Matches(address, Plugin.LeftLidAddress.Value)) { kind = Kind.LidL; return true; }
            if (Matches(address, Plugin.RightLidAddress.Value)) { kind = Kind.LidR; return true; }
            if (leaf == "EyeLid") { kind = Kind.LidBoth; return true; }
            return VrcftGaze(leaf, out kind);
        }

        private static bool SranipalLeaf(string leaf, out Kind kind)
        {
            switch (leaf)
            {
                case "LeftEyeLid": kind = Kind.LidL; return true;
                case "RightEyeLid": kind = Kind.LidR; return true;
                case "LeftEyeX": kind = Kind.GazeLX; return true;
                case "LeftEyeY": kind = Kind.GazeLY; return true;
                case "RightEyeX": kind = Kind.GazeRX; return true;
                case "RightEyeY": kind = Kind.GazeRY; return true;
                default: kind = Kind.LidL; return false;
            }
        }

        // Gaze, VRCFT convention: 1.0 = 45 degrees, +X right, +Y up.
        private static bool VrcftGaze(string leaf, out Kind kind)
        {
            switch (leaf)
            {
                case "EyeLeftX": kind = Kind.GazeLX; return true;
                case "EyeLeftY": kind = Kind.GazeLY; return true;
                case "EyeRightX": kind = Kind.GazeRX; return true;
                case "EyeRightY": kind = Kind.GazeRY; return true;
                case "EyeX": kind = Kind.GazeX; return true; // combined
                case "EyeY": kind = Kind.GazeY; return true;
                default: kind = Kind.LidL; return false;
            }
        }

        private static void HandleSimMessage(string address, List<float> args, List<string> strings)
        {
            switch (address)
            {
                case "/bye/lid": // f [f]: left [right] openness, 0 shut .. 1 open (never inverted)
                    if (args.Count < 1) return;
                    Store(EyeSource.Sim, Kind.LidL, args[0]);
                    Store(EyeSource.Sim, Kind.LidR, args.Count > 1 ? args[1] : args[0]);
                    break;
                case "/bye/head": // x y z (meters) pitch yaw roll (degrees)
                    if (args.Count < 6) return;
                    lock (Lock)
                    {
                        for (int i = 0; i < 6; i++) _simHead[i] = args[i];
                        _simHeadTicks = DateTime.UtcNow.Ticks;
                    }
                    break;
                case "/bye/recenter":
                    _simRecenterRequested = true;
                    break;
                case "/bye/gaze": // x y in VRCFT units (1 = 45 deg, +x right, +y up)
                    if (args.Count < 2) return;
                    Store(EyeSource.Sim, Kind.GazeX, args[0]);
                    Store(EyeSource.Sim, Kind.GazeY, args[1]);
                    break;
                default:
                    DebugCommands.Enqueue(address, strings.Count > 0 ? strings[0] : null);
                    break;
            }
        }

        private static bool Matches(string address, string configured)
        {
            if (string.IsNullOrEmpty(configured)) return false;
            if (address == configured) return true;
            int slash = configured.LastIndexOf('/');
            string leaf = slash >= 0 ? configured.Substring(slash) : "/" + configured;
            return address.EndsWith(leaf, StringComparison.Ordinal);
        }

        private static string ReadString(byte[] data, ref int pos, int end)
        {
            int start = pos;
            while (pos < end && data[pos] != 0) pos++;
            if (pos >= end) return null;
            string s = Encoding.ASCII.GetString(data, start, pos - start);
            pos++; // null terminator
            pos = start + ((pos - start + 3) & ~3); // pad to 4 bytes
            return s;
        }

        private static int ReadInt(byte[] d, int p) => (d[p] << 24) | (d[p + 1] << 16) | (d[p + 2] << 8) | d[p + 3];

        private static float ReadFloat(byte[] d, int p)
        {
            var b = new[] { d[p + 3], d[p + 2], d[p + 1], d[p] };
            return BitConverter.ToSingle(b, 0);
        }

        private static double ReadDouble(byte[] d, int p)
        {
            var b = new byte[8];
            for (int i = 0; i < 8; i++) b[i] = d[p + 7 - i];
            return BitConverter.ToDouble(b, 0);
        }
    }
}
