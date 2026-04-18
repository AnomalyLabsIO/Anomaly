using System;
using BigGustave;
using UnityEngine;

namespace Anomaly.Client.Assets
{
    /// <summary>
    /// Decodes PNG bytes into a new <see cref="Texture2D"/> via pure-managed code
    /// (<see cref="BigGustave"/>) + <see cref="Texture2D.SetPixels32"/>, avoiding
    /// <c>UnityEngine.ImageConversion.LoadImage</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// In this Il2CppInterop build, every overload of <c>ImageConversion.LoadImage</c>
    /// — including the path exposed by MelonLoader's
    /// <c>UnityEngine.Il2CppImageConversionManager</c> shim — ultimately dispatches
    /// to the managed <c>LoadImage(Texture2D, ReadOnlySpan&lt;byte&gt;, bool)</c>
    /// which calls <c>Il2CppSystem.ReadOnlySpan&lt;byte&gt;.GetPinnableReference</c>.
    /// That method isn't exported in this build and throws
    /// <c>MissingMethodException</c> at runtime.
    /// </para>
    /// <para>
    /// This helper exists so the workaround lives in exactly one place. Used by
    /// <see cref="LocalTextures"/> (user-provided PNGs from disk) and
    /// <see cref="AnomalyResources"/> (server-transferred PNGs in FileCache).
    /// </para>
    /// </remarks>
    internal static class PngLoader
    {
        /// <summary>
        /// Decodes a PNG byte array to a new Texture2D. Caller owns the texture
        /// (must Destroy it when done). Returns null on decode failure; the
        /// caller is expected to log its own context.
        /// </summary>
        public static Texture2D TryDecode(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 8)
                return null;

            Png png;
            try
            {
                png = Png.Open(new System.IO.MemoryStream(bytes, writable: false));
            }
            catch (Exception)
            {
                return null;
            }

            int w = png.Width;
            int h = png.Height;
            if (w <= 0 || h <= 0)
                return null;

            var pixels = new Color32[w * h];

            // BigGustave scans top-to-bottom; Unity Texture2D is bottom-up. Flip Y.
            for (int y = 0; y < h; y++)
            {
                int dstRow = (h - 1 - y) * w;
                for (int x = 0; x < w; x++)
                {
                    var p = png.GetPixel(x, y);
                    pixels[dstRow + x] = new Color32(p.R, p.G, p.B, p.A);
                }
            }

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, mipChain: false);
            tex.SetPixels32(pixels);
            tex.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            return tex;
        }
    }
}
