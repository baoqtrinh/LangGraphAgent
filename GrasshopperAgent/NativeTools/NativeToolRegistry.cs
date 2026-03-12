using GrasshopperAgent.Protocol;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GrasshopperAgent.NativeTools
{
    /// <summary>
    /// Maintains the list of all built-in C# tools and routes call_tool requests to them.
    /// </summary>
    public class NativeToolRegistry
    {
        private readonly List<INativeTool> _tools = new();

        public IReadOnlyList<INativeTool> Tools => _tools;

        public NativeToolRegistry()
        {
            Register(new CaptureViewportTool());
            Register(new GetSceneInfoTool());
            Register(new GetSelectedGeometryTool());
            Register(new BakeGhGeometryTool());
            // API discovery + unlimited scripting
            Register(new ListRhinoCommonTypesTool());
            Register(new GetTypeMembersTool());
            Register(new RunCSharpScriptTool());
        }

        private void Register(INativeTool tool) => _tools.Add(tool);

        public INativeTool? FindByName(string name) =>
            _tools.FirstOrDefault(t =>
                string.Equals(t.Definition.Name, name, StringComparison.OrdinalIgnoreCase));

        public List<ToolDefinition> ToMCPDefinitions() =>
            _tools.Select(t => t.Definition).ToList();
    }
}
