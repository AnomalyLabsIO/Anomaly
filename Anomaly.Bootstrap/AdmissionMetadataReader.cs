using System.Globalization;
using System.Reflection;
using MelonLoader;

namespace Anomaly.Bootstrap;

internal sealed class DeclaredAdmissionMod
{
    internal Type Type { get; }
    internal string Id { get; }
    internal string Version { get; }

    internal DeclaredAdmissionMod(Type type, string id, string version)
    {
        Type = type;
        Id = id;
        Version = version;
    }
}

internal sealed class InvalidAdmissionMetadataException : Exception
{
    internal InvalidAdmissionMetadataException(string message) : base(message) { }
}

internal static class AdmissionMetadataReader
{
    internal static IReadOnlyList<DeclaredAdmissionMod> Read(Assembly assembly)
    {
        var result = new List<DeclaredAdmissionMod>();
        var attributes = CustomAttributeData.GetCustomAttributes(assembly);
        var idAttributes = attributes.Where(attribute => attribute.AttributeType == typeof(MelonIDAttribute)).ToArray();
        foreach (var info in attributes.Where(attribute => attribute.AttributeType == typeof(MelonInfoAttribute)))
        {
            var args = info.ConstructorArguments;
            if (args.Count is not (5 or 7 or 8) || args[0].Value is not Type melonType)
                throw new InvalidAdmissionMetadataException("MelonInfo metadata has an unsupported shape.");
            if (!typeof(MelonMod).IsAssignableFrom(melonType))
                continue;
            if (!ReferenceEquals(melonType.Assembly, assembly))
                throw new InvalidAdmissionMetadataException("MelonInfo points at a type in another assembly.");
            if (idAttributes.Length > 1)
                throw new InvalidAdmissionMetadataException("A declared MelonMod has conflicting MelonID declarations.");
            string? declaredId = idAttributes.Length == 0 ? null : Convert.ToString(idAttributes[0].ConstructorArguments[0].Value, CultureInfo.InvariantCulture);
            string name = args[1].Value as string ?? "UNKNOWN";
            string version = args[2].ArgumentType == typeof(string)
                ? args[2].Value as string ?? "1.0.0"
                : string.Join(".", args.Skip(2).Take(3).Select(argument => Convert.ToString(argument.Value, CultureInfo.InvariantCulture)))
                  + (args.Count == 8 ? args[5].Value as string : string.Empty);
            if (version.Length == 0)
                version = "1.0.0";
            string id = IsCoreType(melonType) ? "anomaly.client" : string.IsNullOrWhiteSpace(declaredId) ? name : declaredId;
            result.Add(new DeclaredAdmissionMod(melonType, id, version));
        }
        return result.AsReadOnly();
    }

    // Keep the client wire ID stable without introducing a Shared/Client reference
    // into the plugin that must run before those components are loaded.
    internal static bool IsCoreType(Type type) => type.FullName == "Anomaly.Client.Core" && type.Assembly.GetName().Name == "Anomaly.Client";
}
