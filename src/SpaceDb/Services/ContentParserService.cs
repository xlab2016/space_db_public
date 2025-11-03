using SpaceDb.Models;
using SpaceDb.Services.Parsers;
using System.Text.Json;
using AI;

namespace SpaceDb.Services
{
    /// <summary>
    /// Service for parsing content and storing as hierarchical graph structure
    /// Resource (dimension=0) -> Fragments (dimension=1)
    /// </summary>
    public class ContentParserService : IContentParserService
    {
        private readonly ISpaceDbService _spaceDbService;
        private readonly IEmbeddingProvider _embeddingProvider;
        private readonly ILogger<ContentParserService> _logger;
        private readonly Dictionary<string, PayloadParserBase> _parsers;
        private readonly string _embeddingType;

        public ContentParserService(
            ISpaceDbService spaceDbService,
            IEmbeddingProvider embeddingProvider,
            ILogger<ContentParserService> logger,
            IEnumerable<PayloadParserBase> parsers,
            string embeddingType = "default")
        {
            _spaceDbService = spaceDbService ?? throw new ArgumentNullException(nameof(spaceDbService));
            _embeddingProvider = embeddingProvider ?? throw new ArgumentNullException(nameof(embeddingProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _embeddingType = embeddingType;

            // Register parsers by content type
            _parsers = parsers.ToDictionary(p => p.ContentType, p => p);

            _logger.LogInformation("ContentParserService initialized with {Count} parsers: {Types}",
                _parsers.Count, string.Join(", ", _parsers.Keys));
        }

        public async Task<ContentParseResult?> ParseAndStoreAsync(
            string payload,
            string resourceId,
            string contentType = "auto",
            long? singularityId = null,
            int? userId = null,
            Dictionary<string, object>? metadata = null)
        {
            try
            {
                _logger.LogInformation("Parsing and storing content for resource {ResourceId}, type: {ContentType}",
                    resourceId, contentType);

                // Determine parser
                var parser = GetParser(payload, contentType);
                if (parser == null)
                {
                    _logger.LogError("No suitable parser found for content type: {ContentType}", contentType);
                    return null;
                }

                // Parse payload into fragments
                var parsedResource = await parser.ParseAsync(payload, resourceId, metadata);

                if (parsedResource.Fragments.Count == 0)
                {
                    _logger.LogWarning("No fragments parsed from resource {ResourceId}", resourceId);
                    return null;
                }

                _logger.LogInformation("Parsed {Count} fragments from resource {ResourceId}",
                    parsedResource.Fragments.Count, resourceId);

                // Create resource point (dimension=0, layer=0)
                var resourceMetadata = new Dictionary<string, object>
                {
                    ["resource_id"] = resourceId,
                    ["resource_type"] = parsedResource.ResourceType,
                    ["fragment_count"] = parsedResource.Fragments.Count,
                    ["parsed_at"] = DateTime.UtcNow
                };

                if (metadata != null)
                {
                    foreach (var kvp in metadata)
                    {
                        resourceMetadata[kvp.Key] = kvp.Value;
                    }
                }

                var resourcePayload = $"Resource: {resourceId} ({parsedResource.ResourceType}) with {parsedResource.Fragments.Count} fragments";

                var resourcePoint = new Point
                {
                    Layer = 0,
                    Dimension = 0,  // Resources at dimension 0
                    Weight = 1.0,
                    SingularityId = singularityId,
                    UserId = userId,
                    Payload = resourcePayload
                };

                var resourcePointId = await _spaceDbService.AddPointAsync(null, resourcePoint);

                if (!resourcePointId.HasValue)
                {
                    _logger.LogError("Failed to create resource point for {ResourceId}", resourceId);
                    return null;
                }

                _logger.LogInformation("Created resource point {PointId} for {ResourceId}",
                    resourcePointId, resourceId);

                // Batch create embeddings for all fragments
                var fragmentTexts = parsedResource.Fragments.Select(f => f.Content).ToList();

                _logger.LogInformation("Creating embeddings for {Count} fragments in batch", fragmentTexts.Count);

                var embeddings = await _embeddingProvider.CreateEmbeddingsAsync(
                    _embeddingType,
                    fragmentTexts,
                    labels: null,
                    returnVectors: true);

                if (embeddings.Count != fragmentTexts.Count)
                {
                    _logger.LogError("Embedding count mismatch: expected {Expected}, got {Actual}",
                        fragmentTexts.Count, embeddings.Count);
                    return null;
                }

                _logger.LogInformation("Successfully created {Count} embeddings", embeddings.Count);

                // Create fragment points (dimension=1, layer=0) with segments to resource
                var result = new ContentParseResult
                {
                    ResourcePointId = resourcePointId.Value,
                    ParserType = parser.ContentType
                };

                for (int i = 0; i < parsedResource.Fragments.Count; i++)
                {
                    var fragment = parsedResource.Fragments[i];
                    var embedding = embeddings[i];

                    var fragmentMetadataDict = new Dictionary<string, object>
                    {
                        ["resource_id"] = resourceId,
                        ["fragment_type"] = fragment.Type,
                        ["fragment_order"] = fragment.Order,
                        ["parent_key"] = fragment.ParentKey ?? resourceId
                    };

                    if (fragment.Metadata != null)
                    {
                        foreach (var kvp in fragment.Metadata)
                        {
                            fragmentMetadataDict[$"fragment_{kvp.Key}"] = kvp.Value;
                        }
                    }

                    var fragmentPoint = new Point
                    {
                        Layer = 0,
                        Dimension = 1,  // Fragments at dimension 1
                        Weight = 1.0 / (fragment.Order + 1),  // Weight decreases with order
                        SingularityId = singularityId,
                        UserId = userId,
                        Payload = fragment.Content
                    };

                    // Add point with embedding and create segment to resource
                    var fragmentPointId = await _spaceDbService.AddPointAsync(
                        resourcePointId.Value,
                        fragmentPoint,
                        embedding);

                    if (fragmentPointId.HasValue)
                    {
                        result.FragmentPointIds.Add(fragmentPointId.Value);

                        // Segment is created automatically by AddPointAsync with fromId parameter
                        _logger.LogDebug("Created fragment point {PointId} for fragment {Order}",
                            fragmentPointId, fragment.Order);
                    }
                    else
                    {
                        _logger.LogWarning("Failed to create fragment point for fragment {Order}", fragment.Order);
                    }
                }

                _logger.LogInformation(
                    "Successfully stored resource {ResourceId} with {FragmentCount} fragments. " +
                    "Resource point: {ResourcePointId}, Fragment points: {FragmentCount}",
                    resourceId, result.TotalFragments, resourcePointId, result.FragmentPointIds.Count);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing and storing content for resource {ResourceId}", resourceId);
                return null;
            }
        }

        public IEnumerable<string> GetAvailableParserTypes()
        {
            return _parsers.Keys;
        }

        private PayloadParserBase? GetParser(string payload, string contentType)
        {
            // Auto-detect content type
            if (contentType == "auto")
            {
                foreach (var parser in _parsers.Values)
                {
                    if (parser.CanParse(payload))
                    {
                        _logger.LogInformation("Auto-detected content type: {Type}", parser.ContentType);
                        return parser;
                    }
                }
                return null;
            }

            // Use specified parser
            if (_parsers.TryGetValue(contentType, out var selectedParser))
            {
                if (selectedParser.CanParse(payload))
                {
                    return selectedParser;
                }
                else
                {
                    _logger.LogWarning("Parser {Type} cannot parse this payload", contentType);
                }
            }

            return null;
        }
    }
}
