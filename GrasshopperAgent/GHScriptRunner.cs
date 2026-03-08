using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace GrasshopperAgent
{
    /// <summary>
    /// Loads a .gh file, sets named input parameters, runs the solution,
    /// and reads named output parameters.  All Grasshopper operations are dispatched
    /// to the Rhino UI thread to remain thread-safe.
    /// </summary>
    public class GHScriptRunner
    {
        private readonly int _solveTimeoutMs;

        /// <summary>
        /// When true, the .gh file is opened in the active Grasshopper canvas so the
        /// user can watch it run.  When false (default) it runs in a hidden in-memory document.
        /// </summary>
        public bool ShowInCanvas { get; set; } = false;

        /// <summary>
        /// Only relevant when <see cref="ShowInCanvas"/> is true.
        /// When true, the document remains open on the canvas after execution.
        /// When false (default) it is removed and disposed once outputs are collected.
        /// </summary>
        public bool KeepOpen { get; set; } = false;

        /// <summary>
        /// When true (default), any geometry outputs (Circle, Curve, Brep, Mesh, …) are
        /// automatically baked into the active Rhino document so they appear in the viewport.
        /// The baked object's GUID is appended to the result so the agent can reference it
        /// in follow-up tool calls.
        /// </summary>
        public bool BakeToViewport { get; set; } = true;

        public GHScriptRunner(int solveTimeoutMs = 30_000)
        {
            _solveTimeoutMs = solveTimeoutMs;
        }

        /// <summary>
        /// Execute a Grasshopper definition file.
        /// Delegates to <see cref="ExecuteInActiveWindow"/> or the hidden-document path
        /// depending on <see cref="ShowInCanvas"/>.
        /// </summary>
        /// <param name="filePath">Full path to the .gh file.</param>
        /// <param name="inputs">Named input values — must match the NickName of sliders/panels in the file.</param>
        /// <param name="outputNames">NickNames of the output panels/params to read.</param>
        /// <returns>Dictionary of output name → value string.</returns>
        public Dictionary<string, string> Execute(
            string filePath,
            Dictionary<string, string> inputs,
            IEnumerable<string> outputNames)
        {
            return ShowInCanvas
                ? ExecuteInActiveWindow(filePath, inputs, outputNames)
                : ExecuteInBackground(filePath, inputs, outputNames);
        }

        // ── Hidden in-memory execution ────────────────────────────────────────

        private Dictionary<string, string> ExecuteInBackground(
            string filePath,
            Dictionary<string, string> inputs,
            IEnumerable<string> outputNames)
        {
            var result = new Dictionary<string, string>();
            Exception? runError = null;
            var done = new ManualResetEventSlim(false);
            var outputSet = new HashSet<string>(outputNames, StringComparer.OrdinalIgnoreCase);

            RhinoApp.InvokeOnUiThread(new Action(() =>
            {
                try
                {
                    // ── Load document ──────────────────────────────────────────
                    var io = new GH_DocumentIO();
                    if (!io.Open(filePath))
                        throw new FileLoadException($"Cannot open Grasshopper file: {filePath}");

                    var doc = io.Document
                        ?? throw new InvalidOperationException("GH_DocumentIO.Document is null after Open()");

                    doc.Enabled = true;

                    // ── Set inputs by NickName ─────────────────────────────────
                    foreach (var obj in doc.Objects)
                    {
                        if (inputs.TryGetValue(obj.NickName, out var rawValue))
                            SetObjectValue(obj, rawValue);
                    }

                    // ── Run solution ───────────────────────────────────────────
                    doc.NewSolution(false);   // false = synchronous

                    // ── Read & bake outputs ────────────────────────────────────
                    CollectOutputs(doc, outputSet, result, BakeToViewport);

                    doc.Dispose();
                }
                catch (Exception ex)
                {
                    runError = ex;
                }
                finally
                {
                    done.Set();
                }
            }));

            if (!done.Wait(_solveTimeoutMs))
                throw new TimeoutException(
                    $"GH script execution timed out after {_solveTimeoutMs / 1000}s: {filePath}");

            if (runError is not null)
                throw new Exception($"GH script error: {runError.Message}", runError);

            return result;
        }

        // ── Visible canvas execution ──────────────────────────────────────────

        private Dictionary<string, string> ExecuteInActiveWindow(
            string filePath,
            Dictionary<string, string> inputs,
            IEnumerable<string> outputNames)
        {
            var result = new Dictionary<string, string>();
            Exception? runError = null;
            var done = new ManualResetEventSlim(false);
            var outputSet = new HashSet<string>(outputNames, StringComparer.OrdinalIgnoreCase);

            RhinoApp.InvokeOnUiThread(new Action(() =>
            {
                try
                {
                    // ── Load document ──────────────────────────────────────────
                    var io = new GH_DocumentIO();
                    if (!io.Open(filePath))
                        throw new FileLoadException($"Cannot open Grasshopper file: {filePath}");

                    var doc = io.Document
                        ?? throw new InvalidOperationException("GH_DocumentIO.Document is null after Open()");

                    doc.Enabled = true;

                    // ── Show in the active canvas ──────────────────────────────
                    Grasshopper.Instances.DocumentServer.AddDocument(doc);
                    var canvas = Grasshopper.Instances.ActiveCanvas;
                    canvas.Document = doc;
                    canvas.Refresh();

                    // ── Set inputs by NickName ─────────────────────────────────
                    foreach (var obj in doc.Objects)
                    {
                        if (inputs.TryGetValue(obj.NickName, out var rawValue))
                            SetObjectValue(obj, rawValue);
                    }

                    // ── Run solution ───────────────────────────────────────────
                    doc.NewSolution(false);   // false = synchronous

                    // ── Read & bake outputs ────────────────────────────────────
                    CollectOutputs(doc, outputSet, result, BakeToViewport);

                    // ── Optionally remove after execution ──────────────────────
                    if (!KeepOpen)
                    {
                        Grasshopper.Instances.DocumentServer.RemoveDocument(doc);
                        doc.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    runError = ex;
                }
                finally
                {
                    done.Set();
                }
            }));

            if (!done.Wait(_solveTimeoutMs))
                throw new TimeoutException(
                    $"GH script execution timed out after {_solveTimeoutMs / 1000}s: {filePath}");

            if (runError is not null)
                throw new Exception($"GH script error: {runError.Message}", runError);

            return result;
        }

        // ── Value setters ─────────────────────────────────────────────────────

        private static void SetObjectValue(IGH_DocumentObject obj, string rawValue)
        {
            switch (obj)
            {
                case GH_NumberSlider slider:
                    if (decimal.TryParse(rawValue, out var dec))
                        slider.SetSliderValue(dec);
                    break;

                case GH_Panel panel:
                    panel.UserText = rawValue;
                    panel.ExpireSolution(false);
                    break;

                case GH_BooleanToggle toggle:
                    bool bl;
                    if (bool.TryParse(rawValue, out bl) ||
                        (int.TryParse(rawValue, out var bi) && (bl = bi != 0) == bl))
                    {
                        toggle.Value = bl;
                        toggle.ExpireSolution(false);
                    }
                    break;

                case IGH_Param param when param.SourceCount == 0:
                    // Generic param — inject persistent data
                    param.ClearData();
                    if (double.TryParse(rawValue, out var dbl))
                        param.AddVolatileData(new Grasshopper.Kernel.Data.GH_Path(0), 0, new GH_Number(dbl));
                    else
                        param.AddVolatileData(new Grasshopper.Kernel.Data.GH_Path(0), 0, new GH_String(rawValue));
                    break;
            }
        }

        // ── Output collection + auto-bake ─────────────────────────────────────

        /// <summary>
        /// Reads every named output param from the solved document.
        /// Text/number params → string in result dict.
        /// Geometry params → also baked to the active Rhino doc; GUID appended to entry.
        /// Viewport is redrawn when any geometry was baked.
        /// </summary>
        private static void CollectOutputs(
            GH_Document doc,
            HashSet<string> outputSet,
            Dictionary<string, string> result,
            bool bake)
        {
            bool didBake = false;

            foreach (var obj in doc.Objects)
            {
                if (!outputSet.Contains(obj.NickName)) continue;
                if (obj is not IGH_Param param) continue;

                var allData = param.VolatileData.AllData(true).ToList();
                var textParts = allData.Count > 0
                    ? allData.Select(v => v?.ToString() ?? "").Where(s => !string.IsNullOrEmpty(s)).ToList()
                    : new List<string>();

                var bakedGuids = new List<string>();
                if (bake)
                {
                    foreach (var goo in allData)
                    {
                        var id = TryBakeGoo(goo);
                        if (id != Guid.Empty)
                        {
                            bakedGuids.Add(id.ToString());
                            didBake = true;
                        }
                    }
                }

                var summary = textParts.Count > 0
                    ? string.Join(", ", textParts)
                    : "(no output)";

                if (bakedGuids.Count > 0)
                    summary += "\nbaked_guid: " + string.Join(", ", bakedGuids);

                result[obj.NickName] = summary;
            }

            if (didBake)
                RhinoDoc.ActiveDoc?.Views.Redraw();
        }

        /// <summary>
        /// Bakes a single GH geometry wrapper into the active Rhino document.
        /// Returns the new object GUID, or Guid.Empty if this GH type is not geometry.
        /// </summary>
        private static Guid TryBakeGoo(IGH_Goo goo)
        {
            var doc = RhinoDoc.ActiveDoc;
            if (doc == null || goo == null) return Guid.Empty;

            return goo switch
            {
                GH_Circle  g => doc.Objects.AddCircle(g.Value),
                GH_Arc     g => doc.Objects.AddArc(g.Value),
                GH_Line    g => doc.Objects.AddLine(g.Value),
                GH_Curve   g when g.Value != null => doc.Objects.AddCurve(g.Value),
                GH_Surface g when g.Value != null => doc.Objects.AddBrep(g.Value),
                GH_Brep    g when g.Value != null => doc.Objects.AddBrep(g.Value),
                GH_Mesh    g when g.Value != null => doc.Objects.AddMesh(g.Value),
                GH_Point   g => doc.Objects.AddPoint(g.Value),
                GH_Box     g => doc.Objects.AddBrep(g.Value.ToBrep()),
                _            => Guid.Empty,
            };
        }
    }
}
