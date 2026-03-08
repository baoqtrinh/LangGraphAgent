using Grasshopper.Kernel;
using System;
using System.Drawing;
using System.IO;

namespace GrasshopperAgent
{
    /// <summary>
    /// The single Grasshopper component that:
    /// 1. Reads a folder path + port number
    /// 2. Scans the folder for .gh files + tools.csv
    /// 3. Starts (or stops) an HTTP MCP server
    /// 4. Outputs status information
    ///
    /// Students place ONE instance of this component on their canvas,
    /// point it to their tools folder, and toggle Active to True.
    /// </summary>
    public class GrasshopperAgentComponent : GH_Component
    {
        private HttpMCPServer? _server;
        private int _lastPort = -1;
        private string _lastFolder = "";

        public GrasshopperAgentComponent()
            : base(
                "GH Tool Server",       // display name
                "MCPServer",            // abbreviation
                "Expose .gh scripts as MCP tools for LangGraph agents.\n\n" +
                "1. Set Folder to the directory containing your .gh files and tools.csv\n" +
                "2. Set Active to True to start the server\n" +
                "3. Connect your LangGraph agent to the server URL",
                "MCP",                  // category tab
                "Server"                // sub-category
            )
        { }

        // Keep the original project GUID so existing .ghx files keep working
        public override Guid ComponentGuid => new Guid("5091addf-9a14-486f-a373-4651aea3b8e7");

        protected override Bitmap Icon => null;

        // ── Input parameters ──────────────────────────────────────────────────
        protected override void RegisterInputParams(GH_InputParamManager pm)
        {
            pm.AddTextParameter(
                "Folder", "F",
                "Path to the folder containing .gh tool files and tools.csv",
                GH_ParamAccess.item);

            pm.AddIntegerParameter(
                "Port", "P",
                "HTTP port for the MCP server (default 5100)",
                GH_ParamAccess.item, 5100);

            pm.AddBooleanParameter(
                "Active", "A",
                "Set to True to start the server, False to stop it",
                GH_ParamAccess.item, false);

            pm.AddBooleanParameter(
                "ShowInCanvas", "V",
                "When True, each tool's .gh file is opened in the active Grasshopper canvas so you can watch it run. " +
                "When False (default) the file executes invisibly in memory.",
                GH_ParamAccess.item, false);

            pm.AddBooleanParameter(
                "KeepOpen", "K",
                "Only relevant when ShowInCanvas is True. " +
                "When True, the document stays on the canvas after execution. " +
                "When False (default) it is removed and disposed once outputs are collected.",
                GH_ParamAccess.item, false);

            pm.AddBooleanParameter(
                "BakeToViewport", "B",
                "When True (default), any geometry outputs are automatically baked into the Rhino document " +
                "so they appear in the viewport. The baked GUID is appended to the tool result so the agent " +
                "can reference it in follow-up tool calls.",
                GH_ParamAccess.item, true);
        }

        // ── Output parameters ─────────────────────────────────────────────────
        protected override void RegisterOutputParams(GH_OutputParamManager pm)
        {
            pm.AddTextParameter("Status", "S", "Server status message", GH_ParamAccess.item);
            pm.AddIntegerParameter("Tools", "T", "Number of GH tools loaded", GH_ParamAccess.item);
            pm.AddTextParameter("URL", "U", "Base URL of the running server", GH_ParamAccess.item);
        }

        // ── Solve ─────────────────────────────────────────────────────────────
        protected override void SolveInstance(IGH_DataAccess da)
        {
            string folder = "";
            int port = 5100;
            bool active = false;
            bool showInCanvas = false;
            bool keepOpen = false;
            bool bakeToViewport = true;

            if (!da.GetData("Folder", ref folder)) return;
            da.GetData("Port", ref port);
            da.GetData("Active", ref active);
            da.GetData("ShowInCanvas", ref showInCanvas);
            da.GetData("KeepOpen", ref keepOpen);
            da.GetData("BakeToViewport", ref bakeToViewport);

            if (!active)
            {
                StopServer();
                da.SetData("Status", "Server stopped");
                da.SetData("Tools", 0);
                da.SetData("URL", "");
                return;
            }

            if (!Directory.Exists(folder))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Folder not found: {folder}");
                da.SetData("Status", $"Error: folder not found — {folder}");
                da.SetData("Tools", 0);
                da.SetData("URL", "");
                return;
            }

            // Restart server if folder or port changed
            bool needsRestart = _server is null
                || !_server.IsRunning
                || folder != _lastFolder
                || port != _lastPort;

            if (needsRestart)
            {
                StopServer();
                try
                {
                    var registry = new ToolRegistry(folder);
                    var runner = new GHScriptRunner
                    {
                        ShowInCanvas = showInCanvas,
                        KeepOpen = keepOpen,
                        BakeToViewport = bakeToViewport,
                    };
                    _server = new HttpMCPServer(registry, runner, port);
                    _server.Start();

                    _lastFolder = folder;
                    _lastPort = port;

                    da.SetData("Status", $"Running — {registry.Tools.Count} tool(s) loaded");
                    da.SetData("Tools", registry.Tools.Count);
                    da.SetData("URL", _server.BaseUrl);
                }
                catch (Exception ex)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                    da.SetData("Status", $"Error: {ex.Message}");
                    da.SetData("Tools", 0);
                    da.SetData("URL", "");
                }
            }
            else
            {
                da.SetData("Status", $"Running on port {port}");
                da.SetData("Tools", new ToolRegistry(folder).Tools.Count);
                da.SetData("URL", _server?.BaseUrl ?? "");
            }
        }

        // ── Cleanup ───────────────────────────────────────────────────────────
        private void StopServer()
        {
            _server?.Stop();
            _server?.Dispose();
            _server = null;
        }

        public override void RemovedFromDocument(GH_Document document)
        {
            StopServer();
            base.RemovedFromDocument(document);
        }
    }
}
