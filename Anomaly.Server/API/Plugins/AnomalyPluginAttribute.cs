using System;

namespace Anomaly.Server.API.Plugins
{
    /// <summary>
    /// Applied to a LabAPI plugin class to declare it as an Anomaly-aware plugin.
    /// The attribute carries the plugin id used to scope cross-subsystem registrations.
    /// </summary>
    /// <remarks>
    /// LabAPI already drives plugin lifecycle (Enable/Disable) and config; this
    /// attribute only provides the identity handle Anomaly's server-side and client-side
    /// subsystems use to namespace things. On the server, where most infrastructure
    /// is provided by LabAPI and MEC, this attribute's job is purely identification.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class AnomalyPluginAttribute : Attribute
    {
        /// <summary>
        /// Lowercase dotted plugin id (e.g. "acme.wargames"). Used as the prefix for
        /// every Anomaly registration this plugin makes (network messages, custom types,
        /// etc.). Must match the id the plugin's client-side counterpart uses.
        /// </summary>
        public string Id { get; }

        public AnomalyPluginAttribute(string id)
        {
            Id = id;
        }
    }
}
