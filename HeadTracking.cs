using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.XR;

namespace BYEFrameBlink
{
    /// <summary>
    /// UUVR's UuvrPoseDriver only applies InputTracking.GetLocalRotation (3DOF). We postfix its UpdateTransform
    /// and also apply the head position on the child "VrChildCamera" driver, which UUVR's RelativeMatrix /
    /// RelativeTransform / Child modes use. That driver sits under VrCameraOffset, whose local rotation is identity
    /// relative to the game camera, so tracking-space position maps straight onto the game camera's local axes.
    /// </summary>
    internal static class HeadTracking
    {
        private static Vector3 _origin;
        private static bool _hasOrigin;

        public static Transform ChildCamera { get; private set; }
        public static Vector3 LastHeadPosition { get; private set; }
        public static Quaternion LastHeadRotation { get; private set; } = Quaternion.identity;
        public static bool Patched { get; private set; }

        public static void TryPatchUuvr(Harmony harmony)
        {
            Type driver = AccessTools.TypeByName("Uuvr.UuvrPoseDriver");
            var method = driver == null ? null : AccessTools.Method(driver, "UpdateTransform");
            if (method == null)
            {
                Plugin.Log.LogWarning("UUVR's UuvrPoseDriver.UpdateTransform not found; positional tracking disabled.");
                return;
            }
            harmony.Patch(method, postfix: new HarmonyMethod(AccessTools.Method(typeof(HeadTracking), nameof(UpdateTransformPostfix))));
            Patched = true;
            Plugin.Log.LogInfo("Patched UUVR pose driver for positional tracking.");
        }

        /// <summary>
        /// Yaw applied to every head pose read through InputTracking (UUVR's pose drivers, the UI overlay camera and
        /// our aim all read it there), so "straight ahead" can be reset to wherever you're facing. Not with SimHead,
        /// whose poses AutoPlay sets directly.
        /// </summary>
        private static Quaternion _yawOffset = Quaternion.identity;

        public static void PatchRecenter(Harmony harmony)
        {
            try
            {
                harmony.Patch(AccessTools.Method(typeof(InputTracking), nameof(InputTracking.GetLocalRotation)),
                    postfix: new HarmonyMethod(AccessTools.Method(typeof(HeadTracking), nameof(GetLocalRotationPostfix))));
                harmony.Patch(AccessTools.Method(typeof(InputTracking), nameof(InputTracking.GetLocalPosition)),
                    postfix: new HarmonyMethod(AccessTools.Method(typeof(HeadTracking), nameof(GetLocalPositionPostfix))));
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not patch InputTracking for view recentering: {e.Message}");
            }
        }

        private static readonly GameManager.SceneEventHandler SceneBeginHandler = OnSceneBegin;
        private static float _nextHookCheck;

        /// <summary>GameManager clears its static events in OnDisable (e.g. back to the main menu), so re-subscribe as needed.</summary>
        public static void EnsureSceneHook()
        {
            if (Time.unscaledTime < _nextHookCheck) return;
            _nextHookCheck = Time.unscaledTime + 1f;
            Delegate current = GameManager.OnSceneBegin;
            if (current != null && Array.IndexOf(current.GetInvocationList(), SceneBeginHandler) >= 0) return;
            GameManager.OnSceneBegin = (GameManager.SceneEventHandler)Delegate.Combine(GameManager.OnSceneBegin, SceneBeginHandler);
        }

        // UUVR's pose drivers read CenterEye (node 2), our aim reads Head: offset all head/eye nodes alike.
        private static bool IsHeadNode(XRNode node) =>
            node == XRNode.Head || node == XRNode.CenterEye || node == XRNode.LeftEye || node == XRNode.RightEye;

        private static void GetLocalRotationPostfix(XRNode node, ref Quaternion __result)
        {
            if (IsHeadNode(node) && !SimHead.Active) __result = _yawOffset * __result;
        }

        private static void GetLocalPositionPostfix(XRNode node, ref Vector3 __result)
        {
            if (IsHeadNode(node) && !SimHead.Active) __result = _yawOffset * __result;
        }

        private static void OnSceneBegin()
        {
            if (Plugin.RecenterOnSceneStart.Value && !SimHead.Active) Recenter(quiet: true);
        }

        /// <summary>Makes the current facing direction (yaw only) straight ahead, and the current head position the origin.</summary>
        public static void Recenter(bool quiet = false)
        {
            if (!SimHead.Active)
            {
                Quaternion raw = Quaternion.Inverse(_yawOffset) * InputTracking.GetLocalRotation(XRNode.Head);
                _yawOffset = Quaternion.Euler(0f, -raw.eulerAngles.y, 0f);
            }
            _origin = ReadHeadPosition();
            _hasOrigin = true;
            if (!quiet) Toast.Show("View recentered");
            Plugin.Log.LogInfo($"Recentered view (yaw offset {_yawOffset.eulerAngles.y:F0}) and head position at {_origin:F3}.");
        }

