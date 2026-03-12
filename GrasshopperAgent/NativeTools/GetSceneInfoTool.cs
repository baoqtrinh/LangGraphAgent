using GrasshopperAgent.Protocol;
using Rhino;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace GrasshopperAgent.NativeTools
{
    /// <summary>Returns metadata about the current Rhino scene.</summary>
    public sealed class GetSceneInfoTool : INativeTool
    {
        public ToolDefinition Definition { get; } = new ToolDefinition(
            Name: "get_scene_info",
            Description:
                "Returns metadata about the current Rhino scene: document name and path, " +
                "a full list of all objects (id, type, layer, name), object counts by type, " +
                "layer names, active viewport name, and camera position. " +
                "Use this first to orient yourself before creating or editing geometry — " +
                "the returned object GUIDs can be passed directly to run_csharp_script.",
            InputSchema: new InputSchema(
                Type: "object",
                Properties: new Dictionary<string, PropertySchema>(),
                Required: Array.Empty<string>()
            ),
            Categories: new[] { "scene", "info" },
            Outputs: new Dictionary<string, string>
            {
                ["doc_name"]      = "Rhino document name (filename without path)",
                ["doc_path"]      = "Full path to the .3dm file (empty if unsaved)",
                ["object_count"]  = "Number of live (non-deleted) objects in the document",
                ["object_types"]  = "Object counts grouped by type (Brep, Curve, …)",
                ["objects"]       = "Array of { id, type, layer, name } for every live object",
                ["layers"]        = "All non-deleted layer full-path names",
                ["active_view"]   = "Name of the active viewport",
                ["projection"]    = "parallel or perspective",
                ["camera_target"] = "Camera target [x, y, z]",
                ["units"]         = "Model unit system (Millimeters, Meters, …)",
            }
        );

        public string Execute(Dictionary<string, string> args)
        {
            var doc = RhinoDoc.ActiveDoc
                ?? throw new InvalidOperationException("No active Rhino document.");

            // Non-deleted objects only
            var liveObjects = doc.Objects.Where(o => !o.IsDeleted).ToList();

            // Object counts by type
            var counts = new Dictionary<string, int>();
            foreach (var obj in liveObjects)
            {
                var key = obj.ObjectType.ToString();
                counts[key] = counts.TryGetValue(key, out var n) ? n + 1 : 1;
            }

            // Per-object list: id, type, layer, name
            var objectList = liveObjects.Select(obj =>
            {
                var layer = doc.Layers.FindIndex(obj.Attributes.LayerIndex)?.FullPath ?? "";
                return new
                {
                    id    = obj.Id.ToString(),
                    type  = obj.ObjectType.ToString(),
                    layer,
                    name  = obj.Attributes.Name ?? "",
                };
            }).ToList();

            // Layer names (non-deleted)
            var layers = doc.Layers
                .Where(l => !l.IsDeleted)
                .Select(l => l.FullPath)
                .ToList();

            // Viewport / camera
            var view = doc.Views.ActiveView;
            var vp   = view?.ActiveViewport;
            object? camTarget = vp is null ? null : new[]
            {
                Math.Round(vp.CameraTarget.X, 3),
                Math.Round(vp.CameraTarget.Y, 3),
                Math.Round(vp.CameraTarget.Z, 3),
            };

            return JsonSerializer.Serialize(new
            {
                doc_name      = string.IsNullOrEmpty(doc.Name) ? "(unsaved)" : doc.Name,
                doc_path      = doc.Path ?? "",
                object_count  = liveObjects.Count,
                object_types  = counts,
                objects       = objectList,
                layers,
                active_view   = vp?.Name ?? "none",
                projection    = vp?.IsParallelProjection == true ? "parallel" : "perspective",
                camera_target = camTarget,
                units         = doc.ModelUnitSystem.ToString(),
            });
        }
    }
}
