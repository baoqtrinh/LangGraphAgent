using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace GrasshopperAgent.Protocol
{
    // ── Request wrappers ──────────────────────────────────────────────────────

    public record ListToolsRequest();

    public record CallToolRequest(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("arguments")] Dictionary<string, string>? Arguments
    );

    // ── Tool definition sent to Python ────────────────────────────────────────

    public record ToolDefinition(
        [property: JsonPropertyName("name")]        string Name,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("inputSchema")]  InputSchema InputSchema,
        [property: JsonPropertyName("categories")]  string[] Categories,
        [property: JsonPropertyName("outputs")]     Dictionary<string, string> Outputs
    );

    public record InputSchema(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("properties")] Dictionary<string, PropertySchema> Properties,
        [property: JsonPropertyName("required")] string[] Required
    );

    public record PropertySchema(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("description")] string Description
    );

    // ── Responses ─────────────────────────────────────────────────────────────

    public record HealthResponse(
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("tools")] int Tools
    );

    public record ListToolsResponse(
        [property: JsonPropertyName("tools")] List<ToolDefinition> Tools
    );

    public record CallToolResponse(
        [property: JsonPropertyName("result")] string? Result,
        [property: JsonPropertyName("error")] string? Error
    );
}
