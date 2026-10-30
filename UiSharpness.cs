using UnityEngine;

namespace BYEFrameBlink
{
    /// <summary>
    /// UUVR draws the menus/HUD into a screen-sized texture (2560x1440 here) shown on a quad about 50-70 degrees wide,
    /// about twice as many texture pixels per degree as the headset shows. Without mipmaps the quad samples it by
    /// skipping pixels, so thin text breaks up and shimmers (looks low-res). This swaps in a same-size texture with
    /// mipmaps and trilinear/anisotropic filtering; same size, so gaze and mouse coordinates don't change.
    /// Only in UUVR's CanvasRedirect mode (its capture camera renders into the quad's texture).
    /// </summary>
    internal static class UiSharpness
    {
        private static float _nextCheck;

        public static bool Applied { get; private set; }

        public static void LateUpdate()
        {
            if (!Plugin.SmoothUi.Value || !UnityEngine.XR.XRSettings.enabled || Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + 1f;

            GameObject quad = GameObject.Find("VrUiQuad");
            Renderer renderer = quad != null ? quad.GetComponent<Renderer>() : null;
            var current = renderer != null ? renderer.sharedMaterial.mainTexture as RenderTexture : null;
            if (current == null) return;
            if (current.useMipMap) { Applied = true; return; }

            Camera capture = null;
            foreach (Camera c in Camera.allCameras)
                if (c.targetTexture == current) { capture = c; break; }
            if (capture == null) return;

            var sharp = new RenderTexture(current.width, current.height, 16, RenderTextureFormat.ARGB32)
            {
                name = "BYE Sharp UI",
                useMipMap = true,
                autoGenerateMips = true,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 8,
                wrapMode = TextureWrapMode.Clamp,
            };
            sharp.Create();
            capture.targetTexture = sharp;
            renderer.material.mainTexture = sharp;
            current.Release();
            Applied = true;
            Plugin.Log.LogInfo($"UI panel texture {sharp.width}x{sharp.height} now mipmapped (sharper text in the headset).");
        }
    }
}
