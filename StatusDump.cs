using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

namespace BYEFrameBlink
{
    /// <summary>
    /// "/bye/status [tag]" writes a flat JSON snapshot of the plugin + game state to BepInEx/BYEFrameBlink_status.json.
    /// tools/run_tests.ps1 asserts on it, so keep key names stable.
    /// </summary>
    internal static class StatusDump
    {
        /// <summary>SteamVR's view of the last ~90 frames: app frame interval, GPU time, how many frames it had to reuse.</summary>
        public static string FrameTiming()
        {
            try
            {
                Valve.VR.CVRCompositor comp = Valve.VR.OpenVR.Compositor;
                if (comp == null) return "no compositor";
                var t = new Valve.VR.Compositor_FrameTiming[90];
                for (int i = 0; i < t.Length; i++) t[i].m_nSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(Valve.VR.Compositor_FrameTiming));
                uint n = comp.GetFrameTimings(t);
                if (n == 0) return "no frames";
                float gpu = 0, interval = 0, cpu = 0; int reused = 0, dropped = 0;
                for (int i = 0; i < n; i++)
                {
                    gpu += t[i].m_flTotalRenderGpuMs;
                    interval += t[i].m_flClientFrameIntervalMs;
                    cpu += t[i].m_flNewFrameReadyMs - t[i].m_flNewPosesReadyMs;
                    if (t[i].m_nNumFramePresents > 1) reused++;
                    dropped += (int)t[i].m_nNumDroppedFrames;
                }
                return $"frames={n} appInterval={interval / n:F1}ms (~{1000f / (interval / n):F0} fps) gpu={gpu / n:F1}ms cpuToReady={cpu / n:F1}ms reused={reused} dropped={dropped} " +
                       $"scale={UnityEngine.XR.XRSettings.eyeTextureResolutionScale:F2} viewport={UnityEngine.XR.XRSettings.renderViewportScale:F2} msaa={QualitySettings.antiAliasing} hdr={(Camera.main != null && Camera.main.allowHDR)} shadows={QualitySettings.shadows}";
            }
            catch (System.Exception e) { return "error: " + e.Message; }
        }

