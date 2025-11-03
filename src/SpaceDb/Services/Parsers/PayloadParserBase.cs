using SpaceDb.Models;

namespace SpaceDb.Services.Parsers
{
    /// <summary>
    /// Base class for payload parsers that split content into fragments
    /// </summary>
    public abstract class PayloadParserBase
    {
        protected readonly ILogger _logger;

        protected PayloadParserBase(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Parse payload into structured fragments
        /// </summary>
        /// <param name="payload">Raw payload content</param>
        /// <param name="resourceId">Resource identifier</param>
        /// <param name="metadata">Additional metadata</param>
        /// <returns>Parsed resource with fragments</returns>
        public abstract Task<ParsedResource> ParseAsync(
            string payload,
            string resourceId,
            Dictionary<string, object>? metadata = null);

        /// <summary>
        /// Get the content type this parser handles
        /// </summary>
        public abstract string ContentType { get; }

        /// <summary>
        /// Validate if this parser can handle the given payload
        /// </summary>
        public virtual bool CanParse(string payload)
        {
            return !string.IsNullOrWhiteSpace(payload);
        }

        /// <summary>
        /// Clean and normalize text
        /// </summary>
        protected string NormalizeText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            // Remove excessive whitespace
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");

            // Trim
            text = text.Trim();

            return text;
        }

        /// <summary>
        /// Create metadata dictionary with common fields
        /// </summary>
        protected Dictionary<string, object> CreateMetadata(
            Dictionary<string, object>? baseMetadata = null)
        {
            var metadata = baseMetadata != null
                ? new Dictionary<string, object>(baseMetadata)
                : new Dictionary<string, object>();

            metadata["parsed_at"] = DateTime.UtcNow;
            metadata["parser_type"] = ContentType;

            return metadata;
        }
    }
}
