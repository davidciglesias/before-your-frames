using UnityEngine;
using System;
using System.Collections.Generic;
using Valve.VR;

namespace BYEFrameBlink
{
    /// <summary>
    /// Steam Frame (and other SteamVR) controller shortcuts, read through OpenVR's legacy input
    /// rather than the newer action-based Input system: that's the compatibility layer SteamVR keeps
    /// for pre-2019, point-and-click Unity games like this one, so it needs no action manifest of our
    /// own, and Valve maps it automatically for new controllers (including Frame's). UUVR's XR backend
    /// (XRSDKOpenVR) has already initialized OpenVR in-process by the time this runs, so OpenVR.System
    /// works without us ever calling OpenVR.Init ourselves.
    /// The same buttons do the same thing on either controller, so the game is fully playable holding only one:
    ///   Menu Button (Menu slot: Y / D-pad-up on Frame)  -> open/close the pause menu (or back out of VR settings)
    ///   Blink Button (A slot)                           -> held = eyes closed (play without eye tracking)
    ///   Thumbstick/pad click                            -> recenter view
    ///   Grip                                            -> cycle eye separation ("world size")
    ///   Trigger                                         -> show/hide the aiming dot
    /// </summary>
    internal static class ControllerShortcuts
    {
        private static ulong _prevLeft, _prevRight;
        private static bool _warnedOnce;

        /// <summary>The Blink Button is held on either controller (read by BlinkState as closed eyes).</summary>
        public static bool BlinkHeld { get; private set; }

        /// <summary>Debug/tests: act as if the Blink Button were held.</summary>
        public static bool SimulatedBlinkHeld;

        public static void Update()
        {
            BlinkHeld = SimulatedBlinkHeld;
            if (!UnityEngine.XR.XRSettings.enabled) return;
            CVRSystem system = TryGetSystem();
            if (system == null) return;
            PollHand(system, ETrackedControllerRole.LeftHand, ref _prevLeft);
            PollHand(system, ETrackedControllerRole.RightHand, ref _prevRight);
        }

        private static ulong Bit(ControllerButton b)
        {
            switch (b)
            {
                case ControllerButton.Menu: return 1UL << (int)EVRButtonId.k_EButton_ApplicationMenu;
                case ControllerButton.A: return 1UL << (int)EVRButtonId.k_EButton_A;
                case ControllerButton.Grip: return 1UL << (int)EVRButtonId.k_EButton_Grip;
                case ControllerButton.Trigger: return 1UL << (int)EVRButtonId.k_EButton_Axis1;
                case ControllerButton.Thumbstick: return 1UL << (int)EVRButtonId.k_EButton_Axis0;
                default: return 0;
            }
        }

        private static CVRSystem TryGetSystem()
        {
            try { return OpenVR.System; }
            catch (Exception e)
            {
                if (!_warnedOnce)
                {
                    _warnedOnce = true;
                    Plugin.Log.LogWarning("OpenVR.System unavailable; controller shortcuts disabled: " + e.Message);
                }
                return null;
            }
        }

        private static void PollHand(CVRSystem system, ETrackedControllerRole role, ref ulong prevMask)
        {
            uint index = system.GetTrackedDeviceIndexForControllerRole(role);
            if (index == OpenVR.k_unTrackedDeviceIndexInvalid) { prevMask = 0; return; }
            var state = default(VRControllerState_t);
            uint size = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(VRControllerState_t));
            if (!system.GetControllerState(index, ref state, size)) return;
            ulong mask = state.ulButtonPressed;
            ulong pressed = mask & ~prevMask; // rising edges only, so a held button fires once
            prevMask = mask;

            ulong menuBit = Bit(Plugin.ControllerMenuButton.Value), blinkBit = Bit(Plugin.ControllerBlinkButton.Value);
            if ((mask & blinkBit) != 0) BlinkHeld = true;
            if (pressed == 0) return;

            // SteamVR's auto-generated legacy bindings map the face buttons to its two old "extra" slots: on Steam Frame,
            // Y / D-pad-up are the Menu slot. The dedicated Menu/Start button next to the Steam button fires neither:
            // the runtime keeps it, like Quest's Home button.
            if ((pressed & menuBit) != 0) TogglePauseMenu();
            ulong taken = menuBit | blinkBit;
            if (Fires(pressed, taken, EVRButtonId.k_EButton_Axis0)) HeadTracking.Recenter();
            if (Fires(pressed, taken, EVRButtonId.k_EButton_Grip)) StereoComfort.CycleEyeSeparation();
            if (Fires(pressed, taken, EVRButtonId.k_EButton_Axis1)) Plugin.ToggleReticle();
        }

