# Content Parsing System

## Overview

The content parsing system automatically splits large content (documents, JSON files) into smaller fragments, creating a hierarchical graph structure in SpaceDb:

```
Resource (dimension=0, layer=0)
  ├─> Fragment 1 (dimension=1, layer=0) [embedding vector]
  ├─> Fragment 2 (dimension=1, layer=0) [embedding vector]
  └─> Fragment N (dimension=1, layer=0) [embedding vector]
```

## Architecture

### Components

1. **PayloadParserBase** - Abstract base class for all parsers
2. **TextPayloadParser** - Splits text into paragraphs
3. **JsonPayloadParser** - Converts JSON into graph nodes
4. **ContentParserService** - Orchestrates parsing and storage
5. **ContentController** - REST API endpoints

### How It Works

1. **Parse**: Content is split into fragments by the appropriate parser
2. **Embed**: All fragments are batch-embedded using the AI provider
3. **Store**:
   - Create resource point (dimension=0) without embedding
   - Create fragment points (dimension=1) with embeddings
   - Create segments connecting resource to each fragment

## API Usage

### Upload Text Content

```bash
POST /api/v1/content/upload
Content-Type: application/json

{
  "payload": "First paragraph...\n\nSecond paragraph...\n\nThird paragraph...",
  "resourceId": "document_001",
  "contentType": "text",
  "singularityId": 1,
  "userId": 123,
  "metadata": {
    "source": "manual_upload",
    "author": "John Doe"
  }
}
```

**Response:**
```json
{
  "data": {
    "resourcePointId": 1001,
    "fragmentPointIds": [1002, 1003, 1004],
    "segmentIds": [],
    "parserType": "text",
    "totalFragments": 3
  },
  "message": "Successfully created resource 1001 with 3 fragments"
}
```

### Upload JSON Content

```bash
POST /api/v1/content/upload
Content-Type: application/json

{
  "payload": "{\"user\":{\"name\":\"Alice\",\"age\":30},\"settings\":{\"theme\":\"dark\"}}",
  "resourceId": "config_001.json",
  "contentType": "json",
  "singularityId": 1
}
```

The JSON parser will create fragments for:
- Root object summary
- Each nested object
- String values longer than 20 characters

### Auto-detect Content Type

```bash
POST /api/v1/content/upload
Content-Type: application/json

{
  "payload": "...",
  "resourceId": "unknown_file",
  "contentType": "auto"  // Will detect text or JSON automatically
}
```

### Batch Upload

```bash
POST /api/v1/content/upload/batch
Content-Type: application/json

{
  "singularityId": 1,
  "userId": 123,
  "items": [
    {
      "payload": "First document...",
      "resourceId": "doc_001",
      "contentType": "text"
    },
    {
      "payload": "{\"key\":\"value\"}",
      "resourceId": "config_001.json",
      "contentType": "json"
    }
  ]
}
```

### List Available Parsers

```bash
GET /api/v1/content/parsers
```

**Response:**
```json
{
  "data": ["text", "json"],
  "message": "Available parsers: text, json"
}
```

## Searching Parsed Content

After uploading, you can search fragments using semantic search:

```bash
POST /api/v1/points/search
Content-Type: application/json

{
  "query": "user settings",
  "dimension": 1,  // Search only fragments
  "singularityId": 1,
  "limit": 10
}
```

To search only resources (not fragments):

```bash
{
  "query": "configuration files",
  "dimension": 0,  // Search only resources
  "singularityId": 1
}
```

## Text Parser Configuration

**Parameters:**
- `minParagraphLength`: Minimum paragraph length (default: 50 chars)
- `maxParagraphLength`: Maximum paragraph length (default: 2000 chars)

**Behavior:**
- Splits by double newlines (`\n\n`)
- Skips paragraphs shorter than `minParagraphLength`
- Splits long paragraphs by sentences
- Normalizes whitespace

