using System;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx;
using UnityEngine;

namespace BYEFrameBlink
{
    /// <summary>Per-frame CSV of blink + head tracking state, for checking axes/scale/latency without a headset.</summary>
    internal static class DebugTrace
    {
        private static StreamWriter _writer;
        private static readonly StringBuilder Sb = new StringBuilder(256);

        public static void Update()
        {
            if (!Plugin.TraceCsv.Value)
            {
                Close();
                return;
            }

            if (_writer == null)
            {
                string path = Path.Combine(Paths.BepInExRootPath, "BYEFrameBlink_trace.csv");
                _writer = new StreamWriter(path, false) { AutoFlush = false };
                _writer.WriteLine("frame,time,lidL,lidR,openness,closed,hasData,blinks," +
                                  "headX,headY,headZ,headPitch,headYaw,headRoll," +
                                  "childLocalX,childLocalY,childLocalZ," +
                                  "childWorldX,childWorldY,childWorldZ,childYaw," +
                                  "gameCamX,gameCamY,gameCamZ,gameCamYaw," +
                                  "xrEnabled,xrPresent,renderX,renderY,renderZ,renderYaw,eyeX,eyeY,eyeZ,eyeYaw,gameBlinks,detectionMode");
                Camera.onPreRender += OnPreRender;
                Plugin.Log.LogInfo($"Writing trace to {path}");
            }

            Sb.Length = 0;
            Append(Time.frameCount).Append(Time.unscaledTime);
            Append(OscReceiver.LeftLid).Append(OscReceiver.RightLid).Append(BlinkState.Openness);
            Append(BlinkState.Closed ? 1 : 0).Append(BlinkState.HasData ? 1 : 0).Append(BlinkState.BlinkCount);

            Vector3 hp = HeadTracking.LastHeadPosition;
            Vector3 he = HeadTracking.LastHeadRotation.eulerAngles;
            Append(hp).Append(he);

            Transform child = HeadTracking.ChildCamera;
            if (child != null)
            {
                Append(child.localPosition).Append(child.position).Append(child.eulerAngles.y);
                Transform game = child.parent != null ? child.parent.parent : null;
                if (game != null) Append(game.position).Append(game.eulerAngles.y);
                else Append("").Append("").Append("").Append("");
            }
            else
            {
                for (int i = 0; i < 11; i++) Append("");
            }

            Append(UnityEngine.XR.XRSettings.enabled ? 1 : 0).Append(UnityEngine.XR.XRDevice.isPresent ? 1 : 0);
            // Pose of the child camera as it was when rendering last frame, and its left-eye view matrix.
            Append(_renderPos).Append(_renderYaw).Append(_eyePos).Append(_eyeYaw);
            BlinkCounter counter = BlinkCounter.instance;
            Append(BlinkConfirmedCounter.Count).Append(counter != null ? counter._detectionMode.ToString() : "");

            Sb.Length--; // trailing comma
            _writer.WriteLine(Sb.ToString());
            if (Time.frameCount % 60 == 0) _writer.Flush();
        }

        private static Vector3 _renderPos, _eyePos;
        private static float _renderYaw, _eyeYaw;

        private static void OnPreRender(Camera cam)
        {
            Transform child = HeadTracking.ChildCamera;
            if (child == null || cam.transform != child) return;
            _renderPos = child.position;
            _renderYaw = child.eulerAngles.y;
            if (cam.stereoEnabled)
            {
                Matrix4x4 camToWorld = cam.GetStereoViewMatrix(Camera.StereoscopicEye.Left).inverse;
                _eyePos = camToWorld.MultiplyPoint(Vector3.zero);
                // View space looks down -Z.
                Vector3 fwd = camToWorld.MultiplyVector(Vector3.back);
                _eyeYaw = Quaternion.LookRotation(fwd).eulerAngles.y;
            }
        }

        public static void Close()
        {
            if (_writer == null) return;
            Camera.onPreRender -= OnPreRender;
            try { _writer.Flush(); _writer.Close(); } catch { }
            _writer = null;
        }

        private static Builder Append(object v) => new Builder().Append(v);

        private static Builder Append(Vector3 v) => new Builder().Append(v);

        private struct Builder
        {
            public Builder Append(object v)
            {
                Sb.Append(Convert.ToString(v, CultureInfo.InvariantCulture)).Append(',');
                return this;
            }

            public Builder Append(float v)
            {
                Sb.Append(v.ToString("F4", CultureInfo.InvariantCulture)).Append(',');
                return this;
            }

            public Builder Append(Vector3 v) => Append(v.x).Append(v.y).Append(v.z);
        }
    }

    internal static class DebugOverlay
    {
        private static GUIStyle _style;

        public static void Draw()
        {
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 16 };
                _style.normal.textColor = Color.white;
            }

            var counter = UnityEngine.Object.FindObjectOfType<BlinkCounter>();
            string text =
                $"Before Your Frames\n" +
                $"OSC packets: {OscReceiver.PacketCount}  last: {OscReceiver.LastAddress}  source: {OscReceiver.ActiveSourceLabel ?? "none"}\n" +
                $"Lid L {OscReceiver.LeftLid:F2}  R {OscReceiver.RightLid:F2}  -> {BlinkState.Openness:F2}  (close<{Plugin.CloseThreshold.Value:F2} open>{Plugin.OpenThreshold.Value:F2})\n" +
                $"Data: {(BlinkState.HasData ? "OK" : "NONE")}   EYES {(BlinkState.Closed ? "CLOSED" : "open")}   blinks: {BlinkState.BlinkCount}  game accepted: {BlinkConfirmedCounter.Count}\n" +
                $"Game mode: {(counter != null ? counter._detectionMode.ToString() : "-")}  canBlink: {(counter != null && counter._canBlink)}\n" +
                $"Gaze: {(Gaze.Active ? $"yaw {Gaze.Degrees.x:F1} pitch {Gaze.Degrees.y:F1}" : "none (center of view)")}\n" +
                $"Head pos {HeadTracking.LastHeadPosition:F3}  rot {HeadTracking.LastHeadRotation.eulerAngles:F0}\n" +
                $"6DOF: {(HeadTracking.Patched ? (Plugin.PositionalTracking.Value ? "on" : "off") : "UUVR not found")}  sim: {SimHead.Active}\n" +
                $"[{Plugin.ForceBlinkKey.Value}] hold = eyes closed   [{Plugin.RecenterKey.Value}] recenter";

            GUI.Box(new Rect(10, 10, 720, 215), text, _style);
        }
    }
}