        private static bool Fires(ulong pressed, ulong taken, EVRButtonId id) => HasBit(pressed, id) && (taken & (1UL << (int)id)) == 0;

        private static bool HasBit(ulong mask, EVRButtonId id) => (mask & (1UL << (int)id)) != 0;

        private static readonly Dictionary<int, string> KnownBits = new Dictionary<int, string>
        {
            { (int)EVRButtonId.k_EButton_System, "System" },
            { (int)EVRButtonId.k_EButton_ApplicationMenu, "ApplicationMenu (Menu)" },
            { (int)EVRButtonId.k_EButton_Grip, "Grip" },
            { (int)EVRButtonId.k_EButton_A, "A" },
            { (int)EVRButtonId.k_EButton_Axis0, "Axis0 (thumbstick/trackpad click)" },
            { (int)EVRButtonId.k_EButton_Axis1, "Axis1 (trigger)" },
            { (int)EVRButtonId.k_EButton_Axis2, "Axis2" },
            { (int)EVRButtonId.k_EButton_Axis3, "Axis3" },
            { (int)EVRButtonId.k_EButton_Axis4, "Axis4" },
        };

        /// <summary>For /bye/controllers: logs every bit that changed since the last call for one hand, by name
        /// where known. Uses its own prevMask (passed by the caller) so it doesn't interfere with the live shortcuts.</summary>
        public static void DumpChanges(string hand, ref ulong prevMask, ref ulong prevTouched)
        {
            CVRSystem system = TryGetSystem();
            ETrackedControllerRole role = hand == "Left" ? ETrackedControllerRole.LeftHand : ETrackedControllerRole.RightHand;
            uint index = system?.GetTrackedDeviceIndexForControllerRole(role) ?? OpenVR.k_unTrackedDeviceIndexInvalid;
            if (index == OpenVR.k_unTrackedDeviceIndexInvalid) return;
            var state = default(VRControllerState_t);
            uint size = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(VRControllerState_t));
            if (!system.GetControllerState(index, ref state, size)) return;
            DumpMask(hand, "pressed", state.ulButtonPressed, ref prevMask);
            DumpMask(hand, "touched", state.ulButtonTouched, ref prevTouched);
        }

        private static void DumpMask(string hand, string kind, ulong mask, ref ulong prevMask)
        {
            ulong changed = mask ^ prevMask;
            for (int bit = 0; bit < 64 && changed != 0; bit++, changed >>= 1)
            {
                if ((changed & 1) == 0) continue;
                bool nowDown = (mask & (1UL << bit)) != 0;
                string name = KnownBits.TryGetValue(bit, out string n) ? n : $"bit {bit}";
                Plugin.Log.LogInfo($"[cmd] controller {hand}: {name} {kind} {(nowDown ? "on" : "off")}");
            }
            prevMask = mask;
        }

        /// <summary>Mirrors the game's own Escape/P key handling in PauseMenuController.Update.</summary>
        private static float _toggledAt = -10f, _pauseKeyAt = -10f;

        /// <summary>True shortly after a controller toggled the pause menu: the game should ignore Escape/P then.</summary>
        public static bool JustToggled => Time.unscaledTime - _toggledAt < 0.4f;

        /// <summary>Called every frame: remembers when the game itself saw Escape/P, so our toggle doesn't undo it.</summary>
        public static void TrackPauseKeys()
        {
            if (!JustToggled && (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.P))) _pauseKeyAt = Time.unscaledTime;
        }

        private static void TogglePauseMenu()
        {
            if (Time.unscaledTime - _pauseKeyAt < 0.4f) return; // the game already handled this press as Escape
            _toggledAt = Time.unscaledTime;
            if (VrSettingsMenu.Back()) return;
            PauseMenuController pmc = PauseMenuController.instance;
            if (pmc == null) return;
            if (pmc.activeMenu == PauseMenuController.ActiveMenu.None) pmc.Pause();
            else if (pmc.activeMenu == PauseMenuController.ActiveMenu.Pause) pmc.Resume();
            else pmc.GoBack();
        }
    }
}