**Example fragments created:**
```
Input:
"First paragraph with some content.

Second paragraph with more information.

Third short one."

Output:
- Fragment 0: "First paragraph with some content."
- Fragment 1: "Second paragraph with more information."
- Fragment 2: "Third short one." (if >= 50 chars)
```

## JSON Parser Configuration

**Parameters:**
- `maxDepth`: Maximum nesting depth (default: 10)
- `includeArrays`: Parse arrays (default: true)

**Behavior:**
- Traverses JSON tree recursively
- Creates fragments for:
  - Objects (summary of properties)
  - Arrays (summary of items)
  - String values > 20 characters
- Maintains parent-child relationships via `ParentKey`

**Example fragments created:**
```json
Input:
{
  "user": {
    "name": "Alice",
    "bio": "Software engineer with passion for AI"
  }
}

Output fragments:
- Fragment 0: "Object with 1 properties: user: {...}"
- Fragment 1: "Object with 2 properties: name: Alice, bio: Software engineer..."
- Fragment 2: "Software engineer with passion for AI" (bio value)
```

## Graph Navigation

After parsing, you can navigate the graph:

1. **Get resource point** (dimension=0) → See metadata about the document
2. **Get fragment points** (dimension=1) → Read actual content chunks
3. **Follow segments** → Traverse from resource to fragments

Example use case:
```
1. User searches for "AI systems"
2. System finds Fragment 5 (dimension=1) with high similarity
3. Follow segment back to Resource (dimension=0) to get document metadata
4. Show user: "Found in document_042.txt, paragraph 5"
```

## Performance Considerations

### Batch Embedding
All fragments are embedded in a single batch call to the AI API, providing significant speedup:

```
Sequential: 1000 fragments × 300ms = 5 minutes
Batch:      10 batches × 300ms = 3 seconds (100x faster!)
```

### Dimensions

- **dimension=0**: Resources (documents, files) - no embeddings, just metadata
- **dimension=1**: Fragments (paragraphs, JSON nodes) - with embeddings for search

This separation allows:
- Fast metadata queries (dimension=0)
- Semantic search over content (dimension=1)
- Minimal storage (only fragments have vectors)

## Use Cases

### 1. Document Management System
```
Upload documents → Parse into paragraphs → Semantic search → Find relevant sections
```

### 2. Configuration Files
```
Upload JSON configs → Parse into nodes → Search for settings → Track which config file
```

### 3. Knowledge Base
```
Upload articles → Parse sections → Search by topic → Maintain article hierarchy
```

### 4. Code Documentation
```
Upload API docs → Parse methods → Search by functionality → Link to source files
```

## Extending with Custom Parsers

Create a new parser by extending `PayloadParserBase`:

```csharp
public class MarkdownPayloadParser : PayloadParserBase
{
    public override string ContentType => "markdown";

    public override async Task<ParsedResource> ParseAsync(
        string payload,
        string resourceId,
        Dictionary<string, object>? metadata = null)
    {
        // 1. Parse markdown into sections (# headers)
        // 2. Create fragments for each section
        // 3. Return ParsedResource
    }
}
```

Register in `StartupHelper.cs`:

```csharp
services.AddSingleton<PayloadParserBase, MarkdownPayloadParser>(provider =>
{
    var logger = provider.GetRequiredService<ILogger<MarkdownPayloadParser>>();
    return new MarkdownPayloadParser(logger);
});
```

## Troubleshooting

### "No suitable parser found"
- Ensure `contentType` matches available parsers ("text", "json", "auto")
- Check that payload is not empty
- For JSON, verify valid JSON syntax

### "Failed to create resource point"
- Check database connections (PostgreSQL, RocksDB, Qdrant)
- Verify authentication token is valid
- Check logs for detailed error messages

### "Embedding count mismatch"
- AI API may have rate limits or timeouts
- Check `Providers:QAi:ApiToken` in configuration
- Reduce batch size if hitting API limits

### Fragments not searchable
- Ensure fragments were created (check `fragmentPointIds` in response)
- Search with `dimension=1` to find fragments
- Check that embeddings were created (logs should show "Creating embeddings")
