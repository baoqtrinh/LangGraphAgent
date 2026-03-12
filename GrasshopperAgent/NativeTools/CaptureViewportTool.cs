#pragma warning disable CA1416 // Validate platform compatibility
using GrasshopperAgent.Protocol;
using Rhino;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text.Json;

namespace GrasshopperAgent.NativeTools
{
    /// <summary>Captures the active Rhino viewport as a base64-encoded PNG.</summary>
    public sealed class CaptureViewportTool : INativeTool
    {
        public ToolDefinition Definition { get; } = new ToolDefinition(
            Name: "capture_viewport",
            Description:
                "Captures the active Rhino 3D viewport as a PNG and returns it as a " +
                "base64-encoded string. Pass the result to a vision-language model (VLM) " +
                "to reason about what is currently visible in the scene.",
            InputSchema: new InputSchema(
                Type: "object",
                Properties: new Dictionary<string, PropertySchema>
                {
                    ["width"]  = new("integer", "Capture width  in pixels (default 1024)"),
                    ["height"] = new("integer", "Capture height in pixels (default 768)"),
                },
                Required: Array.Empty<string>()
            ),
            Categories: new[] { "viewport", "vision" },
            Outputs: new Dictionary<string, string>
            {
                ["image_base64"] = "PNG image as a base64 string",
                ["width"]        = "Actual pixel width of the capture",
                ["height"]       = "Actual pixel height of the capture",
                ["view_name"]    = "Name of the active viewport (e.g. Perspective)",
            }
        );

        public string Execute(Dictionary<string, string> args)
        {
            int w = args.TryGetValue("width",  out var ws) && int.TryParse(ws, out var wi) ? wi : 1024;
            int h = args.TryGetValue("height", out var hs) && int.TryParse(hs, out var hi) ? hi : 768;

            // CaptureToBitmap is a UI call — must run on Rhino's main thread.
            // InvokeOnUiThread queues but does not block, so use a ManualResetEventSlim
            // to synchronise the background HTTP thread with the UI callback.
            string json = null!;
            Exception? capEx = null;
            var done = new System.Threading.ManualResetEventSlim(false);

            RhinoApp.InvokeOnUiThread(() =>
            {
                try
                {
                    var doc  = RhinoDoc.ActiveDoc
                        ?? throw new InvalidOperationException("No active Rhino document.");
                    var view = doc.Views.ActiveView
                        ?? throw new InvalidOperationException("No active Rhino viewport.");

                    view.Redraw();
                    RhinoApp.Wait();

                    using var bitmap = view.CaptureToBitmap(new Size(w, h));
                    if (bitmap is null)
                        throw new InvalidOperationException(
                            "CaptureToBitmap returned null — make sure the Rhino window is visible and not minimized.");

                    using var ms = new MemoryStream();
                    bitmap.Save(ms, ImageFormat.Png);
                    var base64   = Convert.ToBase64String(ms.ToArray());
                    var viewName = view.ActiveViewport.Name;

                    json = JsonSerializer.Serialize(new
                    {
                        image_base64 = base64,
                        width        = bitmap.Width,
                        height       = bitmap.Height,
                        view_name    = viewName,
                    });
                }
                catch (Exception ex) { capEx = ex; }
                finally { done.Set(); }
            });

            // Wait up to 10 s for the UI thread to finish the capture
            done.Wait(TimeSpan.FromSeconds(10));

            if (capEx is not null)
                throw new InvalidOperationException(capEx.Message, capEx);

            if (json is null)
                throw new InvalidOperationException(
                    "Capture timed out — the UI thread did not respond within 10 seconds.");

            return json;
        }
    }
}
