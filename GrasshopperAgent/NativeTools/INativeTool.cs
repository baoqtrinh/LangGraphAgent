using GrasshopperAgent.Protocol;
using System.Collections.Generic;

namespace GrasshopperAgent.NativeTools
{
    /// <summary>
    /// Contract for all built-in C# tools exposed through the MCP server.
    ///
    /// Adding a new built-in tool
    /// ──────────────────────────
    ///   1. Create a class implementing <see cref="INativeTool"/> in the NativeTools/ folder.
    ///   2. Call Register(new MyTool()) inside <see cref="NativeToolRegistry"/>'s constructor.
    ///   The tool will automatically appear in /api/list_tools and be callable
    ///   via /api/call_tool just like any .gh-based tool.
    /// </summary>
    public interface INativeTool
    {
        /// <summary>MCP tool definition (name, description, schema) sent to the Python agent.</summary>
        ToolDefinition Definition { get; }

        /// <summary>Execute the tool with the given normalised string arguments.</summary>
        string Execute(Dictionary<string, string> args);
    }
}
