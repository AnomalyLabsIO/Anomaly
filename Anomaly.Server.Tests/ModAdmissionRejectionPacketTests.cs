using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Anomaly.Shared.Auth;
using Xunit;

namespace Anomaly.Server.Tests;

public sealed class ModAdmissionRejectionPacketTests
{
    // The target LiteNetLib 14.2.7 NetPacket header for PacketProperty.Disconnect
    // is 9 bytes. The smallest receive MTU is 508 bytes.
    private const int DisconnectHeaderBytes = 9;
    private const int MinimumPacketMtu = 508;
    // The target game's RejectionReason.Custom is byte 10.
    private const byte CustomRejectionReason = 10;

    [Fact]
    public void CustomRejectionFitsMinimumMtuForMaximumUnicodeIssues()
    {
        var maximumIssues = Enumerable.Range(0, ClientModInventory.MaxEntries)
            .Select(index => new ModAdmissionIssue(
                "wrong_version",
                "m" + index.ToString("D2") + new string('界', 41),
                new string('界', 42),
                new string('界', 16)))
            .ToArray();

        AssertFitsCustomRejection(maximumIssues);

        var unicodeIssues = Enumerable.Range(0, ClientModInventory.MaxEntries)
            .Select(index => new ModAdmissionIssue(
                "wrong_version",
                "mod" + index.ToString("D2") + new string('界', 10),
                "1.0",
                "0.9"))
            .ToArray();

        string response = AssertFitsCustomRejection(unicodeIssues);
        Assert.True(ModAdmissionResponseCodec.TryDecode(response, out var decoded));
        Assert.NotEmpty(decoded.Issues);
        Assert.True(decoded.OmittedCount > 0);
    }

    private static string AssertFitsCustomRejection(IReadOnlyList<ModAdmissionIssue> issues)
    {
        string response = ModAdmissionResponseCodec.EncodeBounded(issues);
        AssertFitsCustomRejection(response);
        return response;
    }

    [Fact]
    public void IntegrityRejectionFitsTargetGameDisconnectPacket()
    {
        string response = ClientIntegrityResponseCodec.Encode(ClientIntegrityAudit.KnownFlags);
        AssertFitsCustomRejection(response);
        Assert.True(ClientIntegrityResponseCodec.TryDecode(response, out var decoded));
        Assert.Equal(ClientIntegrityAudit.KnownFlags, decoded.Flags);
    }

    private static void AssertFitsCustomRejection(string response)
    {
        var gameAssemblyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assembly-CSharp.dll");
        var gameAssembly = Assembly.LoadFrom(gameAssemblyPath);
        var rejectionType = gameAssembly.GetType("RejectionReason", throwOnError: true);
        Assert.Equal(CustomRejectionReason, Convert.ToByte(Enum.Parse(rejectionType, "Custom")));
        var writerType = gameAssembly.GetType("LiteNetLib.Utils.NetDataWriter", throwOnError: true);
        var writer = Activator.CreateInstance(writerType);
        // Invoke the exact overloads because the game assembly also declares Span
        // overloads that the net48 test compiler cannot resolve.
        FindPutOverload(writerType, typeof(byte))
            .Invoke(writer, new object[] { CustomRejectionReason });
        // The runtime exposes Put(string, int) with an optional maxLength of zero,
        // which is the overload used by the production Put(string) call.
        FindPutOverload(writerType, typeof(string), typeof(int))
            .Invoke(writer, new object[] { response, 0 });

        var data = (byte[])writerType.GetProperty("Data").GetValue(writer);
        var length = (int)writerType.GetProperty("Length").GetValue(writer);
        Assert.Equal(CustomRejectionReason, data[0]);
        Assert.True(length + DisconnectHeaderBytes < MinimumPacketMtu,
            $"Custom rejection was {length + DisconnectHeaderBytes} bytes, at or above {MinimumPacketMtu}.");
    }

    private static MethodInfo FindPutOverload(Type writerType, params Type[] parameterTypes)
    {
        foreach (var method in writerType.GetMethods())
        {
            if (method.Name != "Put")
                continue;

            ParameterInfo[] parameters;
            try
            {
                parameters = method.GetParameters();
            }
            catch (TypeLoadException)
            {
                // The game's ReadOnlySpan overload cannot load under net48.
                continue;
            }

            if (parameters.Length == parameterTypes.Length &&
                parameters.Select(parameter => parameter.ParameterType).SequenceEqual(parameterTypes))
                return method;
        }

        throw new MissingMethodException(writerType.FullName,
            "Put(" + string.Join(",", parameterTypes.Select(type => type.Name)) + ")");
    }
}
