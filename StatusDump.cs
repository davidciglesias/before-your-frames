using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BYEFrameBlink
{
    /// <summary>
    /// "/bye/status [tag]" writes a flat JSON snapshot of the plugin + game state to BepInEx/BYEFrameBlink_status.json.
    /// tools/run_tests.ps1 asserts on it, so keep key names stable.
    /// </summary>
    internal static class StatusDump
    {
        public static void Write(string tag)
        {
            var f = new List<KeyValuePair<string, object>>();
            void Add(string k, object v) => f.Add(new KeyValuePair<string, object>(k, v));

            Add("tag", tag ?? "");
            Add("frame", Time.frameCount);
            Add("time", Time.unscaledTime);
            var scenes = new List<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++) scenes.Add(SceneManager.GetSceneAt(i).name);
            Add("scenes", string.Join(",", scenes.ToArray()));
            Add("xrEnabled", UnityEngine.XR.XRSettings.enabled);

            Add("patchedMethods", InputRedirect.PatchedMethodCount);
            Add("patchedTypes", string.Join(",", InputRedirect.PatchedTypes));
            Add("uuvrPatched", HeadTracking.Patched);

            Add("oscPackets", OscReceiver.PacketCount);
            Add("lidLeft", OscReceiver.LeftLid);
            Add("lidRight", OscReceiver.RightLid);
            Add("lidHasData", BlinkState.HasData);
            Add("openness", BlinkState.Openness);
            Add("eyesClosed", BlinkState.Closed);
            Add("pluginBlinks", BlinkState.BlinkCount);
            Add("gameBlinks", BlinkConfirmedCounter.Count);

            BlinkCounter counter = Object.FindObjectOfType<BlinkCounter>();
            Add("blinkCounterPresent", counter != null);
            Add("detectionMode", counter != null ? counter._detectionMode.ToString() : "");
            Add("canBlink", counter != null && counter._canBlink);

            Add("webcamPlaying", WebcamBlock.PlayingWebcamTextures());
            Add("trackerInitsBlocked", WebcamBlock.TrackerInitsBlocked);
            Add("webcamPlaysBlocked", WebcamBlock.PlaysBlocked);
            Add("webcamWatchersSkipped", WebcamBlock.DisconnectWatchersSkipped);

            Quaternion head = HeadTracking.ReadHeadRotation();
            Add("headPitch", head.eulerAngles.x);
            Add("headYaw", head.eulerAngles.y);
            Add("positionalTracking", Plugin.PositionalTracking.Value);
            Transform child = HeadTracking.ChildCamera;
            Add("childLocalX", child != null ? child.localPosition.x : 0f);
            Add("childLocalY", child != null ? child.localPosition.y : 0f);
            Add("childLocalZ", child != null ? child.localPosition.z : 0f);

            Quaternion unused = Gaze.LocalRotation; // sample this frame's gaze
            Add("gazeActive", Gaze.Active);
            Add("gazeYaw", Gaze.Degrees.x);
            Add("gazePitch", Gaze.Degrees.y);
            Camera cam = Camera.main;
            if (cam != null)
            {
                Ray center = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
                Ray aimed = Gaze.ViewportPointToRay(cam, new Vector3(0.5f, 0.5f, 0f));
                Vector3 local = Quaternion.Inverse(Quaternion.LookRotation(center.direction, cam.cameraToWorldMatrix.MultiplyVector(Vector3.up))) * aimed.direction;
                Add("aimAngleFromCenter", Vector3.Angle(center.direction, aimed.direction));
                Add("aimLocalX", local.x);
                Add("aimLocalY", local.y);
            }

            Add("uiOverlayCamera", UiOverlay.Camera != null);
            Add("reticlePresent", Object.FindObjectOfType<ReticleManager>() != null);
            Add("reticleDrawing", ReticleFollower.Drawing);
            Vector3 rd = ReticleFollower.LocalDirection;
            Add("reticleYaw", Mathf.Atan2(rd.x, rd.z) * Mathf.Rad2Deg);       // head space, + right
            Add("reticlePitch", Mathf.Asin(Mathf.Clamp(rd.y, -1f, 1f)) * Mathf.Rad2Deg); // + up

            Add("veilReady", Veil.Ready);
            Add("veilUpperEdge", Veil.UpperEdgeFrac);
            Add("veilLowerEdge", Veil.LowerEdgeFrac);
            Add("veilUpperAlpha", Veil.UpperAlpha);
            Add("veilLowerAlpha", Veil.LowerAlpha);
            Add("veilFullAlpha", Veil.FullAlpha);
            Add("veilFullScreenImages", Veil.FullScreenDarkCount);
            Add("veilOriginalsHidden", Veil.OriginalsHidden);
            Add("veilBehindPanel", Veil.AllBehindPanel);
            Add("volumetricOn", Camera.main != null && Camera.main.GetComponent<VolumetricLightRenderer>() is VolumetricLightRenderer vlr && vlr.enabled);
            Add("ssrOn", Camera.main != null && Camera.main.GetComponent<UnityEngine.PostProcessing.PostProcessingBehaviour>() is UnityEngine.PostProcessing.PostProcessingBehaviour ppb && ppb.profile != null && ppb.profile.screenSpaceReflection.enabled);
            Add("waterMaterialsChanged", StereoComfort.WaterMaterialsChanged);
            Add("waterEyeRenders", WaterReflectionVR.EyeRenders);
            Add("stereoMode", UnityEngine.XR.XRSettings.stereoRenderingMode.ToString());
            Add("overlayLayerExclusive", UiOverlay.OwnLayerExclusive);

            Add("introSkipped", IntroSkip.Skipped);
            Add("introSeen", Plugin.IntroSeen.Value);
            Add("menuActive", MenuGaze.Active);
            Add("menuHover", MenuGaze.HoverName);
            Add("menuClicks", MenuGaze.Clicks);
            Add("menuHoverDegrees", MenuGaze.HoverDegrees);
            Add("menuPanelWidth", MenuGaze.PanelWidth);

            InteractableManager im = Object.FindObjectOfType<InteractableManager>();
            if (im != null)
            {
                Add("imCamera", im._mainCamera != null ? im._mainCamera.name : "");
                Add("imCameraIsMain", im._mainCamera != null && im._mainCamera == Camera.main);
                Add("imCurrent", im._currentObject != null ? im._currentObject.name : "");
                Add("imHit", im._hit.transform != null ? im._hit.transform.name : "");
                Add("imLocked", im._lockedByObject);
                if (im._mainCamera != null)
                {
                    Ray r = Gaze.ViewportPointToRay(im._mainCamera, new Vector3(0.5f, 0.5f, 0f));
                    bool hit = Physics.Raycast(r, out RaycastHit h, 1000f, im._layerMask);
                    Add("imRayHitNow", hit && h.transform != null ? h.transform.name : "");
                    Add("imRayHitTag", hit && h.transform != null ? h.transform.tag : "");
                    Add("imLayerMask", im._layerMask.value);
                    Ray plain = im._mainCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
                    Transform headT = HeadTracking.ChildCamera;
                    // Does the game's center ray follow the VR head? (angle between it and the head's forward)
                    Add("imRayVsHeadDeg", headT != null ? Vector3.Angle(plain.direction, headT.forward) : -1f);
                    Add("imRayVsCamTransformDeg", Vector3.Angle(plain.direction, im._mainCamera.transform.forward));
                    Add("headVsCamTransformDeg", headT != null ? Vector3.Angle(headT.forward, im._mainCamera.transform.forward) : -1f);
                }
            }
            Add("aimAssistDegrees", Plugin.AimAssistDegrees.Value);
            Add("aimAssistHits", AimAssist.AssistedHits);
            if (tag != null && tag.StartsWith("aimtest"))
            {
                bool[] r = AimAssist.SelfTest();
                Add("aimTestRan", r != null);
                if (r != null)
                {
                    Add("aimTestBare2", r[0]);
                    Add("aimTestAssist2", r[1]);
                    Add("aimTestAssist6", r[2]);
                }
            }

            var sb = new StringBuilder("{\n");
            for (int i = 0; i < f.Count; i++)
            {
                sb.Append("  \"").Append(f[i].Key).Append("\": ").Append(Json(f[i].Value));
                sb.Append(i < f.Count - 1 ? ",\n" : "\n");
            }
            sb.Append("}\n");

            string path = Path.Combine(Paths.BepInExRootPath, "BYEFrameBlink_status.json");
            File.WriteAllText(path, sb.ToString());
            Plugin.Log.LogInfo($"[cmd] status '{tag}' -> {path}");
        }

        private static string Json(object v)
        {
            switch (v)
            {
                case bool b: return b ? "true" : "false";
                case float x: return float.IsNaN(x) || float.IsInfinity(x) ? "null" : x.ToString("R", CultureInfo.InvariantCulture);
                case int n: return n.ToString(CultureInfo.InvariantCulture);
                case long l: return l.ToString(CultureInfo.InvariantCulture);
                default:
                    return "\"" + (v?.ToString() ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            }
        }
    }
}
