using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace BYEFrameBlink
{
    /// <summary>
    /// Minimal OSC-over-UDP listener. Runs on a background thread and only stores the latest values;
    /// the main thread reads them via the static properties.
    /// </summary>
    internal static class OscReceiver
    {
        private static UdpClient _client;
        private static Thread _thread;
        private static volatile bool _running;
        private static bool _fromLoopback; // set by the single receive thread for the packet being handled

        private static readonly object Lock = new object();
        private static float _leftLid = 1f;
        private static float _rightLid = 1f;
        private static long _lastLidTicks;
        private static long _packetCount;
        private static string _lastAddress = "";

        // Simulation channel (/bye/...)
        private static readonly float[] _simHead = new float[6];
        private static long _simHeadTicks;
        private static volatile bool _simRecenterRequested;

        public static float LeftLid { get { lock (Lock) return _leftLid; } }
        public static float RightLid { get { lock (Lock) return _rightLid; } }
        public static long PacketCount => Interlocked.Read(ref _packetCount);
        public static string LastAddress { get { lock (Lock) return _lastAddress; } }

        public static double SecondsSinceLidData
        {
            get
            {
                long t;
                lock (Lock) t = _lastLidTicks;
                return t == 0 ? double.PositiveInfinity : (DateTime.UtcNow.Ticks - t) / (double)TimeSpan.TicksPerSecond;
            }
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

        public static void Start(int port)
        {
            try
            {
                _client = new UdpClient(new IPEndPoint(IPAddress.Any, port));
                _running = true;
                _thread = new Thread(Run) { IsBackground = true, Name = "BYEFrameBlink OSC" };
                _thread.Start();
                Plugin.Log.LogInfo($"Listening for OSC on UDP port {port}.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Failed to open UDP port {port} for OSC (is another app using it?): {e.Message}");
            }
        }

        public static void Stop()
        {
            _running = false;
            try { _client?.Close(); } catch { }
        }

        private static void Run()
        {
            var remote = new IPEndPoint(IPAddress.Any, 0);
            while (_running)
            {
                try
                {
                    byte[] data = _client.Receive(ref remote);
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

            if (TryHandleGaze(address, args[0])) return;

            bool left = Matches(address, Plugin.LeftLidAddress.Value);
            bool right = Matches(address, Plugin.RightLidAddress.Value);
            // Combined lid parameter (e.g. FT/v2/EyeLid) drives both eyes.
            if (!left && !right && address.EndsWith("/EyeLid", StringComparison.Ordinal)) left = right = true;
            if (!left && !right) return;

            float v = args[0];
            if (Plugin.LidValuesInverted.Value) v = 1f - v;
            lock (Lock)
            {
                if (left) _leftLid = v;
                if (right) _rightLid = v;
                _lastLidTicks = DateTime.UtcNow.Ticks;
            }
        }

        // Gaze, VRCFT convention: 1.0 = 45 degrees, +X right, +Y up.
        private static float _gazeLX, _gazeLY, _gazeRX, _gazeRY;
        private static long _lastGazeTicks;
        private static readonly Dictionary<string, float> SeenAddresses = new Dictionary<string, float>();

        public static double SecondsSinceGazeData
        {
            get
            {
                long t;
                lock (Lock) t = _lastGazeTicks;
                return t == 0 ? double.PositiveInfinity : (DateTime.UtcNow.Ticks - t) / (double)TimeSpan.TicksPerSecond;
            }
        }

        /// <summary>Average of both eyes' gaze, in VRCFT units.</summary>
        public static void GetGaze(out float x, out float y)
        {
            lock (Lock)
            {
                x = (_gazeLX + _gazeRX) * 0.5f;
                y = (_gazeLY + _gazeRY) * 0.5f;
            }
        }

        public static KeyValuePair<string, float>[] GetSeenAddresses()
        {
            lock (Lock) return new List<KeyValuePair<string, float>>(SeenAddresses).ToArray();
        }

        private static bool TryHandleGaze(string address, float v)
        {
            int slash = address.LastIndexOf('/');
            string leaf = slash >= 0 ? address.Substring(slash + 1) : address;
            lock (Lock)
            {
                switch (leaf)
                {
                    case "EyeLeftX": _gazeLX = v; break;
                    case "EyeLeftY": _gazeLY = v; break;
                    case "EyeRightX": _gazeRX = v; break;
                    case "EyeRightY": _gazeRY = v; break;
                    case "EyeX": _gazeLX = _gazeRX = v; break; // combined
                    case "EyeY": _gazeLY = _gazeRY = v; break;
                    default: return false;
                }
                _lastGazeTicks = DateTime.UtcNow.Ticks;
            }
            return true;
        }

        private static void HandleSimMessage(string address, List<float> args, List<string> strings)
        {
            switch (address)
            {
                case "/bye/lid": // f [f]: left [right] openness, 0 shut .. 1 open (never inverted)
                    if (args.Count < 1) return;
                    lock (Lock)
                    {
                        _leftLid = args[0];
                        _rightLid = args.Count > 1 ? args[1] : args[0];
                        _lastLidTicks = DateTime.UtcNow.Ticks;
                    }
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
                    TryHandleGaze("EyeX", args[0]);
                    TryHandleGaze("EyeY", args[1]);
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
