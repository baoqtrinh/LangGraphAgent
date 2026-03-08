using GrasshopperAgent.Protocol;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Rhino;
using System.IO;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Threading;

namespace GrasshopperAgent
{
    /// <summary>
    /// A single tool discovered from a .gh file.
    /// </summary>
    public record GHToolDefinition(
        string FilePath,
        string Name,
        string Description,
        List<ParameterSpec> Inputs,
        List<ParameterSpec> Outputs,
        string ToolGroupName = ""   // non-empty when this tool lives inside a TOOL_ group in the file
    );

    public record ParameterSpec(string Name, string Description, string Type = "string");

    /// <summary>
    /// Scans a folder for .gh files and extracts tool metadata entirely from
    /// inside each file — no external CSV required.
    ///
    /// Convention inside the .gh file
    /// ────────────────────────────────
    ///  • Add a Panel anywhere, set its NickName to DESCRIPTION.
    ///    Its text becomes the tool description shown to the LLM.
    ///    Include what each input/output means so the agent knows what to pass:
    ///      "Creates a circle. plane: origin as x,y,z e.g. 0,0,0  radius: float"
    ///
    ///  • Create a canvas Group, set its label to INPUT.
    ///    Place all input sliders / panels / toggles inside it.
    ///    Their NickNames become the JSON parameter keys.
    ///
    ///  • Create a canvas Group, set its label to OUTPUT.
    ///    Place all output panels inside it.
    ///    Their NickNames become the result keys returned to the agent.
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

        // ── Scan all .gh files ────────────────────────────────────────────────

        private void Load()
        {
            _tools.Clear();
            if (!Directory.Exists(_folder)) return;

            foreach (var ghFile in Directory.GetFiles(_folder, "*.gh", SearchOption.TopDirectoryOnly))
                _tools.AddRange(ScanGHFile(ghFile));
        }

        /// <summary>
        /// Opens a .gh file on the GH UI thread (read-only) and returns one
        /// <see cref="GHToolDefinition"/> per tool found inside.
        ///
        /// Multi-tool mode (preferred):
        ///   – Any canvas Group whose label starts with "TOOL_" defines one tool.
        ///     The name is the part after "TOOL_", normalised to snake_case.
        ///     Inside each such group place:
        ///       • A Group labelled INPUT  — sliders, panels and toggles become JSON inputs.
        ///       • A Group labelled OUTPUT — params / panels become JSON outputs.
        ///       • A Panel with NickName DESCRIPTION — its text is the LLM description.
        ///
        /// Legacy / single-tool mode (automatic fallback):
        ///   – If no TOOL_ groups are found the file is treated as a single tool and
        ///     its name is derived from the file name (fully backward-compatible).
        /// </summary>
        private static List<GHToolDefinition> ScanGHFile(string filePath)
        {
            var results = new List<GHToolDefinition>();
            var done    = new ManualResetEventSlim(false);

            RhinoApp.InvokeOnUiThread(new Action(() =>
            {
                try
                {
                    var io = new GH_DocumentIO();
                    if (!io.Open(filePath)) return;

                    var doc = io.Document;
                    if (doc == null) return;

                    // ── Build GUID lookup ──────────────────────────────────────
                    var byId = doc.Objects.ToDictionary(o => o.InstanceGuid);

                    // ── Classify top-level groups ──────────────────────────────
                    var toolGroups    = new List<GH_Group>();
                    GH_Group? legacyInput  = null;
                    GH_Group? legacyOutput = null;
                    GH_Panel? legacyDesc   = null;

                    foreach (var obj in doc.Objects)
                    {
                        if (obj is GH_Group g)
                        {
                            var lbl = g.NickName?.Trim() ?? "";
                            if (lbl.StartsWith("TOOL_", StringComparison.OrdinalIgnoreCase))
                                toolGroups.Add(g);
                            else if (lbl.Equals("INPUT",  StringComparison.OrdinalIgnoreCase))
                                legacyInput = g;
                            else if (lbl.Equals("OUTPUT", StringComparison.OrdinalIgnoreCase))
                                legacyOutput = g;
                        }
                        else if (legacyDesc is null && obj is GH_Panel dp &&
                                 (dp.NickName?.Trim() ?? "").Equals("DESCRIPTION", StringComparison.OrdinalIgnoreCase))
                        {
                            var t = dp.UserText?.Trim() ?? "";
                            if (!string.IsNullOrEmpty(t)) legacyDesc = dp;
                        }
                    }

                    if (toolGroups.Count > 0)
                    {
                        // ── Multi-tool: one definition per TOOL_ group ─────────
                        foreach (var tg in toolGroups)
                        {
                            var groupLabel = tg.NickName!.Trim();
                            var toolName   = NormalizeToolName(groupLabel.Substring(5)); // strip "TOOL_"
                            var desc       = $"Run Grasshopper script: {toolName}";
                            var inputs     = new List<ParameterSpec>();
                            var outputs    = new List<ParameterSpec>();

                            foreach (var memberId in tg.ObjectIDs)
                            {
                                if (!byId.TryGetValue(memberId, out var m)) continue;

                                if (m is GH_Group sub)
                                {
                                    var subLbl = sub.NickName?.Trim() ?? "";
                                    if (subLbl.Equals("INPUT",  StringComparison.OrdinalIgnoreCase))
                                        CollectInputParams(sub.ObjectIDs, byId, inputs);
                                    else if (subLbl.Equals("OUTPUT", StringComparison.OrdinalIgnoreCase))
                                        CollectOutputParams(sub.ObjectIDs, byId, outputs);
                                }
                                else if (m is GH_Panel panel &&
                                         (panel.NickName?.Trim() ?? "").Equals("DESCRIPTION", StringComparison.OrdinalIgnoreCase))
                                {
                                    var t = panel.UserText?.Trim() ?? "";
                                    if (!string.IsNullOrEmpty(t)) desc = t;
                                }
                            }

                            results.Add(new GHToolDefinition(filePath, toolName, desc, inputs, outputs, groupLabel));
                        }
                    }
                    else
                    {
                        // ── Legacy single-tool: name from file name ────────────
                        var filename = Path.GetFileName(filePath);
                        var toolName = FileNameToToolName(filename);
                        var desc     = legacyDesc?.UserText?.Trim() ?? $"Run Grasshopper script: {filename}";
                        var inputs   = new List<ParameterSpec>();
                        var outputs  = new List<ParameterSpec>();

                        if (legacyInput  is not null) CollectInputParams(legacyInput.ObjectIDs,   byId, inputs);
                        if (legacyOutput is not null) CollectOutputParams(legacyOutput.ObjectIDs, byId, outputs);

                        results.Add(new GHToolDefinition(filePath, toolName, desc, inputs, outputs));
                    }

                    doc.Dispose();
                }
                catch { /* skip unreadable files */ }
                finally { done.Set(); }
            }));

            done.Wait(10_000);
            return results;
        }

