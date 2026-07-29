namespace Anomaly.Server.Content
{
    /// <summary>
    /// Declarative definition of a custom status effect. Materialized client-side as one
    /// generic data-driven StatusEffectBase parameterized by these values (no shipped code).
    /// The pilot supports a single movement-speed facet.
    /// </summary>
    public sealed class CustomEffectDefinition
    {
        /// <summary>Namespaced stable key, e.g. "mymod:lucky". Required.</summary>
        public string Key { get; set; }

        /// <summary>Display name surfaced via ICustomDisplayName / spectator text.</summary>
        public string DisplayName { get; set; } = "";

        /// <summary>Game EffectClassification: 0=Negative, 1=Positive, 2=Mixed, 3=Technical.</summary>
        public byte Classification { get; set; } = 1;

        /// <summary>Movement-speed multiplier applied while the effect is enabled (pilot facet).</summary>
        public float SpeedMultiplier { get; set; } = 1f;
    }
}
