using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GrasshopperAgent.Protocol
{
    // ── Request wrappers ──────────────────────────────────────────────────────

    public record ListToolsRequest();

    /// <summary>
    /// Arguments are deserialized as JsonElement so numeric/bool values sent
    /// from Python (e.g. {"width": 1024}) are not silently dropped by the
    /// string-only deserializer.  Use <see cref="ArgumentHelpers.Normalize"/>
    /// to convert to Dictionary&lt;string, string&gt; before passing to tools.
    /// </summary>
    public record CallToolRequest(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("arguments")] Dictionary<string, JsonElement>? Arguments
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

    // ── Argument normalization ─────────────────────────────────────────────────

    public static class ArgumentHelpers
    {
        /// <summary>
        /// Converts a <see cref="Dictionary{String, JsonElement}"/> received from the
        /// HTTP request body into the <c>Dictionary&lt;string, string&gt;</c> that all
        /// <see cref="NativeTools.INativeTool"/> Execute implementations expect.
        /// Numbers, booleans and null are converted to their canonical string form.
        /// </summary>
        public static Dictionary<string, string> Normalize(
            Dictionary<string, JsonElement>? args)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (args is null) return result;
            foreach (var (key, element) in args)
            {
                result[key] = element.ValueKind switch
                {
                    JsonValueKind.String  => element.GetString() ?? "",
                    JsonValueKind.Number  => element.GetRawText(),
                    JsonValueKind.True    => "true",
                    JsonValueKind.False   => "false",
                    JsonValueKind.Null    => "",
                    _                    => element.GetRawText(),
                };
            }
            return result;
        }
    }
}
