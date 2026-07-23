namespace PepperX.Server.Api.Mcp
{
    using System.Collections.Generic;

    /// <summary>
    /// Flexible, typed argument bag bound from an MCP tool call's input. Each tool reads the fields relevant
    /// to it; unrelated fields remain null. This keeps tool handlers off raw JSON DOM navigation.
    /// </summary>
    public class McpToolArgs
    {
        #region Public-Members

        /// <summary>Container name.</summary>
        public string? Container { get; set; } = null;

        /// <summary>Object key.</summary>
        public string? Key { get; set; } = null;

        /// <summary>New container name (for create).</summary>
        public string? Name { get; set; } = null;

        /// <summary>Content type (for write).</summary>
        public string? ContentType { get; set; } = null;

        /// <summary>Base64 payload (for write and read).</summary>
        public string? DataBase64 { get; set; } = null;

        /// <summary>Labels (for write).</summary>
        public List<string>? Labels { get; set; } = null;

        /// <summary>Tags (for write and container tags).</summary>
        public Dictionary<string, string>? Tags { get; set; } = null;

        /// <summary>Freeform metadata object (for write).</summary>
        public object? Object { get; set; } = null;

        /// <summary>Whether to fail if the key exists (for write).</summary>
        public bool NoOverwrite { get; set; } = false;

        /// <summary>Whether to force-delete a non-empty container.</summary>
        public bool Force { get; set; } = false;

        /// <summary>Maximum results (for enumeration).</summary>
        public int? MaxResults { get; set; } = null;

        /// <summary>Key or name prefix filter (for enumeration).</summary>
        public string? Prefix { get; set; } = null;

        /// <summary>Label filter (for enumeration).</summary>
        public List<string>? LabelsFilter { get; set; } = null;

        /// <summary>Tag filter (for enumeration).</summary>
        public Dictionary<string, string>? TagsFilter { get; set; } = null;

        /// <summary>Container names to restrict a cross-container search.</summary>
        public List<string>? Containers { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate empty tool arguments.
        /// </summary>
        public McpToolArgs()
        {
        }

        #endregion
    }
}
