namespace SpaceDb.Models
{
    /// <summary>
    /// Represents a parsed resource with its fragments
    /// </summary>
    public class ParsedResource
    {
        /// <summary>
        /// Resource identifier (filename, url, etc.)
        /// </summary>
        public string ResourceId { get; set; } = string.Empty;

        /// <summary>
        /// Resource type (text, json, etc.)
        /// </summary>
        public string ResourceType { get; set; } = string.Empty;

        /// <summary>
        /// Original resource metadata
        /// </summary>
        public Dictionary<string, object>? Metadata { get; set; }

        /// <summary>
        /// Parsed content fragments
        /// </summary>
        public List<ContentFragment> Fragments { get; set; } = new();
    }
}
