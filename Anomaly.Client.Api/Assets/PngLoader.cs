using System;
using BigGustave;
using UnityEngine;

namespace Anomaly.Client.Api.Assets
{
    /// <summary>
    /// Bounded PNG decoder used by local and server-transferred texture paths.
    /// Header dimensions and peak working memory are validated before the decoder allocates.
    /// </summary>
    internal static class PngLoader
    {
        public static Texture2D TryDecode(byte[] bytes) => TryDecode(bytes, null);

        internal static Texture2D TryDecode(byte[] bytes, string liveBudgetKey)
        {
            if (!TryReadDimensions(bytes, out int width, out int height))
                return null;

            long pixels;
            long liveBytes;
            long peakBytes;
            try
            {
                pixels = checked((long)width * height);
                liveBytes = checked(pixels * 4L);
                // BigGustave's decoded representation plus the Unity upload array can coexist.
                peakBytes = checked((liveBytes * 2L) + bytes.Length);
            }
            catch (OverflowException)
            {
                return null;
            }

            if (width > RemoteAssetDecodeBudget.MaxTextureDimension ||
                height > RemoteAssetDecodeBudget.MaxTextureDimension ||
                pixels > RemoteAssetDecodeBudget.MaxTexturePixels ||
                bytes.Length > RemoteAssetDecodeBudget.MaxEncodedTextureBytes)
                return null;

            if (!RemoteAssetDecodeBudget.TryReserve(
                    peakBytes,
                    liveBytes,
                    out var reservation,
                    out _))
                return null;

            using (reservation)
            {
                Texture2D texture = null;
                try
                {
                    using var stream = new System.IO.MemoryStream(bytes, writable: false);
                    Png png = Png.Open(stream);
                    if (png.Width != width || png.Height != height)
                        return null;

                    var decodedPixels = new Color32[checked((int)pixels)];
                    for (int y = 0; y < height; y++)
                    {
                        int dstRow = (height - 1 - y) * width;
                        for (int x = 0; x < width; x++)
                        {
                            var pixel = png.GetPixel(x, y);
                            decodedPixels[dstRow + x] = new Color32(pixel.R, pixel.G, pixel.B, pixel.A);
                        }
                    }

                    texture = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false);
                    texture.SetPixels32(decodedPixels);
                    texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
                    reservation.CommitLive(liveBudgetKey);
                    return texture;
                }
                catch (Exception)
                {
                    if (texture != null)
                        UnityEngine.Object.Destroy(texture);
                    return null;
                }
            }
        }

        internal static bool TryReadDimensions(byte[] bytes, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (bytes == null || bytes.Length < 24)
                return false;
            if (bytes[0] != 0x89 || bytes[1] != 0x50 || bytes[2] != 0x4E || bytes[3] != 0x47 ||
                bytes[4] != 0x0D || bytes[5] != 0x0A || bytes[6] != 0x1A || bytes[7] != 0x0A ||
                bytes[12] != (byte)'I' || bytes[13] != (byte)'H' ||
                bytes[14] != (byte)'D' || bytes[15] != (byte)'R')
                return false;

            uint rawWidth = ReadUInt32BigEndian(bytes, 16);
            uint rawHeight = ReadUInt32BigEndian(bytes, 20);
            if (rawWidth == 0 || rawHeight == 0 || rawWidth > int.MaxValue || rawHeight > int.MaxValue)
                return false;
            width = (int)rawWidth;
            height = (int)rawHeight;
            return true;
        }

        private static uint ReadUInt32BigEndian(byte[] bytes, int offset) =>
            ((uint)bytes[offset] << 24) |
            ((uint)bytes[offset + 1] << 16) |
            ((uint)bytes[offset + 2] << 8) |
            bytes[offset + 3];
    }
}