        private static bool _probeInstalled;
        private static Matrix4x4 _renderView;
        private static int _renderFrame;

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
            // Where the headset actually renders "straight ahead" vs the head pose the aim ray is built from: the
            // forward of each eye's view matrix, expressed in the aim frame (yaw +right, pitch +up, degrees).
            if (child != null && child.parent != null && Camera.main != null && UnityEngine.XR.XRSettings.enabled)
            {
                Quaternion aim = child.parent.rotation * head;
                string Dir(Vector3 world)
                {
                    Vector3 d = Quaternion.Inverse(aim) * world;
                    return $"{Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg:F2}/{Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg:F2}";
                }
                Vector3 Fwd(Camera.StereoscopicEye e) => -Camera.main.GetStereoViewMatrix(e).inverse.GetColumn(2);
                string Node(XRNode n) => Dir(child.parent.rotation * InputTracking.GetLocalRotation(n) * Vector3.forward);
                if (!_probeInstalled)
                {
                    _probeInstalled = true;
                    Camera.onPreRender += c => { if (c == Camera.main) { _renderView = c.worldToCameraMatrix; _renderFrame = Time.frameCount; } };
                }
                Add("renderedFwd", _renderFrame > 0 ? $"{Dir(-_renderView.inverse.GetColumn(2))} (frame {Time.frameCount - _renderFrame} ago)" : "none yet, ask again");
                Add("renderVsAim", $"viewL={Dir(Fwd(Camera.StereoscopicEye.Left))} viewR={Dir(Fwd(Camera.StereoscopicEye.Right))} " +
                                   $"centerEye={Node(XRNode.CenterEye)} leftEye={Node(XRNode.LeftEye)} rightEye={Node(XRNode.RightEye)} camFwd={Dir(Camera.main.transform.forward)}");
            }
            try
            {
                Valve.VR.CVRSystem vr = Valve.VR.OpenVR.System;
                if (vr != null)
                {
                    string Eye(Valve.VR.EVREye e)
                    {
                        Valve.VR.HmdMatrix34_t m = vr.GetEyeToHeadTransform(e);
                        // OpenVR is right-handed (-z forward): forward = -column 2.
                        float fx = -m.m2, fy = -m.m6, fz = -m.m10;
                        float l = 0, r = 0, t = 0, b = 0;
                        vr.GetProjectionRaw(e, ref l, ref r, ref t, ref b);
                        return $"fwd yaw={Mathf.Atan2(fx, -fz) * Mathf.Rad2Deg:F2} pitch={Mathf.Asin(Mathf.Clamp(fy, -1f, 1f)) * Mathf.Rad2Deg:F2} " +
                               $"pos=({m.m3:F3},{m.m7:F3},{m.m11:F3}) raw l={l:F3} r={r:F3} t={t:F3} b={b:F3}";
                    }
                    Add("openvrEyes", $"L[{Eye(Valve.VR.EVREye.Eye_Left)}] R[{Eye(Valve.VR.EVREye.Eye_Right)}]");
                }
            }
            catch (System.Exception e) { Add("openvrEyes", "error: " + e.Message); }
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
            UnityEngine.PostProcessing.VignetteModel vig = VignetteVR.CurrentModel();
            Add("vignetteModelOn", vig != null && vig.enabled && vig.settings.intensity > 0.001f);
            if (Camera.main != null && Camera.main.GetComponent<UnityEngine.PostProcessing.PostProcessingBehaviour>() is UnityEngine.PostProcessing.PostProcessingBehaviour ppAll && ppAll.profile != null)
            {
                var pf = ppAll.profile;
                var on = new System.Collections.Generic.List<string>();
                if (pf.antialiasing.enabled) on.Add("antialiasing:" + pf.antialiasing.settings.method);
                if (pf.ambientOcclusion.enabled) on.Add("ao");
                if (pf.screenSpaceReflection.enabled) on.Add("ssr");
                if (pf.depthOfField.enabled) on.Add("dof");
                if (pf.motionBlur.enabled) on.Add("motionBlur");
                if (pf.eyeAdaptation.enabled) on.Add("eyeAdaptation");
                if (pf.bloom.enabled) on.Add("bloom");
                if (pf.colorGrading.enabled) on.Add("colorGrading");
                if (pf.userLut.enabled) on.Add("userLut");
                if (pf.chromaticAberration.enabled) on.Add("chromaticAberration:" + pf.chromaticAberration.settings.intensity.ToString("F2"));
                if (pf.grain.enabled) on.Add("grain:" + pf.grain.settings.intensity.ToString("F2") + "/" + pf.grain.settings.size.ToString("F2"));
                if (pf.vignette.enabled) on.Add("vignette");
                if (pf.dithering.enabled) on.Add("dithering");
                if (pf.fog.enabled) on.Add("fog");
                Add("ppProfile", pf.name + ": " + string.Join(",", on.ToArray()));
            }
            Add("vignetteFlatActive", VignetteVR.FlatActive());
            Add("vignetteOverlay", VignetteVR.OverlayShown);
            Add("eyeSeparation", Plugin.EyeSeparationScale.Value);
            Add("webcamFeedShown", WebcamBlock.FeedShown);
            Add("screen", $"{Screen.width}x{Screen.height} fullscreen={Screen.fullScreen}");
            GameObject uiQuad = GameObject.Find("VrUiQuad");
            Texture uiTex = uiQuad != null && uiQuad.GetComponent<Renderer>() != null ? uiQuad.GetComponent<Renderer>().sharedMaterial.mainTexture : null;
            Add("uiTexture", uiTex != null ? $"{uiTex.width}x{uiTex.height} mips={(uiTex is RenderTexture rtx && rtx.useMipMap)} filter={uiTex.filterMode}" : "");
            Add("eyeTexture", $"{UnityEngine.XR.XRSettings.eyeTextureWidth}x{UnityEngine.XR.XRSettings.eyeTextureHeight} scale={UnityEngine.XR.XRSettings.eyeTextureResolutionScale:F2} viewport={UnityEngine.XR.XRSettings.renderViewportScale:F2}");
            Add("quality", $"{QualitySettings.names[QualitySettings.GetQualityLevel()]} aa={QualitySettings.antiAliasing} mainTarget={(Camera.main != null && Camera.main.targetTexture != null ? Camera.main.targetTexture.width + "x" + Camera.main.targetTexture.height : "-")} " +
                                $"dynRes={(Camera.main != null && Camera.main.allowDynamicResolution)} scaler={ScalableBufferManager.widthScaleFactor:F2}");
            Add("render", $"shadowRes={QualitySettings.shadowResolution} dist={QualitySettings.shadowDistance:F0} cascades={QualitySettings.shadowCascades} proj={QualitySettings.shadowProjection} " +
                          (Camera.main != null ? $"path={Camera.main.actualRenderingPath} msaa={Camera.main.allowMSAA} hdr={Camera.main.allowHDR} far={Camera.main.farClipPlane:F0}" : ""));
            if (Camera.main != null)
            {
                Camera c = Camera.main;
                string P(Matrix4x4 m) => $"m00={m.m00:F3} m02={m.m02:F3} m11={m.m11:F3} m12={m.m12:F3}";
                Add("projection", $"mono[{P(c.projectionMatrix)}] L[{P(c.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left))}] R[{P(c.GetStereoProjectionMatrix(Camera.StereoscopicEye.Right))}] " +
                                  $"pixels={c.pixelWidth}x{c.pixelHeight} aspect={c.aspect:F3} fov={c.fieldOfView:F1} sep={c.stereoSeparation:F3}");
            }
            Add("autoResolution", $"{BYEFrameBlink.AutoResolution.Label} factor={BYEFrameBlink.AutoResolution.Factor:F2} gpu={BYEFrameBlink.AutoResolution.LastGpuMs:F1}ms reused={BYEFrameBlink.AutoResolution.LastReused}");
            Add("performance", $"{BYEFrameBlink.Performance.Label} wine={BYEFrameBlink.Performance.OnWine}");
            Add("uiQuadScale", uiQuad != null ? uiQuad.transform.lossyScale.ToString("F2") : "");
            Add("vrMenuEntries", VrSettingsMenu.Entries);
            Add("vrMenuOpen", VrSettingsMenu.IsOpen);
            Add("vrMenuRows", VrSettingsMenu.OpenRows);
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
