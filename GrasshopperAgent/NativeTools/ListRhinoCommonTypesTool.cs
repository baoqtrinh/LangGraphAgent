using GrasshopperAgent.Protocol;
using Rhino;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace GrasshopperAgent.NativeTools
{
    /// <summary>Introspects loaded RhinoCommon/Grasshopper assemblies to discover types.</summary>
    public sealed class ListRhinoCommonTypesTool : INativeTool
    {
        public ToolDefinition Definition { get; } = new ToolDefinition(
            Name: "list_rhinocommon_types",
            Description:
                "Introspects the loaded RhinoCommon (and Grasshopper) assemblies via .NET " +
                "reflection to help you discover available types before writing a script.\n" +
                "\n" +
                "Modes:\n" +
                "  • No arguments → returns all namespace names (use to explore the tree)\n" +
                "  • namespace='Rhino.Geometry' → lists types in that namespace\n" +
                "  • filter='Curve' → searches type names across all namespaces\n" +
                "\n" +
                "Follow up with get_type_members to learn constructor/method signatures.",
            InputSchema: new InputSchema(
                Type: "object",
                Properties: new Dictionary<string, PropertySchema>
                {
                    ["namespace"] = new("string",
                        "Namespace prefix to list types from, e.g. 'Rhino.Geometry'. " +
                        "Prefix-matched: 'Rhino' matches Rhino, Rhino.Geometry, Rhino.DocObjects."),
                    ["filter"] = new("string",
                        "Substring to match in type names, e.g. 'Curve', 'Mesh', 'Surface'."),
                },
                Required: Array.Empty<string>()
            ),
            Categories: new[] { "api", "discovery", "reflection" },
            Outputs: new Dictionary<string, string>
            {
                ["namespaces"] = "Distinct namespace list (returned when no args given)",
                ["types"]      = "Array of { full_name, name, namespace, kind }",
                ["count"]      = "Number of results",
            }
        );

        public string Execute(Dictionary<string, string> args)
        {
            args.TryGetValue("namespace", out var ns);
            args.TryGetValue("filter", out var filter);

            var assemblies = new List<Assembly> { typeof(RhinoDoc).Assembly };
            try { assemblies.Add(typeof(Grasshopper.Instances).Assembly); } catch { }

            var allTypes = assemblies
                .SelectMany(a =>
                {
                    try { return a.GetExportedTypes(); }
                    catch { return Array.Empty<Type>(); }
                })
                .Where(t => !t.IsNested);

            if (string.IsNullOrEmpty(ns) && string.IsNullOrEmpty(filter))
            {
                var nsList = allTypes
                    .Select(t => t.Namespace ?? "")
                    .Where(n => n.Length > 0)
                    .Distinct().OrderBy(n => n).ToList();
                return JsonSerializer.Serialize(new { count = nsList.Count, namespaces = nsList });
            }

            var filtered = allTypes;
            if (!string.IsNullOrEmpty(ns))
                filtered = filtered.Where(t =>
                    t.Namespace != null &&
                    (t.Namespace == ns || t.Namespace.StartsWith(ns + ".")));
            if (!string.IsNullOrEmpty(filter))
                filtered = filtered.Where(t =>
                    t.Name.Contains(filter, StringComparison.OrdinalIgnoreCase));

            var types = filtered
                .Select(t => new
                {
                    full_name  = t.FullName ?? t.Name,
                    name       = t.Name,
                    @namespace = t.Namespace,
                    kind       = t.IsEnum             ? "enum"
                               : t.IsInterface        ? "interface"
                               : t.IsAbstract && !t.IsSealed ? "abstract"
                               : "class",
                })
                .OrderBy(t => t.full_name)
                .Take(150)
                .ToList();

            return JsonSerializer.Serialize(new { count = types.Count, types });
        }
    }
}
