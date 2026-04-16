using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using LabApi.Features.Console;
using LabApi.Loader;

namespace Anomaly.Server.API.Plugins
{
    /// <summary>
    /// Server-side registry of Anomaly-aware LabAPI plugins. Discovery is driven by
    /// the <see cref="AnomalyPluginAttribute"/> on a LabAPI plugin class; lifecycle
    /// stays with LabAPI.
    /// </summary>
    /// <remarks>
    /// This class does NOT manage enable/disable — LabAPI owns that. It just tracks
    /// which loaded plugins want their registrations namespace-scoped under their
    /// Anomaly plugin id. Other server-side subsystems (e.g. custom type registries
    /// in Phase B) consult <see cref="IsKeyOwnedByRegisteredPlugin"/> to enforce the
    /// convention.
    /// </remarks>
    public static class AnomalyServerPlugins
    {
        // Must match the client-side validation rule in AnomalyClientPlugins.
        private static readonly Regex IdPattern = new Regex(
            @"^[a-z][a-z0-9_]{0,31}(\.[a-z][a-z0-9_]{0,31}){0,39}$",
            RegexOptions.Compiled);

        private static readonly Dictionary<string, Entry> _plugins =
            new Dictionary<string, Entry>(StringComparer.Ordinal);

        private static bool _discovered;

        /// <summary>
        /// Opaque record of a discovered Anomaly-aware plugin.
        /// </summary>
        public class Entry
        {
            public string Id { get; internal set; }
            public string DisplayName { get; internal set; }
            public Version Version { get; internal set; }
            public object PluginInstance { get; internal set; }
        }

        public static IReadOnlyCollection<Entry> All =>
            new ReadOnlyCollection<Entry>(_plugins.Values.ToList());

        public static Entry GetById(string id) =>
            id != null && _plugins.TryGetValue(id, out var entry) ? entry : null;

        public static bool IsRegistered(string id) =>
            id != null && _plugins.ContainsKey(id);

        /// <summary>
        /// True if <paramref name="key"/> either equals a registered plugin id or is
        /// prefixed by one followed by a dot.
        /// </summary>
        public static bool IsKeyOwnedByRegisteredPlugin(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            foreach (var id in _plugins.Keys)
            {
                if (key == id) return true;
                if (key.Length > id.Length && key[id.Length] == '.' && key.StartsWith(id, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Scans loaded LabAPI plugin assemblies for types marked with
        /// <see cref="AnomalyPluginAttribute"/> and records their identities.
        /// Idempotent. Called once from Anomaly.Server's <c>Plugin.Enable</c>.
        /// </summary>
        internal static void Discover()
        {
            if (_discovered) return;
            _discovered = true;

            // Pull plugin instances from LabAPI's loader when available, so we can
            // resolve the attribute and metadata off the same class LabAPI instantiated.
            IEnumerable<object> loadedPlugins = TryGetLoadedLabApiPlugins();

            foreach (var plugin in loadedPlugins)
            {
                if (plugin == null) continue;

                var type = plugin.GetType();
                var attr = type.GetCustomAttribute<AnomalyPluginAttribute>();
                if (attr == null) continue;

                string id = attr.Id;
                try
                {
                    ValidateId(id);
                }
                catch (Exception ex)
                {
                    Logger.Warn($"AnomalyServerPlugins: '{type.FullName}' has invalid id '{id}': {ex.Message}");
                    continue;
                }

                if (_plugins.ContainsKey(id))
                {
                    Logger.Warn($"AnomalyServerPlugins: duplicate registration for '{id}' ({type.FullName}); ignoring.");
                    continue;
                }

                string displayName = TryGetStringMember(plugin, "Name") ?? type.Name;
                Version version = TryGetVersionMember(plugin, "Version") ?? type.Assembly.GetName().Version;

                _plugins[id] = new Entry
                {
                    Id = id,
                    DisplayName = displayName,
                    Version = version,
                    PluginInstance = plugin
                };

                Logger.Info($"AnomalyServerPlugins: Registered '{id}' ({displayName} v{version}).");
            }
        }

        private static IEnumerable<object> TryGetLoadedLabApiPlugins()
        {
            // LabAPI exposes loaded plugins through PluginLoader; the API shape varies
            // slightly by version, so use reflection defensively.
            var loaderType = typeof(PluginLoader);
            var candidates = new[] { "Plugins", "EnabledPlugins", "LoadedPlugins", "AllPlugins" };
            foreach (var name in candidates)
            {
                var prop = loaderType.GetProperty(name, BindingFlags.Public | BindingFlags.Static);
                if (prop == null) continue;
                var value = prop.GetValue(null);
                if (value is System.Collections.IEnumerable enumerable)
                    return enumerable.Cast<object>().ToList();
            }

            // Fallback: scan all loaded assemblies for types with our attribute and
            // try to find a static Instance field/property. This is best-effort.
            var result = new List<object>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray(); }
                catch { continue; }

                foreach (var type in types)
                {
                    if (type == null) continue;
                    if (type.GetCustomAttribute<AnomalyPluginAttribute>() == null) continue;

                    var instance = TryGetInstanceMember(type);
                    if (instance != null) result.Add(instance);
                }
            }
            return result;
        }

        private static object TryGetInstanceMember(Type type)
        {
            var prop = type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            if (prop != null) return prop.GetValue(null);
            var field = type.GetField("Instance", BindingFlags.Public | BindingFlags.Static);
            if (field != null) return field.GetValue(null);
            return null;
        }

        private static string TryGetStringMember(object obj, string name)
        {
            var type = obj.GetType();
            var prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (prop != null && prop.PropertyType == typeof(string))
                return prop.GetValue(obj) as string;
            return null;
        }

        private static Version TryGetVersionMember(object obj, string name)
        {
            var type = obj.GetType();
            var prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (prop != null && prop.PropertyType == typeof(Version))
                return prop.GetValue(obj) as Version;
            return null;
        }

        private static void ValidateId(string id)
        {
            if (string.IsNullOrEmpty(id))
                throw new ArgumentException("Plugin id must be non-empty.", nameof(id));
            if (!IdPattern.IsMatch(id))
                throw new ArgumentException(
                    $"Plugin id '{id}' is invalid. Must be lowercase dotted (e.g. \"acme.wargames\"), " +
                    "each segment alphanumeric or underscore, starting with a letter.",
                    nameof(id));
        }
    }
}