        private static void CollectInputParams(
            IEnumerable<Guid> ids,
            Dictionary<Guid, IGH_DocumentObject> byId,
            List<ParameterSpec> target)
        {
            foreach (var id in ids)
            {
                if (!byId.TryGetValue(id, out var member)) continue;
                var nick = member.NickName?.Trim() ?? "";
                if (string.IsNullOrEmpty(nick)) continue;

                string? inputType = member switch
                {
                    GH_NumberSlider  => "number",
                    GH_BooleanToggle => "boolean",
                    GH_Panel         => "string",
                    IGH_Param p      => TypeNameToSimpleType(p.TypeName),
                    _                => null,
                };
                if (inputType != null)
                    target.Add(new ParameterSpec(nick, nick, inputType));
            }
        }

        private static void CollectOutputParams(
            IEnumerable<Guid> ids,
            Dictionary<Guid, IGH_DocumentObject> byId,
            List<ParameterSpec> target)
        {
            foreach (var id in ids)
            {
                if (!byId.TryGetValue(id, out var member)) continue;
                var nick = member.NickName?.Trim() ?? "";
                if (string.IsNullOrEmpty(nick)) continue;

                string outputType = member switch
                {
                    GH_Panel panel => InferOutputTypeFromPanel(panel),
                    IGH_Param p    => TypeNameToSimpleType(p.TypeName),
                    _              => TypeNameToSimpleType(member.GetType().Name),
                };
                target.Add(new ParameterSpec(nick, nick, outputType));
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Maps a Grasshopper TypeName string to a simplified agent-facing type label.
        /// Covers the most common GH param types; unknown types fall back to "string".
        /// </summary>
        private static string TypeNameToSimpleType(string? typeName) =>
            (typeName?.ToLower() ?? "") switch
            {
                // ── TypeName values (from IGH_Param.TypeName) ─────────────────
                "number" or "double" or "float"    => "number",
                "integer" or "int"                 => "integer",
                "boolean" or "bool"                => "boolean",
                "text" or "string"                 => "string",
                "point" or "point3d"               => "point",
                "plane"                            => "plane",
                "vector" or "vector3d"             => "vector",
                "geometry" or "geometrybase"
                    or "generic geometry"
                    or "circle" or "arc" or "line"
                    or "curve" or "nurbs curve"
                    or "brep" or "surface"
                    or "nurbs surface" or "mesh"   => "geometry",
                // ── Class names (from GetType().Name fallback) ─────────────────
                "param_geometry" or "param_brep" or "param_surface"
                    or "param_curve" or "param_nurbscurve"
                    or "param_circle" or "param_arc" or "param_line"
                    or "param_mesh" or "param_box"  => "geometry",
                "param_number" or "gh_numberslider" => "number",
                "param_integer"                    => "integer",
                "param_boolean" or "gh_booleantogg" => "boolean",
                "param_point"                      => "point",
                "param_plane"                      => "plane",
                "param_vector"                     => "vector",
                _                                  => "string",
            };

        /// <summary>
        /// For a Panel output, look at the TypeName of the first wired source
        /// because a Panel's own TypeName is always "Text".
        /// </summary>
        private static string InferOutputTypeFromPanel(GH_Panel panel)
        {
            if (panel.Sources.Count == 0) return "string";
            return TypeNameToSimpleType(panel.Sources[0].TypeName);
        }

        private static string NormalizeToolName(string raw) =>
            Regex.Replace(raw, @"[^a-zA-Z0-9_]", "_").ToLower();

        private static string FileNameToToolName(string filename) =>
            NormalizeToolName(Path.GetFileNameWithoutExtension(filename));

        /// <summary>Convert the loaded tools to MCP ToolDefinitions for the HTTP response.</summary>
        public List<ToolDefinition> ToMCPDefinitions() =>
            _tools.Select(t =>
            {
                var props    = t.Inputs.ToDictionary(i => i.Name, i => new PropertySchema(i.Type, i.Description));
                var required = t.Inputs.Select(i => i.Name).ToArray();
                var outputs  = t.Outputs.ToDictionary(o => o.Name, o => o.Type);
                return new ToolDefinition(
                    t.Name,
                    t.Description,
                    new InputSchema("object", props, required),
                    new[] { "grasshopper", "custom" },
                    outputs);
            }).ToList();

        public GHToolDefinition? FindByName(string name) =>
            _tools.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }
}
