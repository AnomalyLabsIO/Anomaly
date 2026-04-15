using System;
using System.IO;

namespace Anomaly.Shared.Networking
{
    /// <summary>
    /// Minimal IEEE 754 half-precision float conversion.
    /// Used on both client and server so the wire format stays identical
    /// regardless of whether the runtime has native <c>System.Half</c> support.
    /// </summary>
    public static class HalfHelper
    {
        public static ushort FloatToHalf(float value)
        {
            int bits = BitConverter.ToInt32(BitConverter.GetBytes(value), 0);
            int sign = (bits >> 16) & 0x8000;
            int exponent = ((bits >> 23) & 0xFF) - 127 + 15;
            int mantissa = bits & 0x7FFFFF;

            if (exponent <= 0)
            {
                if (exponent < -10)
                    return (ushort)sign;
                mantissa |= 0x800000;
                int shift = 14 - exponent;
                mantissa >>= shift;
                return (ushort)(sign | mantissa);
            }

            if (exponent >= 31)
                return (ushort)(sign | 0x7C00); // Infinity

            return (ushort)(sign | (exponent << 10) | (mantissa >> 13));
        }

        public static float HalfToFloat(ushort half)
        {
            int sign = (half >> 15) & 1;
            int exponent = (half >> 10) & 0x1F;
            int mantissa = half & 0x3FF;

            int floatBits;
            if (exponent == 0)
            {
                if (mantissa == 0)
                    floatBits = sign << 31;
                else
                {
                    exponent = 1;
                    while ((mantissa & 0x400) == 0)
                    {
                        mantissa <<= 1;
                        exponent--;
                    }
                    mantissa &= 0x3FF;
                    floatBits = (sign << 31) | ((exponent + 127 - 15) << 23) | (mantissa << 13);
                }
            }
            else if (exponent == 31)
                floatBits = (sign << 31) | 0x7F800000 | (mantissa << 13);
            else
                floatBits = (sign << 31) | ((exponent + 127 - 15) << 23) | (mantissa << 13);

            return BitConverter.ToSingle(BitConverter.GetBytes(floatBits), 0);
        }

        public static void WriteHalf(BinaryWriter writer, float value)
        {
            writer.Write(FloatToHalf(value));
        }

        public static float ReadHalf(BinaryReader reader)
        {
            return HalfToFloat(reader.ReadUInt16());
        }
    }
}
