using System.Collections.Generic;

namespace Anomaly.Server.Features
{
    /// <summary>
    /// In-memory store of every player's chosen jumpsuit tint, keyed by UserId so
    /// the value survives a player's role swaps within a session. Not persisted to
    /// disk: clients always re-send their color on join, so the dictionary
    /// rebuilds naturally after a server restart.
    /// </summary>
    internal static class PlayerColorState
    {
        internal readonly struct ColorRgb
        {
            public readonly byte R;
            public readonly byte G;
            public readonly byte B;

            public ColorRgb(byte r, byte g, byte b)
            {
                R = r;
                G = g;
                B = b;
            }
        }

        private static readonly Dictionary<string, ColorRgb> _byUserId = new Dictionary<string, ColorRgb>();

        internal static void Set(string userId, ColorRgb color)
        {
            if (string.IsNullOrEmpty(userId))
                return;
            _byUserId[userId] = color;
        }

        internal static bool TryGet(string userId, out ColorRgb color)
        {
            if (string.IsNullOrEmpty(userId))
            {
                color = default;
                return false;
            }
            return _byUserId.TryGetValue(userId, out color);
        }

        internal static void Remove(string userId)
        {
            if (string.IsNullOrEmpty(userId))
                return;
            _byUserId.Remove(userId);
        }

        internal static IEnumerable<KeyValuePair<string, ColorRgb>> All => _byUserId;
    }
}
