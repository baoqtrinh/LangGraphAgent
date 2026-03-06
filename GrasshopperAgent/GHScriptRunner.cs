using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;
using Rhino;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace GrasshopperAgent
{
    /// <summary>
    /// Loads a .gh file in memory, sets named input parameters, runs the solution,
    /// and reads named output parameters.  All Grasshopper operations are dispatched
    /// to the Rhino UI thread to remain thread-safe.
    /// </summary>
    public class GHScriptRunner
    {
        private readonly int _solveTimeoutMs;

        public GHScriptRunner(int solveTimeoutMs = 30_000)
        {
            _solveTimeoutMs = solveTimeoutMs;
        }

        /// <summary>
        /// Execute a Grasshopper definition file.
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
            var result = new Dictionary<string, string>();
            Exception? runError = null;
            var done = new ManualResetEventSlim(false);

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

                    // ── Read outputs by NickName ───────────────────────────────
                    var outputSet = new HashSet<string>(outputNames, StringComparer.OrdinalIgnoreCase);
                    foreach (var obj in doc.Objects)
                    {
                        if (!outputSet.Contains(obj.NickName)) continue;
                        if (obj is IGH_Param param)
                        {
                            var vals = param.VolatileData.AllData(true).ToList();
                            result[obj.NickName] = vals.Count > 0
                                ? string.Join(", ", vals.Select(v => v?.ToString() ?? ""))
                                : "(no output)";
                        }
                    }

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
    }
}
