using AI;
using SpaceDb.Models;

namespace SpaceDb.Services;

public interface ISpaceDbService
{
    Task<long?> AddPointAsync(long? fromId, Point point);
    Task<long?> AddPointAsync(long? fromId, Point point, AIEmbedding embedding);
    Task<bool> UpdatePointAsync(Point point);
    Task<bool> UpdatePointAsync(Point point, AIEmbedding embedding);
    Task<bool> DeletePointAsync(long pointId);
    
    Task<long?> AddSegmentAsync(long? fromId, long? toId);
    Task<bool> DeleteSegmentAsync(long? fromId, long? toId);
    
    Task<IEnumerable<object>> SearchAsync(
        string query,
        long? singularityId = null,
        int? dimension = null,
        int? layer = null,
        uint limit = 10,
        float scoreThreshold = 0.0f);
    
    Task<IEnumerable<object>> SearchWithEmbeddingAsync(
        AIEmbedding queryEmbedding,
        long? singularityId = null,
        int? dimension = null,
        int? layer = null,
        uint limit = 10,
        float scoreThreshold = 0.0f);
}

