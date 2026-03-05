using Grasshopper;
using Grasshopper.Kernel;
using System;
using System.Drawing;

namespace GrasshopperAgent
{
    public class GrasshopperAgentInfo : GH_AssemblyInfo
    {
        public override string Name => "GrasshopperAgent";

        public override Bitmap Icon => null;

        public override string Description =>
            "Exposes .gh script files as MCP-compatible tools so LangGraph agents can call them.";

        public override Guid Id => new Guid("beb97388-c33e-44f1-be74-3e427f63fc37");

        public override string AuthorName => "Design Tech Course";

        public override string AuthorContact => "";

        public override string AssemblyVersion => GetType().Assembly.GetName().Version.ToString();
    }
}
