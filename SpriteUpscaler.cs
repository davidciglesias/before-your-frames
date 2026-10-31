using System.Collections.Generic;
using UnityEngine;

namespace BYEFrameBlink
{
    /// <summary>
    /// The camera device's viewfinder frame is hand-drawn line art (sprites of ~680x380 px on a 2048x768 sheet), sized
    /// for a flat screen. Drawn across much of a headset's view it is heavily magnified and blurry. Make a sharper copy
    /// of the sheet once (upscaled, with the alpha edges of the strokes re-sharpened, mipmapped) and swap each frame the
    /// game's animator shows for its copy.
    /// </summary>
    internal static class SpriteUpscaler
    {
        private const int Scale = 3;
        private static readonly Dictionary<Sprite, Sprite> Map = new Dictionary<Sprite, Sprite>();
        private static readonly Dictionary<Texture, Texture2D> Sheets = new Dictionary<Texture, Texture2D>();

        public static void Apply(Transform root)
        {
            if (!Plugin.CameraDeviceSharpFrame.Value) return;
            foreach (SpriteRenderer sr in root.GetComponentsInChildren<SpriteRenderer>())
            {
                Sprite s = sr.sprite;
                if (s == null || s.texture == null || s.texture.name != "CameraOverlay") continue;
                if (!Map.TryGetValue(s, out Sprite sharp))
                {
                    sharp = Make(s);
                    Map[s] = sharp;
                }
                if (sharp != null) sr.sprite = sharp;
            }
        }

        private static Sprite Make(Sprite s)
        {
            Texture2D sheet = Sheet(s.texture);
            if (sheet == null) return null;
            Rect r = s.rect;
            var rect = new Rect(r.x * Scale, r.y * Scale, r.width * Scale, r.height * Scale);
            var pivot = new Vector2(s.pivot.x / r.width, s.pivot.y / r.height);
            return Sprite.Create(sheet, rect, pivot, s.pixelsPerUnit * Scale, 0, SpriteMeshType.FullRect);
        }

        private static Texture2D Sheet(Texture2D src)
        {
            if (Sheets.TryGetValue(src, out Texture2D done)) return done;
            int w = src.width * Scale, h = src.height * Scale;
            RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            FilterMode old = src.filterMode;
            src.filterMode = FilterMode.Bilinear;
            Graphics.Blit(src, rt);
            src.filterMode = old;
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);

            // Re-sharpen the strokes' edges: the bilinear upscale left them soft.
            Color32[] px = tex.GetPixels32();
            for (int i = 0; i < px.Length; i++)
            {
                float a = px[i].a / 255f;
                a = Mathf.Clamp01((a - 0.4f) / 0.2f);
                a = a * a * (3f - 2f * a);
                px[i].a = (byte)(a * 255f);
            }
            tex.SetPixels32(px);
            tex.Apply(true, true);
            tex.filterMode = FilterMode.Trilinear;
            tex.anisoLevel = 8;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.name = src.name + " (sharp)";
            Sheets[src] = tex;
            Plugin.Log.LogInfo($"[camdevice] sharpened {src.name} {src.width}x{src.height} -> {w}x{h}");
            return tex;
        }
    }
}
