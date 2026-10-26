using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;
using Anomaly.Shared.Scripting;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Anomaly.Shared.Tests;

public sealed class ScriptMessageContractTests
{
    [Fact]
    public void PrimitiveDtoRoundTripsThroughImmutableRecordAndBothScopedDirections()
    {
        var dto = new State { RequestId = uint.MaxValue, Count = int.MinValue, Enabled = true, Value = 1.25, Label = "é😀" };
        ScriptMessagePayload payload = ScriptMessagePayload.Create(dto);
        dto.Label = "Changed";
        Assert.Equal("é😀", payload.Values["Label"]);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, object>)payload.Values).Add("Other", true));
        Assert.True(payload.TryRead<State>(out var copied));
        Assert.Equal(uint.MaxValue, copied.RequestId);
        Assert.Equal(int.MinValue, copied.Count);
        Assert.Equal("é😀", copied.Label);
        var client = ScriptToClientMessage.Create("example:panel", "State", payload).Bind(new string('a', 64), 42);
        var server = ScriptToServerMessage.Create("example:panel", "State", copied).Bind(new string('a', 64), 42);
        foreach (IAnomalyMessage message in new IAnomalyMessage[] { client, server })
        {
            IAnomalyMessage decoded = message is ScriptToClientMessage ? new ScriptToClientMessage() : new ScriptToServerMessage();
            byte[] bytes = Serialize(message);
            using var reader = new BinaryReader(new MemoryStream(bytes));
            decoded.Deserialize(reader);
            var envelope = (IScriptMessage)decoded;
            Assert.Equal("example:panel", envelope.PackId);
            Assert.Equal("State", envelope.Name);
            Assert.Equal(new string('a', 64), envelope.IndexSha256);
            Assert.Equal(42UL, envelope.ActivationId);
            Assert.Equal(bytes.Length, reader.BaseStream.Position);
            Assert.True(envelope.Payload.TryRead<State>(out var output));
            Assert.Equal(1.25, output.Value);
            Assert.True(output.Enabled);
            Assert.Equal(bytes, Serialize(decoded));
            Assert.True(bytes.Length <= message.MaxPayloadBytes);
            Assert.Equal(MessageChannel.ReliableOrdered, message.TransportChannel);
        }
        Assert.Equal(AnomalyMessageDirection.ServerToClient, client.Direction);
        Assert.Equal(AnomalyMessageDirection.ClientToServer, server.Direction);
    }

    [Fact]
    public void EnvelopesAreBuiltInAndDirectionIsPinned()
    {
        AnomalyMessageRegistry.RegisterDefaults();
        Assert.True(AnomalyMessageRegistry.TryGetPolicy(AnomalyMessageRegistry.GetHash("anomaly:script:client"), out var client));
        Assert.True(AnomalyMessageRegistry.TryGetPolicy(AnomalyMessageRegistry.GetHash("anomaly:script:server"), out var server));
        Assert.Equal(AnomalyMessageDirection.ServerToClient, client.Direction);
        Assert.Equal(AnomalyMessageDirection.ClientToServer, server.Direction);
        Assert.Equal(AnomalyMessageAuthPhase.AuthenticatedSession, client.AuthPhase);
        Assert.False(client.IsRoundScoped);
    }

    [Theory]
    [InlineData("")]
    [InlineData("state")]
    [InlineData("Foo.Bar")]
    [InlineData("État")]
    [InlineData("Bad\0Name")]
    public void NamesAreBoundedCSharpIdentifiers(string name)
    {
        Assert.False(ScriptMessageSchema.IsName(name));
        Assert.Throws<InvalidDataException>(() => ScriptToClientMessage.Create("example:panel", name, new Empty()));
        Assert.Throws<InvalidDataException>(() => ScriptMessagePayload.FromValues(new Dictionary<string, object> { [name] = true }));
    }

    [Fact]
    public void UnboundOrInvalidScopeCannotSerialize()
    {
        var message = ScriptToClientMessage.Create("example:panel", "State", new Empty());
        Assert.Throws<InvalidDataException>(() => Serialize(message));
        Assert.Throws<InvalidDataException>(() => message.Bind(new string('A', 64), 1));
        Assert.Throws<InvalidDataException>(() => message.Bind(new string('0', 64), 1));
        Assert.Throws<InvalidDataException>(() => message.Bind(new string('a', 64), 0));
        Assert.Throws<InvalidDataException>(() => ScriptToServerMessage.Create("invalid", "State", new Empty()));
        Assert.Equal(string.Empty, message.IndexSha256);
        Assert.Equal(0UL, message.ActivationId);
    }

    [Theory]
    [InlineData("nested")]
    [InlineData("array")]
    [InlineData("null")]
    [InlineData("long")]
    [InlineData("decimal")]
    [InlineData("nan")]
    [InlineData("infinite")]
    [InlineData("nul")]
    [InlineData("surrogate")]
    [InlineData("utf8-limit")]
    public void NonPrimitiveOrInvalidValuesFailBeforeEncoding(string invalid)
    {
        object value = invalid switch
        {
            "nested" => new Empty(), "array" => new int[0], "null" => null, "long" => 1L, "decimal" => 1m,
            "nan" => double.NaN, "infinite" => double.PositiveInfinity, "nul" => "a\0b", "surrogate" => "\ud800",
            _ => new string('é', 513),
        };
        Assert.Throws<InvalidDataException>(() => ScriptMessagePayload.FromValues(new Dictionary<string, object> { ["Value"] = value }));
    }

    [Fact]
    public void PayloadCapsApplyToCountAndAggregateBytesAndDefensivelyCopyInput()
    {
        var values = new Dictionary<string, object> { ["Value"] = new string('é', 512) };
        ScriptMessagePayload payload = ScriptMessagePayload.FromValues(values);
        values["Value"] = "mutated";
        Assert.Equal(new string('é', 512), payload.Values["Value"]);
        var many = Enumerable.Range(0, 33).ToDictionary(i => "Field" + i, _ => (object)true);
        Assert.Throws<InvalidDataException>(() => ScriptMessagePayload.FromValues(many));
        var misleading = new MisleadingCount();
        foreach (var pair in many) misleading.Add(pair.Key, pair.Value);
        Assert.Throws<InvalidDataException>(() => ScriptMessagePayload.FromValues(misleading));
        var large = Enumerable.Range(0, 4).ToDictionary(i => "Field" + i, _ => (object)new string('x', 1024));
        Assert.Throws<InvalidDataException>(() => ScriptMessagePayload.FromValues(large));
        Assert.Throws<InvalidDataException>(() => ScriptMessagePayload.Create(new Nested()));
        Assert.Throws<InvalidDataException>(() => ScriptMessagePayload.Create(new GetterOnly()));
        Assert.False(payload.TryRead<Empty>(out _));
        Assert.False(payload.TryRead<Nested>(out _));
        Assert.False(payload.TryRead<OtherName>(out _));
    }

    [Fact]
    public void SchemaCloneDoesNotShareMutableFieldsAndRequiresExactTypesAndFields()
    {
        ScriptMessageSchema schema = Schema();
        ScriptMessageSchema clone = schema.Clone();
        schema.Name = "Changed";
        schema.Fields[0].Name = "Changed";
        schema.Fields.Clear();
        Assert.Equal("State", clone.Name);
        Assert.Equal("Enabled", clone.Fields[0].Name);
        var good = ScriptMessagePayload.FromValues(new Dictionary<string, object> { ["Enabled"] = true });
        clone.ValidatePayload(good);
        foreach (var invalid in new[]
        {
            new Dictionary<string, object>(), new Dictionary<string, object> { ["Enabled"] = 1 },
            new Dictionary<string, object> { ["Enabled"] = true, ["Other"] = 1 },
        }) Assert.Throws<InvalidDataException>(() => clone.ValidatePayload(ScriptMessagePayload.FromValues(invalid)));
        var text = new ScriptMessageSchema { Name = "Text", Direction = AnomalyMessageDirection.ServerToClient,
            Fields = new List<ScriptMessageField> { new ScriptMessageField { Name = "Label", Type = "String", MaxBytes = 2 } } };
        text.ValidatePayload(ScriptMessagePayload.FromValues(new Dictionary<string, object> { ["Label"] = "é" }));
        Assert.Throws<InvalidDataException>(() => text.ValidatePayload(ScriptMessagePayload.FromValues(new Dictionary<string, object> { ["Label"] = "éé" })));
    }

    [Fact]
    public void DtoMappingDoesNotHideFatalHostExceptions()
    {
        ScriptMessagePayload payload = ScriptMessagePayload.FromValues(new Dictionary<string, object> { ["Enabled"] = true });
        Exception setter = Assert.Throws<System.Reflection.TargetInvocationException>(() => payload.TryRead<FatalSetter>(out _));
        Assert.IsType<OutOfMemoryException>(setter.InnerException);
        Exception constructor = Assert.ThrowsAny<Exception>(() => payload.TryRead<FatalConstructor>(out _));
        Assert.IsType<OutOfMemoryException>(constructor.GetBaseException());
        Assert.False(payload.TryRead<OrdinarySetter>(out _));
    }

    [Theory]
    [InlineData("direction")]
    [InlineData("version")]
    [InlineData("type")]
    [InlineData("duplicate")]
    [InlineData("name")]
    [InlineData("nonstring-limit")]
    [InlineData("string-limit")]
    [InlineData("field-count")]
    public void SchemaValidationRejectsAmbiguousOrUnboundedDefinitions(string invalid)
    {
        var schema = Schema();
        switch (invalid)
        {
            case "direction": schema.Direction = AnomalyMessageDirection.Bidirectional; break;
            case "version": schema.Version = 2; break;
            case "type": schema.Fields[0].Type = "Table"; break;
            case "duplicate": schema.Fields.Add(schema.Fields[0]); break;
            case "name": schema.Name = new string('X', 65); break;
            case "nonstring-limit": schema.Fields[0].MaxBytes = 1; break;
            case "string-limit": schema.Fields[0].Type = "String"; schema.Fields[0].MaxBytes = 1025; break;
            case "field-count": schema.Fields = Enumerable.Range(0, 33).Select(i => new ScriptMessageField { Name = "Field" + i, Type = "Boolean" }).ToList(); break;
        }
        Assert.Throws<InvalidDataException>(schema.Validate);
    }

    [Fact]
    public void MessageSchemasBelongToVersionedSourcePacks()
    {
        var index = Pack();
        index.Messages.Add(Schema());
        Assert.Single(ScriptPackIndexCodec.Parse(Json(index)).Messages);
        index.ApiVersion++;
        Assert.Throws<InvalidDataException>(() => ScriptPackIndexCodec.Validate(index));
    }

    [Fact]
    public void IndexSchemasRequireSourcesAndExactBoundedJson()
    {
        var index = Pack(); index.Messages.Add(Schema());
        Assert.Equal("State", ScriptPackIndexCodec.Parse(Json(index)).Messages[0].Name);
        index.Messages.Add(Schema());
        Assert.Throws<InvalidDataException>(() => ScriptPackIndexCodec.Validate(index));
        index.Messages.RemoveAt(1);
        foreach (string property in new[] { "Nested", "Direction", "Version", "Fields" })
        {
            JObject json = JObject.Parse(Encoding.UTF8.GetString(Json(index)));
            JObject schema = (JObject)json["Messages"][0];
            if (property == "Nested") schema[property] = "bad";
            else if (property == "Direction") schema[property] = "Bidirectional";
            else if (property == "Version") schema[property] = 65537;
            else ((JObject)schema["Fields"][0])["Optional"] = true;
            Assert.Throws<InvalidDataException>(() => ScriptPackIndexCodec.Parse(Encoding.UTF8.GetBytes(json.ToString())));
        }
        index.Sources.Clear();
        Assert.Throws<InvalidDataException>(() => ScriptPackIndexCodec.Validate(index));
    }

    [Fact]
    public void PayloadDecoderRejectsTruncationUnknownTagsDuplicateKeysTrailingAndNoncanonicalBoolean()
    {
        byte[] valid = Serialize(ScriptToClientMessage.Create("example:panel", "State", new EnabledOnly { Enabled = true }).Bind(new string('a', 64), 42));
        for (int length = 0; length < valid.Length; length++)
        {
            byte[] truncated = valid.Take(length).ToArray();
            Assert.ThrowsAny<Exception>(() => new ScriptToClientMessage().Deserialize(new BinaryReader(new MemoryStream(truncated))));
        }
        var malformed = new List<byte[]>();
        byte[] wrongTag = (byte[])valid.Clone(); wrongTag[wrongTag.Length - 2] = 255; malformed.Add(wrongTag);
        byte[] wrongBool = (byte[])valid.Clone(); wrongBool[wrongBool.Length - 1] = 2; malformed.Add(wrongBool);
        foreach (byte[] bytes in malformed)
            Assert.Throws<InvalidDataException>(() => new ScriptToClientMessage().Deserialize(new BinaryReader(new MemoryStream(bytes))));
        Assert.Throws<InvalidDataException>(() => ScriptMessagePayload.Read(new BinaryReader(new MemoryStream(new byte[] { 0, 1 })), 2));
        Assert.Throws<InvalidDataException>(() => ScriptMessagePayload.Read(new BinaryReader(new MemoryStream(new byte[] { 33 })), 1));
        Assert.Throws<InvalidDataException>(() => ScriptMessagePayload.Read(new BinaryReader(new MemoryStream()), 4097));
        using var duplicateStream = new MemoryStream();
        using var writer = new BinaryWriter(duplicateStream, Encoding.UTF8, true);
        writer.Write((byte)2);
        for (int i = 0; i < 2; i++) { writer.Write((byte)1); writer.Write((byte)'A'); writer.Write((byte)1); writer.Write((byte)1); }
        writer.Flush(); duplicateStream.Position = 0;
        Assert.Throws<InvalidDataException>(() => ScriptMessagePayload.Read(new BinaryReader(duplicateStream), (int)duplicateStream.Length));
    }

    [Theory]
    [InlineData("nonfinite")]
    [InlineData("invalid-utf8")]
    [InlineData("nul")]
    [InlineData("string-length")]
    [InlineData("field-length")]
    public void PayloadDecoderChecksScalarValuesBeforePublishing(string invalid)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write((byte)1);
        writer.Write((byte)(invalid == "field-length" ? 65 : 1));
        writer.Write((byte)'A');
        if (invalid == "nonfinite") { writer.Write((byte)4); writer.Write(double.NaN); }
        else
        {
            writer.Write((byte)5);
            writer.Write((ushort)(invalid == "string-length" ? 1025 : 1));
            writer.Write((byte)(invalid == "invalid-utf8" ? 255 : 0));
        }
        writer.Flush(); stream.Position = 0;
        Assert.Throws<InvalidDataException>(() => ScriptMessagePayload.Read(new BinaryReader(stream), (int)stream.Length));
    }

    private static ScriptMessageSchema Schema() => new ScriptMessageSchema { Name = "State", Direction = AnomalyMessageDirection.ServerToClient,
        Fields = new List<ScriptMessageField> { new ScriptMessageField { Name = "Enabled", Type = "Boolean" } } };
    private static ScriptPackIndex Pack() => new ScriptPackIndex
    {
        PackId = "example:panel",
        Sources = new List<ScriptPackSource> { new ScriptPackSource { Address = "mods/example/main.lua", SourceBytes = 0, SourceSha256 = ScriptSourceValidator.EmptySha256Hex } },
    };
    private static byte[] Json(ScriptPackIndex index) => ScriptPackIndexCodec.Encode(index);
    private static byte[] Serialize(IAnomalyMessage message)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        message.Serialize(writer); writer.Flush(); return stream.ToArray();
    }
    public sealed class Empty { }
    public sealed class State
    {
        public uint RequestId { get; set; }
        public int Count;
        public bool Enabled { get; set; }
        public double Value { get; set; }
        public string Label { get; set; }
    }
    public sealed class EnabledOnly { public bool Enabled { get; set; } }
    public sealed class Nested { public Empty Value { get; set; } }
    public sealed class GetterOnly { public bool Enabled => true; }
    public sealed class OtherName { public string Other { get; set; } }
    public sealed class FatalSetter { public bool Enabled { get => true; set => throw new OutOfMemoryException("Synthetic test exception"); } }
    public sealed class FatalConstructor
    {
        public FatalConstructor() => throw new OutOfMemoryException("Synthetic test exception");
        public bool Enabled { get; set; }
    }
    public sealed class OrdinarySetter { public bool Enabled { get => true; set => throw new InvalidOperationException("Synthetic test exception"); } }
    private sealed class MisleadingCount : Dictionary<string, object>, IReadOnlyDictionary<string, object>
    {
        int IReadOnlyCollection<KeyValuePair<string, object>>.Count => 0;
    }
}
