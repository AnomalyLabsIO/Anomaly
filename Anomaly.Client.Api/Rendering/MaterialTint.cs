using System.Collections.Generic;
using UnityEngine;

namespace Anomaly.Client.Api.Rendering;

/// <summary>
/// Reusable HDRP_Lit base-color tinting via <see cref="MaterialPropertyBlock"/>.
/// Features such as sub-role differentiation, item state indicators, and
/// faction flair can apply consistent per-renderer tints without
/// touching shared materials or instantiating per-renderer material copies.
/// </summary>
/// <remarks>
/// <para>
/// HDRP_Lit samples <c>_BaseColorMap * _BaseColor</c>. With a heavily colored
/// base map (e.g. an orange texture) the multiply alone cannot
/// produce cool colors — picking blue against an orange map gives black. For
/// faithful arbitrary-color reproduction the caller must also supply a
/// luminance-preserving but chroma-flattened version of the base map; the
/// <see cref="Apply(Renderer, Color, Texture)"/> overloads accept it.
/// </para>
/// <para>
/// The helper is stateless. Consumers own "what color is currently applied"
/// state. <see cref="Clear(Renderer)"/> drops every MPB override on the
/// renderer (it calls <c>SetPropertyBlock(null)</c>); if a future caller
/// needs to compose with another MPB user on the same renderer, this helper
/// will need a more granular reset path.
/// </para>
/// </remarks>
public static class MaterialTint
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int BaseColorMapId = Shader.PropertyToID("_BaseColorMap");

    // Reused across calls. MaterialPropertyBlock is a managed wrapper around an
    // unmanaged buffer; instantiating fresh per call would be wasteful even at
    // the call rates we expect here.
    private static readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();

    /// <summary>
    /// Multiplies <paramref name="color"/> into the renderer's <c>_BaseColor</c>
    /// without touching the underlying texture. Best for materials whose base
    /// map is already neutral / grayscale.
    /// </summary>
    public static void Apply(Renderer renderer, Color color)
    {
        if (renderer == null)
            return;

        renderer.GetPropertyBlock(_block);
        _block.SetColor(BaseColorId, color);
        renderer.SetPropertyBlock(_block);
    }

    /// <inheritdoc cref="Apply(Renderer, Color)" />
    public static void Apply(IEnumerable<Renderer> renderers, Color color)
    {
        if (renderers == null)
            return;

        foreach (var r in renderers)
            Apply(r, color);
    }

    /// <summary>
    /// Multiplies <paramref name="color"/> into the renderer's <c>_BaseColor</c>
    /// and overrides <c>_BaseColorMap</c> with <paramref name="neutralBaseMap"/>.
    /// Use when the underlying material's base texture has a strong color cast
    /// (e.g. an orange texture) and you want any chosen color to land
    /// faithfully across the full hue range. The caller is responsible for
    /// supplying a luminance-preserving, chroma-flattened version of the
    /// material's original base map.
    /// </summary>
    public static void Apply(Renderer renderer, Color color, Texture neutralBaseMap)
    {
        if (renderer == null)
            return;

        renderer.GetPropertyBlock(_block);
        _block.SetColor(BaseColorId, color);
        if (neutralBaseMap != null)
            _block.SetTexture(BaseColorMapId, neutralBaseMap);
        renderer.SetPropertyBlock(_block);
    }

    /// <inheritdoc cref="Apply(Renderer, Color, Texture)" />
    public static void Apply(IEnumerable<Renderer> renderers, Color color, Texture neutralBaseMap)
    {
        if (renderers == null)
            return;

        foreach (var r in renderers)
            Apply(r, color, neutralBaseMap);
    }

    /// <summary>
    /// Drops every MPB override on the renderer, restoring the shared
    /// material's stock values. Call when a tinted role/state ends so the
    /// pooled CharacterModel doesn't carry the override into its next owner.
    /// </summary>
    public static void Clear(Renderer renderer)
    {
        if (renderer == null)
            return;

        renderer.SetPropertyBlock(null);
    }

    /// <inheritdoc cref="Clear(Renderer)" />
    public static void Clear(IEnumerable<Renderer> renderers)
    {
        if (renderers == null)
            return;

        foreach (var r in renderers)
            Clear(r);
    }
}
