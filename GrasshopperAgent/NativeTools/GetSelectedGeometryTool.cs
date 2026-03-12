using GrasshopperAgent.Protocol;
using Rhino;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace GrasshopperAgent.NativeTools
{
    /// <summary>Returns details about every currently selected object in the Rhino scene.</summary>
    public sealed class GetSelectedGeometryTool : INativeTool
    {
        public ToolDefinition Definition { get; } = new ToolDefinition(
            Name: "get_selected_geometry",
            Description:
                "Returns details about every currently selected object in the Rhino scene. " +
                "For each object: its GUID, object type, layer, name (if any), " +
                "and bounding-box extents so the LLM knows exact position and size. " +
                "Call this before editing or referencing specific geometry.",
            InputSchema: new InputSchema(
                Type: "object",
                Properties: new Dictionary<string, PropertySchema>
                {
                    ["include_bbox"] = new("boolean", "Include bounding box min/max corners (default true)"),
                },
                Required: Array.Empty<string>()
            ),
            Categories: new[] { "selection", "geometry", "info" },
            Outputs: new Dictionary<string, string>
            {
                ["count"]         = "Number of selected objects",
                ["selected_json"] = "JSON array of selected objects with id, type, layer, name, bbox",
            }
        );

        public string Execute(Dictionary<string, string> args)
        {
            var doc = RhinoDoc.ActiveDoc
                ?? throw new InvalidOperationException("No active Rhino document.");

            bool includeBbox = !args.TryGetValue("include_bbox", out var ib)
                || !string.Equals(ib, "false", StringComparison.OrdinalIgnoreCase);

            var selected = doc.Objects
                .Where(o => !o.IsDeleted && o.IsSelected(false) > 0)
                .ToList();

            var items = selected.Select(obj =>
            {
                var layer = doc.Layers.FindIndex(obj.Attributes.LayerIndex)?.FullPath ?? "";
                var name  = obj.Attributes.Name ?? "";
                var type  = obj.ObjectType.ToString();
                var id    = obj.Id.ToString();

                if (!includeBbox)
                    return (object)new { id, type, layer, name };

                var bbox    = obj.Geometry?.GetBoundingBox(accurate: false);
                object? bboxObj = bbox is null || !bbox.Value.IsValid ? null : new
                {
                    min = new[] {
                        Math.Round(bbox.Value.Min.X, 4),
                        Math.Round(bbox.Value.Min.Y, 4),
                        Math.Round(bbox.Value.Min.Z, 4),
                    },
                    max = new[] {
                        Math.Round(bbox.Value.Max.X, 4),
                        Math.Round(bbox.Value.Max.Y, 4),
                        Math.Round(bbox.Value.Max.Z, 4),
                    },
                    size = new[] {
                        Math.Round(bbox.Value.Max.X - bbox.Value.Min.X, 4),
                        Math.Round(bbox.Value.Max.Y - bbox.Value.Min.Y, 4),
                        Math.Round(bbox.Value.Max.Z - bbox.Value.Min.Z, 4),
                    },
                };

                return (object)new { id, type, layer, name, bbox = bboxObj };
            }).ToList();

            return JsonSerializer.Serialize(new
            {
                count    = items.Count,
                selected = items,
            });
        }
    }
}
