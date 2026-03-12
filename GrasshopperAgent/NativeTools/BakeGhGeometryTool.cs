using GrasshopperAgent.Protocol;
using Rhino;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace GrasshopperAgent.NativeTools
{
    /// <summary>Bakes all current Grasshopper preview geometry into the active Rhino document.</summary>
    public sealed class BakeGhGeometryTool : INativeTool
    {
        public ToolDefinition Definition { get; } = new ToolDefinition(
            Name: "bake_gh_geometry",
            Description:
                "Bakes all current Grasshopper preview geometry into the active Rhino " +
                "document as real objects. Use this after a GH script tool has generated " +
                "geometry so it becomes a permanent Rhino object with a GUID. " +
                "Optionally specify a target layer name.",
            InputSchema: new InputSchema(
                Type: "object",
                Properties: new Dictionary<string, PropertySchema>
                {
                    ["layer"] = new("string",
                        "Optional layer name to bake objects onto. " +
                        "Layer is created if it does not exist. Defaults to the current layer."),
                },
                Required: Array.Empty<string>()
            ),
            Categories: new[] { "grasshopper", "bake", "geometry" },
            Outputs: new Dictionary<string, string>
            {
                ["baked_count"] = "Number of objects successfully baked",
                ["baked_ids"]   = "GUIDs of all newly baked Rhino objects",
                ["layer"]       = "Layer the objects were baked onto",
                ["skipped"]     = "Number of GH components skipped (not bake-aware or no geometry)",
            }
        );

        public string Execute(Dictionary<string, string> args)
        {
            var rhinoDoc = RhinoDoc.ActiveDoc
                ?? throw new InvalidOperationException("No active Rhino document.");

            var ghCanvas = Grasshopper.Instances.ActiveCanvas
                ?? throw new InvalidOperationException("No active Grasshopper canvas.");
            var ghDoc = ghCanvas.Document
                ?? throw new InvalidOperationException("No active Grasshopper document.");

            // Resolve or create target layer
            args.TryGetValue("layer", out var layerName);
            var attrs = new Rhino.DocObjects.ObjectAttributes();
            if (!string.IsNullOrWhiteSpace(layerName))
            {
                int layerIdx = rhinoDoc.Layers.FindByFullPath(layerName, -1);
                if (layerIdx < 0)
                {
                    var newLayer = new Rhino.DocObjects.Layer { Name = layerName };
                    layerIdx = rhinoDoc.Layers.Add(newLayer);
                }
                if (layerIdx >= 0) attrs.LayerIndex = layerIdx;
            }

            var bakedIds = new List<string>();
            int skipped  = 0;

            foreach (var obj in ghDoc.Objects)
            {
                if (obj is Grasshopper.Kernel.IGH_BakeAwareObject bakeObj
                    && obj is Grasshopper.Kernel.IGH_ActiveObject activeObj
                    && activeObj.RuntimeMessageLevel < Grasshopper.Kernel.GH_RuntimeMessageLevel.Error)
                {
                    var ids = new List<Guid>();
                    try
                    {
                        bakeObj.BakeGeometry(rhinoDoc, attrs, ids);
                        bakedIds.AddRange(ids.Select(id => id.ToString()));
                    }
                    catch
                    {
                        skipped++;
                    }
                }
                else
                {
                    skipped++;
                }
            }

            rhinoDoc.Views.Redraw();

            return JsonSerializer.Serialize(new
            {
                baked_count = bakedIds.Count,
                baked_ids   = bakedIds,
                layer       = string.IsNullOrWhiteSpace(layerName) ? "(current layer)" : layerName,
                skipped,
            });
        }
    }
}
