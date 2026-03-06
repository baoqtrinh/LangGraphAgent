using GrasshopperAgent.Protocol;
using System.IO;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Linq;
using System;

namespace GrasshopperAgent
{
    /// <summary>
    /// A single tool loaded from a .gh file + CSV entry.
    /// </summary>
    public record GHToolDefinition(
        string FilePath,
        string Name,
        string Description,
        List<ParameterSpec> Inputs,
        List<ParameterSpec> Outputs
    );

    public record ParameterSpec(string Name, string Description);

    /// <summary>
    /// Scans a folder for .gh files and reads tools.csv for tool metadata.
    /// Falls back to the filename as the tool name when no CSV entry exists.
    /// </summary>
    public class ToolRegistry
    {
        private readonly string _folder;
        private readonly List<GHToolDefinition> _tools = new();

        public IReadOnlyList<GHToolDefinition> Tools => _tools;

        public ToolRegistry(string folder)
        {
            _folder = folder;
            Load();
        }

        private void Load()
        {
            _tools.Clear();

            if (!Directory.Exists(_folder)) return;

            // Parse CSV if it exists
            var csvPath = Path.Combine(_folder, "tools.csv");
            var csvEntries = new Dictionary<string, GHToolDefinition>(StringComparer.OrdinalIgnoreCase);

            if (File.Exists(csvPath))
            {
                bool first = true;
                foreach (var line in File.ReadLines(csvPath))
                {
                    // Skip header and comment lines
                    if (first) { first = false; continue; }
                    if (line.TrimStart().StartsWith("#") || string.IsNullOrWhiteSpace(line)) continue;

                    var entry = ParseCsvLine(line);
                    if (entry is not null)
                        csvEntries[entry.FilePath] = entry;
                }
            }

            // Find all .gh files
            foreach (var ghFile in Directory.GetFiles(_folder, "*.gh", SearchOption.TopDirectoryOnly))
            {
                var filename = Path.GetFileName(ghFile);

                if (csvEntries.TryGetValue(filename, out var tool))
                    _tools.Add(tool with { FilePath = ghFile });
                else
                    _tools.Add(new GHToolDefinition(
                        FilePath: ghFile,
                        Name: FileNameToToolName(filename),
                        Description: $"Run Grasshopper script: {filename}",
                        Inputs: new List<ParameterSpec> { new("input", "Input value") },
                        Outputs: new List<ParameterSpec> { new("output", "Output value") }
                    ));
            }
        }

        /// <summary>
        /// CSV format:
        ///   filename,name,description,inputs,outputs
        /// inputs/outputs: semicolon-separated  paramName:description  pairs
        /// </summary>
        private static GHToolDefinition? ParseCsvLine(string line)
        {
            var parts = SplitCsv(line);
            if (parts.Length < 3) return null;

            var filename    = parts[0].Trim();
            var name        = parts[1].Trim();
            var description = parts[2].Trim();
            var inputs  = parts.Length > 3 ? ParseParams(parts[3]) : new List<ParameterSpec>();
            var outputs = parts.Length > 4 ? ParseParams(parts[4]) : new List<ParameterSpec>();

            return new GHToolDefinition(filename, name, description, inputs, outputs);
        }

        private static List<ParameterSpec> ParseParams(string raw)
        {
            var result = new List<ParameterSpec>();
            foreach (var chunk in raw.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var idx = chunk.IndexOf(':');
                result.Add(idx > 0
                    ? new ParameterSpec(chunk[..idx].Trim(), chunk[(idx + 1)..].Trim())
                    : new ParameterSpec(chunk.Trim(), ""));
            }
            return result;
        }

        /// <summary>Simple CSV split respecting double-quoted fields.</summary>
        private static string[] SplitCsv(string line)
        {
            var fields = new List<string>();
            var sb = new System.Text.StringBuilder();
            bool inQuotes = false;
            foreach (var ch in line)
            {
                if (ch == '"') inQuotes = !inQuotes;
                else if (ch == ',' && !inQuotes) { fields.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(ch);
            }
            fields.Add(sb.ToString());
            return fields.ToArray();
        }

        private static string FileNameToToolName(string filename) =>
            Regex.Replace(Path.GetFileNameWithoutExtension(filename), @"[^a-zA-Z0-9_]", "_").ToLower();

        /// <summary>Convert the loaded tools to MCP ToolDefinitions for the HTTP response.</summary>
        public List<ToolDefinition> ToMCPDefinitions() =>
            _tools.Select(t =>
            {
                var props = t.Inputs.ToDictionary(
                    i => i.Name,
                    i => new PropertySchema("string", i.Description));
                var required = t.Inputs.Select(i => i.Name).ToArray();
                return new ToolDefinition(
                    t.Name,
                    t.Description,
                    new InputSchema("object", props, required),
                    new[] { "grasshopper", "custom" });
            }).ToList();

        public GHToolDefinition? FindByName(string name) =>
            _tools.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }
}