        public static Vector3 ReadHeadPosition() =>
            SimHead.Active ? SimHead.Position : InputTracking.GetLocalPosition(XRNode.Head);

        public static Quaternion ReadHeadRotation() =>
            SimHead.Active ? SimHead.Rotation : InputTracking.GetLocalRotation(XRNode.Head);

        private static void UpdateTransformPostfix(MonoBehaviour __instance)
        {
            Transform t = __instance.transform;
            // Only the child driver; the parent driver is the game's own camera (Absolute mode) and must not be moved.
            if (t.name != "VrChildCamera") return;
            ChildCamera = t;

            LastHeadRotation = ReadHeadRotation();
            if (SimHead.Active) t.localRotation = LastHeadRotation; // UUVR read the real HMD via reflection only if unpatched

            if (!Plugin.PositionalTracking.Value)
            {
                t.localPosition = Vector3.zero;
                return;
            }

            Vector3 pos = ReadHeadPosition();
            LastHeadPosition = pos;
            if (!_hasOrigin)
            {
                if (pos == Vector3.zero && !SimHead.Active) return; // tracking not ready yet
                _origin = pos;
                _hasOrigin = true;
            }
            t.localPosition = (pos - _origin) * Plugin.PositionScale.Value;
        }
    }

    /// <summary>
    /// Debug-only simulated HMD pose, so tracking can be tested with SteamVR's null driver (which never moves).
    /// Driven by keyboard or by OSC "/bye/head x y z pitch yaw roll".
    /// </summary>
    internal static class SimHead
    {
        private const float MoveSpeed = 0.25f;   // m/s
        private const float TurnSpeed = 45f;     // deg/s

        private static Vector3 _pos;
        private static Vector3 _euler;

        public static bool Active => Plugin.SimulateHead.Value;
        public static Vector3 Position => _pos;
        public static Quaternion Rotation => Quaternion.Euler(_euler);

        /// <summary>Used by AutoPlay to aim.</summary>
        public static void SetRotation(Quaternion rotation) => _euler = rotation.eulerAngles;

        public static void Patch(Harmony harmony)
        {
            // UUVR reads rotation via reflection; prefixing the managed wrappers covers that path.
            var prefixPos = new HarmonyMethod(AccessTools.Method(typeof(SimHead), nameof(GetLocalPositionPrefix)));
            var prefixRot = new HarmonyMethod(AccessTools.Method(typeof(SimHead), nameof(GetLocalRotationPrefix)));
            try
            {
                harmony.Patch(AccessTools.Method(typeof(InputTracking), nameof(InputTracking.GetLocalPosition)), prefix: prefixPos);
                harmony.Patch(AccessTools.Method(typeof(InputTracking), nameof(InputTracking.GetLocalRotation)), prefix: prefixRot);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not patch InputTracking for head simulation: {e.Message}");
            }
        }

        private static bool GetLocalPositionPrefix(XRNode node, ref Vector3 __result)
        {
            if (!Active || node != XRNode.Head) return true;
            __result = _pos;
            return false;
        }

        private static bool GetLocalRotationPrefix(XRNode node, ref Quaternion __result)
        {
            if (!Active || node != XRNode.Head) return true;
            __result = Rotation;
            return false;
        }

        public static void Update()
        {
            if (!Active) return;

            if (OscReceiver.TryGetSimHead(out float[] p))
            {
                var oscPos = new Vector3(p[0], p[1], p[2]);
                var oscEuler = new Vector3(p[3], p[4], p[5]);
                // Only take OSC values when they change, so the keyboard still works afterwards.
                if (oscPos != _lastOscPos || oscEuler != _lastOscEuler)
                {
                    _pos = oscPos;
                    _euler = oscEuler;
                    _lastOscPos = oscPos;
                    _lastOscEuler = oscEuler;
                }
            }

            float dt = Time.unscaledDeltaTime;
            Vector3 move = Vector3.zero;
            if (Input.GetKey(KeyCode.J)) move.x -= 1;
            if (Input.GetKey(KeyCode.L)) move.x += 1;
            if (Input.GetKey(KeyCode.I)) move.z += 1;
            if (Input.GetKey(KeyCode.K)) move.z -= 1;
            if (Input.GetKey(KeyCode.U)) move.y -= 1;
            if (Input.GetKey(KeyCode.O)) move.y += 1;
            _pos += Quaternion.Euler(0, _euler.y, 0) * move * (MoveSpeed * dt);

            if (Input.GetKey(KeyCode.LeftArrow)) _euler.y -= TurnSpeed * dt;
            if (Input.GetKey(KeyCode.RightArrow)) _euler.y += TurnSpeed * dt;
            if (Input.GetKey(KeyCode.UpArrow)) _euler.x -= TurnSpeed * dt;
            if (Input.GetKey(KeyCode.DownArrow)) _euler.x += TurnSpeed * dt;
        }

        private static Vector3 _lastOscPos = new Vector3(float.NaN, 0, 0);
        private static Vector3 _lastOscEuler;
    }
}
