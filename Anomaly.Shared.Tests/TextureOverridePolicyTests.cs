using System;
using System.IO;
using Anomaly.Shared.Assets;
using Anomaly.Shared.Networking.Messages;
using Xunit;

namespace Anomaly.Shared.Tests;

public sealed class TextureOverridePolicyTests
{
    [Theory]
    [InlineData(0u, 0)]
    [InlineData(1u, -1)]
    [InlineData(1u, 65536)]
    public void Invalid_target_is_rejected_before_state_can_be_published(uint netId, int material)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TextureOverridePolicy.ValidateTarget(netId, "", material));
    }

    [Fact]
    public void Path_bound_is_utf8_bytes_and_rejects_invalid_unicode()
    {
        TextureOverridePolicy.ValidateTarget(1, new string('\u00e9', 512), ushort.MaxValue);
        Assert.Throws<ArgumentException>(() => TextureOverridePolicy.ValidateTarget(1, new string('\u00e9', 513), 0));
        Assert.Throws<System.Text.EncoderFallbackException>(() => TextureOverridePolicy.ValidateTarget(1, "\ud800", 0));
    }

    [Fact]
    public void Capacity_allows_replacement_but_rejects_new_keys_at_either_bound()
    {
        Assert.True(TextureOverridePolicy.CanStore(false, 1023, 31));
        Assert.False(TextureOverridePolicy.CanStore(false, 1024, 0));
        Assert.False(TextureOverridePolicy.CanStore(false, 32, 32));
        Assert.True(TextureOverridePolicy.CanStore(true, 1024, 32));
    }

    [Fact]
    public void Invalid_texture_target_does_not_write_a_partial_message()
    {
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output);
        var message = new AssetOverrideTextureMessage { NetId = 0, TextureAddress = "mods/example/textures/item" };
        Assert.Throws<ArgumentOutOfRangeException>(() => message.Serialize(writer));
        Assert.Equal(0, output.Length);
    }
}
