using System;
using System.IO;
using System.Linq;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace BYEFrameBlink
{
    /// <summary>
    /// Debug autoplay ("/bye/autoplay on|off"): plays the game through the same paths a player uses, so a full run
    /// exercises the plugin end to end. It drives the simulated head (aim) and the eyes (BlinkState) only:
    /// - an active EyesClosedMechanic -> hold the eyes shut until it completes;
    /// - an interactable is up -> turn the head to its icon, wait until the game reports it as looked at, blink;
    /// - the HUD blink is on deck -> blink after a while (so some dialogue plays first);
    /// - nothing changes for StuckSeconds -> log STUCK, screenshot, and skip the scene (BlinkCounter.ForceEndBlink).
    /// Logs to BepInEx/BYEFrameBlink_autoplay.log and screenshots to BYEFrameBlink_shots/autoplay/.
    /// </summary>
    internal static class AutoPlay
    {
        private const float StuckSeconds = 240f;
        private const float HudBlinkAfter = 7f;
        private const float MaxSceneSeconds = 900f; // past this, dialogue no longer holds off the stuck timer
        private static float _sceneStartAt;
        private const float ScreenshotEvery = 45f;

        public static bool Enabled { get; private set; }

        /// <summary>Interactable name fragments autoplay must not use ("/bye/autoplay avoid X"), to test the other branch of a choice.</summary>
        public static readonly System.Collections.Generic.List<string> Avoid = new System.Collections.Generic.List<string>();

        /// <summary>
        /// "/bye/autoplay pause-on X": freeze autoplay (simulated head and eyes stay in its control, nothing acts) as soon
        /// as a log line contains X, so a moment can be inspected by hand. "/bye/autoplay resume" continues.
        /// </summary>
        /// <summary>
        /// "/bye/autoplay thorough on": only take a scene's closing blink once dialogue has been quiet and nothing new
        /// has happened for a while, so optional interactables and late dialogue get exercised too (slower).
        /// </summary>
        public static bool Thorough;
        private static float _lastActivity, _dialogueBusyAt;

        private static bool ReadyToClose(float now)
        {
            if (!Thorough || now - _onDeckSince > 90f) return true; // never wait forever (e.g. looping dialogue)
            return now - Mathf.Max(_lastActivity, _progressAt) > 10f && now - _dialogueBusyAt > 3f;
        }

        /// <summary>Before a closing blink: log what the scene still offered (a coverage hint for thorough passes).</summary>
        private static void LogUntouched()
        {
            var left = UnityEngine.Object.FindObjectsOfType<InteractableObject>()
                .Where(ia => ia.isActiveAndEnabled && ia._interactState == InteractableObject.InteractState.IsInteractable && Targetable(ia))
                .Select(ia => ia.name).ToArray();
            if (left.Length > 0) Log($"closing with {left.Length} untouched: {string.Join(", ", left)}");
        }

        public static string PauseOn;
        public static bool Paused;

        /// <summary>Continue after a pause; the time spent paused does not count towards the stuck timer.</summary>
        public static void Resume()
        {
            Paused = false;
            _progressAt = Time.unscaledTime;
        }

        private static StreamWriter _log;
        private static float _blinkUntil, _holdUntil, _nextBlinkAllowed, _nextShot, _progressAt, _onDeckSince;
        private static string _scene = "";
        private static int _sceneCount, _shotCount, _blinks, _interacts, _holds, _skips;
        private static InteractableObject _target;
        private static InteractableObject[] _candidates = new InteractableObject[0];
        private static float _nextScan, _nextGazeScan, _nextPianoScan, _lastIdleBlink;
        private static Collider[] _pianoKeys = new Collider[0];
        private static Collider _dotTarget, _dotAimed;
        private static ConnectTheDots _dotPattern, _dotPatternCandidate;
        private static int _dotPatternNext, _dotPatternCandidateNext;
        private static InteractableObject _lastInteracted;
        private static float _lastInteractAt;
        private static int _repeatCount;
        private static float _deckSinceAny;
        private static float _dotSince;
        private static readonly System.Collections.Generic.Dictionary<int, float> _ignoreUntil = new System.Collections.Generic.Dictionary<int, float>();
        private static float _nextDotScan;
        private static readonly System.Collections.Generic.HashSet<int> _dotLogged = new System.Collections.Generic.HashSet<int>();
        private static bool _pianoLogged, _typeLogged;
        private static Collider[] _typeKeys = new Collider[0];
        private static float _nextKeyScan;
        private static System.Collections.Generic.List<Collider> _gazeTargets = new System.Collections.Generic.List<Collider>();
        private static readonly System.Collections.Generic.HashSet<int> _gazeLogged = new System.Collections.Generic.HashSet<int>();
        private static float _aimedSince;
        private static int _lastGameBlinks;
        private static bool _savedSim, _savedTest;

        public static bool ForceClosed => Enabled && Time.unscaledTime < Mathf.Max(_blinkUntil, _holdUntil);
        public static string Summary => $"scenes={_sceneCount} blinks={_blinks} interacts={_interacts} holds={_holds} skips={_skips} scene={_scene}";

        public static void Set(bool on)
        {
            if (on == Enabled) return;
            Enabled = on;
            Paused = false;
            if (on)
            {
                _savedSim = Plugin.SimulateHead.Value;
                _savedTest = Plugin.TestMode.Value;
                // Don't write the autoplay-only values to disk: if the game is killed mid-run they'd stick and the
                // real headset would be ignored on the next launch.
                Plugin.Instance.Config.SaveOnConfigSet = false;
                Plugin.SimulateHead.Value = true;
                Plugin.TestMode.Value = true;
                string path = Path.Combine(Paths.BepInExRootPath, "BYEFrameBlink_autoplay.log");
                _log = new StreamWriter(path, true) { AutoFlush = true };
                _progressAt = Time.unscaledTime;
                _deckSinceAny = _onDeckSince = Time.unscaledTime;
                _scene = "";
                Log("=== autoplay started ===");
            }
            else
            {
                Log("=== autoplay stopped: " + Summary);
                _log?.Close();
                _log = null;
                Plugin.SimulateHead.Value = _savedSim;
                Plugin.TestMode.Value = _savedTest;
                Plugin.Instance.Config.SaveOnConfigSet = true;
                Plugin.Instance.Config.Save();
            }
        }

        private static void Log(string msg)
        {
            string line = $"{DateTime.Now:HH:mm:ss} [{_scene}] {msg}";
            _log?.WriteLine(line);
            Plugin.Log.LogInfo("[autoplay] " + msg);
            if (!msg.StartsWith("blink (idle") && !msg.StartsWith("blink (scene")) _lastActivity = Time.unscaledTime;
            if (!string.IsNullOrEmpty(PauseOn) && msg.IndexOf(PauseOn, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                PauseOn = null;
                Paused = true;
                _log?.WriteLine($"{DateTime.Now:HH:mm:ss} [{_scene}] PAUSED (resume with /bye/autoplay resume)");
            }
        }

        private static void Shot(string why)
        {
            _shotCount++;
            string name = $"autoplay/{_shotCount:D4}_{Sanitize(_scene)}_{why}";
            Directory.CreateDirectory(Path.Combine(Paths.BepInExRootPath, "BYEFrameBlink_shots/autoplay"));
            ScreenCapture.CaptureScreenshot(Path.Combine(Paths.BepInExRootPath, "BYEFrameBlink_shots/" + name + ".png"),
                ScreenCapture.StereoScreenCaptureMode.LeftEye);
        }

        private static string Sanitize(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Replace(' ', '_');
        }

        private static void Blink(string why, float duration = 0.18f)
        {
            _blinkUntil = Time.unscaledTime + duration;
            _nextBlinkAllowed = Time.unscaledTime + duration + 1.2f;
            _blinks++;
            Log($"blink ({why})");
        }

        /// <summary>
        /// Can the aim ray select this interactable right now? Its state can say IsInteractable while the icon is still
        /// switched off (gated by something else in the scene); aiming at those just wastes time.
        /// </summary>
        private static bool Targetable(InteractableObject ia)
        {
            if (ia.GetComponent<GazeInteract>() != null || ia is InteractablePaintObject) return true; // dots / canvas instead of the icon
            InteractableIcon icon = ia._interactableIcon;
            if (icon == null) return true;
            if (!icon.gameObject.activeInHierarchy) return false;
            Collider c = icon.GetComponent<Collider>();
            return c == null || c.enabled;
        }
        /// <summary>World point to look at: the center of the interactable's collider (what the game's ray must hit).</summary>
        public static Vector3 AimPoint(Component target)
        {
            InteractableObject ia = target as InteractableObject;
            // A picked-up paint object hovers in front of you with its icon collider off; the game then wants you to
            // look at the canvas (PaintGameManager), which InteractableManager maps back to the current paint object.
            if (ia is InteractablePaintObject)
            {
                Collider icon = ia._interactableIcon != null ? ia._interactableIcon.GetComponent<Collider>() : null;
                PaintGameManager pgm = Traverse.Create(ia).Field("_paintGM").GetValue<PaintGameManager>();
                bool held = Traverse.Create(ia).Field("_state").GetValue()?.ToString() == "CURRENT";
                if ((held || icon == null || !icon.enabled) && pgm != null)
                {
                    Collider canvas = pgm.GetComponent<Collider>();
                    // Wander over the canvas: the held object itself may block the ray to its center.
                    float t = Time.unscaledTime;
                    if (canvas != null) return canvas.bounds.center + Vector3.Scale(canvas.bounds.extents * 0.6f,
                        new Vector3(Mathf.Sin(t * 0.9f), Mathf.Sin(t * 1.3f), Mathf.Sin(t * 0.7f)));
                }
            }
            Component c = ia != null && ia._interactableIcon != null ? (Component)ia._interactableIcon : target;
            Collider col = c.GetComponentInChildren<Collider>();
            if (col == null) col = target.GetComponentInChildren<Collider>();
            return col != null ? col.bounds.center : c.transform.position;
        }

        /// <summary>
        /// Rotates the simulated head so the given camera's center ray goes through a world point. The VR child camera
        /// sits under the game camera with identity offset, so the head rotation is relative to the camera transform.
        /// </summary>
        public static void AimAt(Camera cam, Vector3 worldPoint)
        {
            Transform head = HeadTracking.ChildCamera;
            Vector3 origin = head != null ? head.position : cam.transform.position;
            Quaternion parentRot = head != null && head.parent != null ? head.parent.rotation : cam.transform.rotation;
            Vector3 local = Quaternion.Inverse(parentRot) * (worldPoint - origin);
            if (local.sqrMagnitude > 1e-6f) SimHead.SetRotation(Quaternion.LookRotation(local.normalized, Vector3.up));
        }

        public static void Update()
        {
            if (!Enabled || Paused) return;
            float now = Time.unscaledTime;
            DialogueManager dm = DialogueManager.instance;
            if (dm != null && dm._state != DialogueManager.State.Idle) _dialogueBusyAt = now;
            // A scene that is still talking is not stuck (some long monologues come before the closing blink).
            if (now - _dialogueBusyAt < 1f && now - _sceneStartAt < MaxSceneSeconds) _progressAt = Mathf.Max(_progressAt, now - 1f);

            // Scene tracking.
            GameManager gm = GameManager.instance;
            string scene = gm != null && gm._currentScene != null ? gm._currentScene.name : UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (scene != _scene)
            {
                _scene = scene;
                _sceneCount++;
                _progressAt = now;
                _sceneStartAt = now;
                _deckSinceAny = now;
                _onDeckSince = now;
                _target = null;
                Log($"scene #{_sceneCount}");
                Shot("enter");
                _nextShot = now + 5f;
            }
            if (BlinkConfirmedCounter.Count != _lastGameBlinks)
            {
                _lastGameBlinks = BlinkConfirmedCounter.Count;
                // Our own idle blinks always "confirm"; counting them kept the stuck timer from ever firing.
                if (now - _lastIdleBlink > 1.5f) _progressAt = now;
            }
            if (now >= _nextShot)
            {
                _nextShot = now + ScreenshotEvery;
                Shot("t");
            }
            if (now - _progressAt > StuckSeconds)
            {
                _skips++;
                Log("STUCK: no progress for " + StuckSeconds + " s, skipping scene");
                Shot("stuck");
                BlinkCounter.instance?.ForceEndBlink();
                _progressAt = now;
            }

            if (ForceClosed) return;

            // Pause menu open (e.g. focus loss)? Close it.
            PauseMenuController pause = PauseMenuController.instance;
            if (pause != null && pause.activeMenu != PauseMenuController.ActiveMenu.None)
            {
                Log("pause menu open -> resume");
                pause.Resume();
                return;
            }

            // Eyes-closed sections.
            foreach (EyesClosedMechanic ecm in UnityEngine.Object.FindObjectsOfType<EyesClosedMechanic>())
            {
                if (!Traverse.Create(ecm).Field("_detectionEnabled").GetValue<bool>()) continue;
                _holds++;
                _holdUntil = now + Mathf.Max(4f, ecm._duration + 1f);
                _progressAt = now;
                Log($"holding eyes closed for {ecm.name} ({_holdUntil - now:F0} s)");
                return;
            }

            BlinkCounter counter = BlinkCounter.instance;
            InteractableManager im = InteractableManager.instance;
            BlinkIconManager icons = BlinkIconManager.instance;
            if (counter == null || im == null) return;

            // Connect-the-dots (gaze traces a line of dots; often gates an interactable, e.g. picking up a crayon):
            // look at the next live dot in order.
            Camera dotCam = im._mainCamera != null ? im._mainCamera : Camera.main;
            if (dotCam != null && now >= _nextDotScan)
            {
                _nextDotScan = now + 0.25f;
                // Several patterns can be live at once; finish the one in progress instead of hopping between them
                // (hopping never let a pattern complete). Preference: the pattern we're on, then any pattern already
                // started, then the first one found.
                Collider best = null;
                int bestScore = -1;
                foreach (ConnectTheDots ctd in UnityEngine.Object.FindObjectsOfType<ConnectTheDots>())
                {
                    if (ctd.complete || ctd.dots == null) continue;
                    int next = Traverse.Create(ctd).Field("nextDotId").GetValue<int>();
                    if (next >= ctd.dots.Length || ctd.dots[next].dotObject == null || !ctd.dots[next].dotObject.activeInHierarchy) continue;
                    Collider dc = ctd.dots[next].dotObject.GetComponent<Collider>();
                    if (dc == null || !dc.enabled || (_ignoreUntil.TryGetValue(dc.GetInstanceID(), out float dIgnore) && now < dIgnore)) continue;
                    int score = ctd == _dotPattern ? 2 : next > 0 ? 1 : 0;
                    if (score <= bestScore) continue;
                    best = dc;
                    bestScore = score;
                    if (score == 2) { _dotPatternNext = next; }
                    else { _dotPatternCandidate = ctd; _dotPatternCandidateNext = next; }
                }
                if (best != null && bestScore < 2) { _dotPattern = _dotPatternCandidate; _dotPatternNext = _dotPatternCandidateNext; }
                if (best == null) _dotPattern = null;
                _dotTarget = best;
                if (best != null && _dotLogged.Add(best.GetInstanceID())) Log($"dot {_dotPattern?.name}[{_dotPatternNext}] {best.name}");
            }
            if (_dotTarget != null && dotCam != null && _dotTarget.enabled && _dotTarget.gameObject.activeInHierarchy)
            {
                if (_dotTarget != _dotAimed) { _dotAimed = _dotTarget; _dotSince = now; _progressAt = now; }
                if (now - _dotSince > 6f)
                {
                    // Some dots stay live but no longer do anything (e.g. the object they pick up is already held).
                    Log($"dot {_dotTarget.name} did not advance in 6 s; ignoring it for 30 s");
                    _ignoreUntil[_dotTarget.GetInstanceID()] = now + 30f;
                    _dotTarget = null;
                    _dotAimed = null;
                }
                else
                {
                    // Gaze events fire only when the gazed object changes; if the ray already rested on this dot, glance away.
                    float held = (now - _dotSince) % 2.5f;
                    AimAt(dotCam, _dotTarget.bounds.center);
                    if (now - _dotSince > 1.5f && held > 2.1f) SimHead.SetRotation(SimHead.Rotation * Quaternion.Euler(0f, 25f, 0f));
                    return;
                }
            }

            // The scene's closing blink has been on offer for a while: take it even if optional interactables remain.
            bool deckUp = icons != null && icons._onDeckState != BlinkIconManager.OnDeckState.none;
            if (!deckUp) _deckSinceAny = now;
            else if (now - _deckSinceAny > (Thorough ? 120f : 60f) && counter._canBlink && now >= _nextBlinkAllowed && ReadyToClose(now))
            {
                _deckSinceAny = now;
                _target = null;
                LogUntouched();
                Blink(Thorough ? "scene (on deck 120 s)" : "scene (on deck 60 s)");
                return;
            }

            // Interactables: aim at the first live one.
            if (_target == null || !_target.isActiveAndEnabled || _target._interactState != InteractableObject.InteractState.IsInteractable)
            {
                _target = null;
                // Not every interactable registers in InteractableManager.activeIas, so scan the scene (throttled).
                if (now >= _nextScan)
                {
                    _nextScan = now + 0.5f;
                    _candidates = UnityEngine.Object.FindObjectsOfType<InteractableObject>();
                }
                foreach (InteractableObject ia in _candidates)
                {
                    if (ia != null && ia.isActiveAndEnabled && ia._interactState == InteractableObject.InteractState.IsInteractable &&
                        !(_ignoreUntil.TryGetValue(ia.GetInstanceID(), out float until) && now < until) &&
                        !Avoid.Exists(a => ia.name.IndexOf(a, StringComparison.OrdinalIgnoreCase) >= 0) && Targetable(ia))
                    {
                        _target = ia;
                        _aimedSince = now;
                        Log($"target {ia.name}");
                        break;
                    }
                }
            }

            Camera cam = im._mainCamera != null ? im._mainCamera : Camera.main;
            if (_target != null && cam != null)
            {
                AimAt(cam, AimPoint(_target));

                bool lookedAt = im._currentObject == _target;
                if (lookedAt && now - _aimedSince > 0.5f && counter._canBlink && now >= _nextBlinkAllowed)
                {
                    _interacts++;
                    // Blinking at it again and again with no effect? Some prompts want the eyes held shut instead.
                    _repeatCount = _target == _lastInteracted && now - _lastInteractAt < 6f ? _repeatCount + 1 : 0;
                    _lastInteracted = _target;
                    _lastInteractAt = now;
                    if (_repeatCount >= 3)
                    {
                        _repeatCount = 0;
                        _holds++;
                        _holdUntil = now + 8f;
                        Log($"holding eyes closed on {_target.name} (blinks had no effect)");
                    }
                    else Blink("interact " + _target.name);
                    _target = null;
                }
                else if (now - _aimedSince > 20f)
                {
                    Log($"could not get {_target.name} looked at (current={(im._currentObject != null ? im._currentObject.name : "none")}, ray hits {(im._hit.transform != null ? im._hit.transform.name + "/" + im._hit.transform.tag : "nothing")}, aim {AimPoint(_target)}); ignoring it for 30 s");
                    _ignoreUntil[_target.GetInstanceID()] = now + 30f;
                    _target = null;
                }
                return;
            }

            // Painting: strokes land only while the aim ray stays on the canvas; wander over it until the timer fills.
            if (cam != null)
            {
                foreach (PaintGameManager pgm in UnityEngine.Object.FindObjectsOfType<PaintGameManager>())
                {
                    Collider canvas = pgm._isPainting ? pgm.GetComponent<Collider>() : null;
                    if (canvas == null) continue;
                    Bounds bb = canvas.bounds;
                    AimAt(cam, bb.center + Vector3.Scale(bb.extents * 0.5f, new Vector3(Mathf.Sin(now * 1.3f), Mathf.Sin(now * 1.7f), Mathf.Sin(now * 1.1f))));
                    _progressAt = now;
                    return;
                }
            }

            // Piano minigames: notes play while the aim ray rests on any "PianoKey"-tagged key; sweep along the keys.
            ReticleManager reticle = ReticleManager.Instance;
            if (cam != null && reticle != null &&
                (reticle.reticleImage == ReticleManager.ImageType.PIANO || reticle.reticleImage == ReticleManager.ImageType.PIANOPUNCH))
            {
                if (now >= _nextPianoScan)
                {
                    _nextPianoScan = now + 1f;
                    try { _pianoKeys = UnityEngine.Object.FindObjectsOfType<Collider>().Where(c => c.enabled && c.CompareTag("PianoKey") && ((1 << c.gameObject.layer) & im._layerMask) != 0).ToArray(); }
                    catch { _pianoKeys = new Collider[0]; }
                    if (_pianoKeys.Length > 0 && !_pianoLogged) { _pianoLogged = true; Log($"piano: gazing over {_pianoKeys.Length} keys"); }
                }
                _pianoKeys = _pianoKeys.Where(c => c != null && c.gameObject.activeInHierarchy).ToArray();
                if (_pianoKeys.Length > 0)
                {
                    AimAt(cam, _pianoKeys[(int)(now / 0.5f) % _pianoKeys.Length].bounds.center);
                    _progressAt = now;
                    return;
                }
            }

            // Typewriter: every move of the aim onto a different "TypewriterKey" types one character; hop between keys.
            if (cam != null && reticle != null &&
                (reticle.reticleImage == ReticleManager.ImageType.HAND || reticle.reticleImage == ReticleManager.ImageType.HANDPUNCH))
            {
                if (now >= _nextKeyScan)
                {
                    _nextKeyScan = now + 1f;
                    try { _typeKeys = UnityEngine.Object.FindObjectsOfType<Collider>().Where(c => c.enabled && c.CompareTag("TypewriterKey") && ((1 << c.gameObject.layer) & im._layerMask) != 0).ToArray(); }
                    catch { _typeKeys = new Collider[0]; }
                    if (_typeKeys.Length > 1 && !_typeLogged) { _typeLogged = true; Log($"typewriter: hopping over {_typeKeys.Length} keys"); }
                }
                _typeKeys = _typeKeys.Where(c => c != null && c.gameObject.activeInHierarchy).ToArray();
                if (_typeKeys.Length > 1)
                {
                    AimAt(cam, _typeKeys[(int)(now / 0.15f) % _typeKeys.Length].bounds.center);
                    _progressAt = now;
                    return;
                }
            }

            // Gaze-only progress (look at an object, no blink): cycle through what the game's gaze listeners wait for.
            // Otherwise look forward again, and after a long stall sweep the view in case the target is unlisted.
            if (cam != null && now >= _nextGazeScan)
            {
                _nextGazeScan = now + 0.5f;
                _gazeTargets = GazeTargets.Collect(im._layerMask);
                foreach (Collider c in _gazeTargets)
                    if (_gazeLogged.Add(c.GetInstanceID())) Log($"gaze target {c.name}");
            }
            _gazeTargets.RemoveAll(c => c == null || !c.enabled || !c.gameObject.activeInHierarchy);
            if (cam != null && _gazeTargets.Count > 0)
            {
                int slot = (int)(now / 2f);                 // 2 s per target, a forward look between rounds
                int i = slot % (_gazeTargets.Count + 1);
                if (i < _gazeTargets.Count) AimAt(cam, _gazeTargets[i].bounds.center);
                else SimHead.SetRotation(Quaternion.identity);
            }
            else if (now - _progressAt > 30f)
            {
                float t = now - _progressAt;
                SimHead.SetRotation(Quaternion.Euler(Mathf.Sin(t * 0.37f) * 30f, Mathf.Sin(t * 0.23f) * 70f, 0f));
            }
            else SimHead.SetRotation(Quaternion.Slerp(SimHead.Rotation, Quaternion.identity, 0.05f));
            bool onDeck = icons != null && icons._onDeckState != BlinkIconManager.OnDeckState.none;
            if (!onDeck)
            {
                _onDeckSince = now;
                // Some sections (tutorial text, prompts) just wait for any blink.
                if (now - _progressAt > 15f && now >= _nextBlinkAllowed && now - _lastIdleBlink > 10f && (!Thorough || now - _dialogueBusyAt > 3f))
                {
                    _lastIdleBlink = now;
                    Blink("idle");
                }
                return;
            }
            if (counter._canBlink && now - _onDeckSince > HudBlinkAfter && now >= _nextBlinkAllowed && ReadyToClose(now))
            {
                LogUntouched();
                Blink("scene");
                _onDeckSince = now;
            }
        }
    }
}
