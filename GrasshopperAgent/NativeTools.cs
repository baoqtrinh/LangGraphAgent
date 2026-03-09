// This file uses System.Drawing (GDI+) which is Windows-only.
// The GrasshopperAgent plugin only runs inside Rhino for Windows, so this is safe.
#pragma warning disable CA1416 // Validate platform compatibility
using GrasshopperAgent.Protocol;
using Rhino;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GrasshopperAgent
{
    /// <summary>
    /// Registry of built-in C# tools that are invoked directly — no .gh file needed.
    ///
    /// Adding a new built-in tool
    /// ──────────────────────────
    ///   1. Write a private static NativeTool MyTool() method below.
    ///   2. Call Register(MyTool()) inside the constructor.
    ///   The tool will automatically appear in /api/list_tools and be callable
    ///   via /api/call_tool just like any .gh-based tool.
    /// </summary>
    public class NativeToolRegistry
    {
        private readonly List<NativeTool> _tools = new();

        public IReadOnlyList<NativeTool> Tools => _tools;

        public NativeToolRegistry()
        {
            Register(CaptureViewportTool());
            Register(GetSceneInfoTool());
            Register(GetSelectedGeometryTool());
            Register(RunRhinoCommandTool());
            Register(ListRhinoCommandsTool());
            Register(GetCommandHelpTool());
        }

        private void Register(NativeTool tool) => _tools.Add(tool);

        public NativeTool? FindByName(string name) =>
            _tools.FirstOrDefault(t =>
                string.Equals(t.Definition.Name, name, StringComparison.OrdinalIgnoreCase));

        public List<ToolDefinition> ToMCPDefinitions() =>
            _tools.Select(t => t.Definition).ToList();

        // ── Tool: capture_viewport ────────────────────────────────────────────

        private static NativeTool CaptureViewportTool() => new(
            new ToolDefinition(
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
            ),
            Execute: args =>
            {
                int w = args.TryGetValue("width",  out var ws) && int.TryParse(ws, out var wi) ? wi : 1024;
                int h = args.TryGetValue("height", out var hs) && int.TryParse(hs, out var hi) ? hi : 768;

                var doc  = RhinoDoc.ActiveDoc
                    ?? throw new InvalidOperationException("No active Rhino document.");
                var view = doc.Views.ActiveView
                    ?? throw new InvalidOperationException("No active Rhino viewport.");

                using var bitmap = view.CaptureToBitmap(new Size(w, h));
                if (bitmap is null)
                    throw new InvalidOperationException("CaptureToBitmap returned null — viewport may not be visible.");

                using var ms = new MemoryStream();
                bitmap.Save(ms, ImageFormat.Png);
                var base64   = Convert.ToBase64String(ms.ToArray());
                var viewName = view.ActiveViewport.Name;

                return JsonSerializer.Serialize(new
                {
                    image_base64 = base64,
                    width        = bitmap.Width,
                    height       = bitmap.Height,
                    view_name    = viewName,
                });
            }
        );

        // ── Tool: get_scene_info ──────────────────────────────────────────────

        private static NativeTool GetSceneInfoTool() => new(
            new ToolDefinition(
                Name: "get_scene_info",
                Description:
                    "Returns metadata about the current Rhino scene: object counts by type, " +
                    "layer names, active viewport name, and camera position. " +
                    "Useful context before drawing or verifying geometry.",
                InputSchema: new InputSchema(
                    Type: "object",
                    Properties: new Dictionary<string, PropertySchema>(),
                    Required: Array.Empty<string>()
                ),
                Categories: new[] { "scene", "info" },
                Outputs: new Dictionary<string, string>
                {
                    ["scene_json"] = "JSON object with counts, layers, and camera info",
                }
            ),
            Execute: args =>
            {
                var doc = RhinoDoc.ActiveDoc
                    ?? throw new InvalidOperationException("No active Rhino document.");

                // Object counts by type
                var counts = new Dictionary<string, int>();
                foreach (var obj in doc.Objects)
                {
                    if (obj.IsDeleted) continue;
                    var key = obj.ObjectType.ToString();
                    counts[key] = counts.TryGetValue(key, out var n) ? n + 1 : 1;
                }

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
                    object_count = doc.Objects.Count,
                    objects      = counts,
                    layers,
                    active_view  = vp?.Name ?? "none",
                    projection   = vp?.IsParallelProjection == true ? "parallel" : "perspective",
                    camera_target = camTarget,
                    units        = doc.ModelUnitSystem.ToString(),
                });
            }
        );

        // ── Tool: get_selected_geometry ───────────────────────────────────────

        private static NativeTool GetSelectedGeometryTool() => new(
            new ToolDefinition(
                Name: "get_selected_geometry",
                Description:
                    "Returns details about every currently selected object in the Rhino scene. " +
                    "For each object: its GUID, object type, layer, name (if any), " +
                    "and bounding-box extents so the LLM knows exact position and size. " +
                    "Call this before editing or referencing specific geometry.",
                InputSchema: new InputSchema(
                    Type: "object",
                    Properties: new Dictionary<string, PropertySchema>()
                    {
                        ["include_bbox"] = new("boolean", "Include bounding box min/max corners (default true)"),
                    },
                    Required: Array.Empty<string>()
                ),
                Categories: new[] { "selection", "geometry", "info" },
                Outputs: new Dictionary<string, string>
                {
                    ["selected_json"] = "JSON array of selected objects with id, type, layer, name, bbox",
                    ["count"]         = "Number of selected objects",
                }
            ),
            Execute: args =>
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

                    var bbox  = obj.Geometry?.GetBoundingBox(accurate: false);
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
        );

        // ── Tool: run_rhino_command ───────────────────────────────────────────

        private static NativeTool RunRhinoCommandTool() => new(
            new ToolDefinition(
                Name: "run_rhino_command",
                Description:
                    "Runs any Rhino command string exactly as if the user typed it in the " +
                    "command line. Supports both interactive commands (e.g. '_Box 0,0,0 10,10,10') " +
                    "and silent scripted commands prefixed with underscore and bang " +
                    "(e.g. '! _-Layer _New MyLayer _Enter'). " +
                    "Returns the count of objects before/after, GUIDs of newly added objects, " +
                    "and a success flag.",
                InputSchema: new InputSchema(
                    Type: "object",
                    Properties: new Dictionary<string, PropertySchema>
                    {
                        ["command"] = new("string",
                            "The full Rhino command string to execute, e.g. " +
                            "'_Box 0,0,0 10,10,5' or '! _-Layer _New Walls _Enter'. " +
                            "Prefix commands with '_' for language-neutral English names. " +
                            "Prefix with '!' to cancel any running command first."),
                        ["echo"] = new("boolean",
                            "When true (default) the command echoes to the Rhino command history window."),
                    },
                    Required: new[] { "command" }
                ),
                Categories: new[] { "command", "scripting" },
                Outputs: new Dictionary<string, string>
                {
                    ["success"]        = "true if the command ran without error",
                    ["objects_before"] = "Object count in the document before the command",
                    ["objects_after"]  = "Object count in the document after the command",
                    ["objects_added"]  = "Net objects added (after minus before)",
                    ["new_object_ids"] = "GUIDs of objects added by this command (if any)",
                    ["error"]          = "Error message if the command failed",
                }
            ),
            Execute: args =>
            {
                if (!args.TryGetValue("command", out var command) || string.IsNullOrWhiteSpace(command))
                    throw new ArgumentException("'command' argument is required and must not be empty.");

                bool echo = !args.TryGetValue("echo", out var echoStr)
                    || !string.Equals(echoStr, "false", StringComparison.OrdinalIgnoreCase);

                var doc = RhinoDoc.ActiveDoc
                    ?? throw new InvalidOperationException("No active Rhino document.");

                // Snapshot object IDs before
                var idsBefore = new HashSet<Guid>(doc.Objects
                    .Where(o => !o.IsDeleted)
                    .Select(o => o.Id));
                int countBefore = idsBefore.Count;

                bool ok = RhinoApp.RunScript(command, echo);

                // Snapshot after
                var idsAfter = doc.Objects
                    .Where(o => !o.IsDeleted)
                    .Select(o => o.Id)
                    .ToList();
                int countAfter = idsAfter.Count;

                var addedIds = idsAfter
                    .Where(id => !idsBefore.Contains(id))
                    .Select(id => id.ToString())
                    .ToList();

                return JsonSerializer.Serialize(new
                {
                    success        = ok,
                    objects_before = countBefore,
                    objects_after  = countAfter,
                    objects_added  = countAfter - countBefore,
                    new_object_ids = addedIds,
                    error          = ok ? (string?)null : "RunScript returned false — check command syntax.",
                });
            }
        );

        // ── Tool: list_rhino_commands ─────────────────────────────────────────

        private static NativeTool ListRhinoCommandsTool() => new(
            new ToolDefinition(
                Name: "list_rhino_commands",
                Description:
                    "Lists all Rhino commands registered in this session with their English name " +
                    "and plug-in source. Use this to discover available command names before " +
                    "calling run_rhino_command. Optionally filter by a search term.",
                InputSchema: new InputSchema(
                    Type: "object",
                    Properties: new Dictionary<string, PropertySchema>
                    {
                        ["filter"] = new("string",
                            "Optional case-insensitive substring to filter command names, " +
                            "e.g. 'box' returns Box, BoxEdit, SubDBox, etc."),
                        ["scriptable_only"] = new("boolean",
                            "When true, only return commands that support scripted execution."),
                    },
                    Required: Array.Empty<string>()
                ),
                Categories: new[] { "command", "info", "discovery" },
                Outputs: new Dictionary<string, string>
                {
                    ["commands_json"] = "JSON array of { name } objects sorted alphabetically",
                    ["count"]         = "Number of commands returned",
                }
            ),
            Execute: args =>
            {
                args.TryGetValue("filter", out var filter);

                var allNames = Rhino.Commands.Command.GetCommandNames(true, false);

                var results = allNames
                    .Where(name =>
                        string.IsNullOrEmpty(filter) ||
                        name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    .Select(name => new { name })
                    .OrderBy(c => c.name)
                    .ToList();

                return JsonSerializer.Serialize(new
                {
                    count    = results.Count,
                    commands = results,
                });
            }
        );

        // ── Tool: get_command_help ────────────────────────────────────────────

        private static NativeTool GetCommandHelpTool() => new(
            new ToolDefinition(
                Name: "get_command_help",
                Description:
                    "Returns the syntax documentation for a Rhino command by reading the " +
                    "local Rhino help HTML file that ships with the installation. " +
                    "Use this after list_rhino_commands to learn a command's options and " +
                    "argument order before calling run_rhino_command. " +
                    "Returns: the scripted form to use, the docs URL, and the full help text.",
                InputSchema: new InputSchema(
                    Type: "object",
                    Properties: new Dictionary<string, PropertySchema>
                    {
                        ["command_name"] = new("string",
                            "The Rhino command name to look up, e.g. 'Box', '_Box', or 'box'. " +
                            "Leading underscores, dashes and bangs are stripped automatically."),
                        ["locale"] = new("string",
                            "Help locale folder, e.g. 'en-us' (default), 'de-de', 'ja-jp'."),
                    },
                    Required: new[] { "command_name" }
                ),
                Categories: new[] { "command", "help", "discovery" },
                Outputs: new Dictionary<string, string>
                {
                    ["command_name"]  = "Normalised command name (no prefixes)",
                    ["scripted_form"] = "Language-neutral form to pass to run_rhino_command, e.g. '_Box'",
                    ["silent_form"]   = "Dialog-suppressed form, e.g. '_-Box'",
                    ["help_url"]      = "McNeel online docs URL for this command",
                    ["help_file"]     = "Path to the local HTML help file (if found)",
                    ["help_text"]     = "Plain-text content of the help file (HTML tags removed)",
                    ["found"]         = "true if the local help file was found on disk",
                }
            ),
            Execute: args =>
            {
                if (!args.TryGetValue("command_name", out var rawName) || string.IsNullOrWhiteSpace(rawName))
                    throw new ArgumentException("'command_name' is required.");

                args.TryGetValue("locale", out var locale);
                if (string.IsNullOrWhiteSpace(locale)) locale = "en-us";

                // Normalise: strip leading !, _, - characters
                var name = rawName.TrimStart('!', ' ').TrimStart('_').TrimStart('-').Trim();
                var nameLower = name.ToLowerInvariant();

                var scriptedForm = $"_{name}";
                var silentForm   = $"_-{name}";
                var helpUrl      = $"https://docs.mcneel.com/rhino/8/help/{locale}/commands/{nameLower}.htm";

                // ── Locate local help file ───────────────────────────────────
                // RhinoCommon.dll lives in <RhinoInstall>\System\ — walk up one level.
                var rhinoSystemDir = Path.GetDirectoryName(
                    typeof(RhinoApp).Assembly.Location) ?? "";
                var rhinoRootDir   = Path.GetDirectoryName(rhinoSystemDir) ?? "";

                // Rhino 7/8 ship help under <root>\Help\<locale>\Commands\
                // Some builds place it under <root>\System\Help\<locale>\Commands\
                var candidates = new[]
                {
                    Path.Combine(rhinoRootDir,   "Help",        locale, "Commands", $"{nameLower}.htm"),
                    Path.Combine(rhinoRootDir,   "Help",        locale, "commands", $"{nameLower}.htm"),
                    Path.Combine(rhinoSystemDir, "Help",        locale, "Commands", $"{nameLower}.htm"),
                    Path.Combine(rhinoSystemDir, "Help",        locale, "commands", $"{nameLower}.htm"),
                    // Localised .chm extraction path (some installs)
                    Path.Combine(rhinoRootDir,   "en",          "commands",          $"{nameLower}.htm"),
                };

                string? helpFile = candidates.FirstOrDefault(File.Exists);
                bool found = helpFile is not null;
                string helpText = "";

                if (found)
                {
                    try
                    {
                        var html = File.ReadAllText(helpFile!);
                        helpText = StripHtml(html);
                    }
                    catch (Exception ex)
                    {
                        helpText = $"(could not read file: {ex.Message})";
                    }
                }
                else
                {
                    helpText = $"Local help file not found. Visit: {helpUrl}";
                }

                return JsonSerializer.Serialize(new
                {
                    command_name  = name,
                    scripted_form = scriptedForm,
                    silent_form   = silentForm,
                    help_url      = helpUrl,
                    help_file     = helpFile ?? "",
                    help_text     = helpText,
                    found,
                });
            }
        );

        // ── HTML → plain text helper ──────────────────────────────────────────

        private static string StripHtml(string html)
        {
            // Remove <script> and <style> blocks entirely
            html = Regex.Replace(html, @"<(script|style)[^>]*>.*?<\/\1>",
                "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            // Convert common block elements to newlines
            html = Regex.Replace(html, @"<(br|p|li|h[1-6]|tr|div)[^>]*>",
                "\n", RegexOptions.IgnoreCase);
            // Strip all remaining tags
            html = Regex.Replace(html, @"<[^>]+>", "");
            // Decode common HTML entities
            html = html
                .Replace("&nbsp;",  " ")
                .Replace("&amp;",   "&")
                .Replace("&lt;",    "<")
                .Replace("&gt;",    ">")
                .Replace("&quot;",  "\"")
                .Replace("&#39;",   "'");
            // Collapse runs of blank lines
            html = Regex.Replace(html, @"\n{3,}", "\n\n");
            return html.Trim();
        }
    }

    /// <summary>
    /// A single built-in tool: its MCP schema definition + the C# handler function.
    /// </summary>
    public record NativeTool(
        ToolDefinition Definition,
        Func<Dictionary<string, string>, string> Execute
    );
}
