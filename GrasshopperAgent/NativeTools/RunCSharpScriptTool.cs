using GrasshopperAgent.Protocol;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Rhino;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace GrasshopperAgent.NativeTools
{
    /// <summary>
    /// Global variables injected into every <c>run_csharp_script</c> execution.
    /// <para><c>Doc</c> is pre-set to <see cref="RhinoDoc.ActiveDoc"/> before the script runs.</para>
    /// </summary>
    public sealed class CSharpScriptGlobals
    {
        public RhinoDoc Doc { get; init; } = null!;
    }

    /// <summary>Executes arbitrary C# code inside Rhino's process via Roslyn scripting.</summary>
    public sealed class RunCSharpScriptTool : INativeTool
    {
        public ToolDefinition Definition { get; } = new ToolDefinition(
            Name: "run_csharp_script",
            Description:
                "Executes arbitrary C# code using the Roslyn scripting engine directly " +
                "inside Rhino's process. This gives unlimited access to the full RhinoCommon " +
                "and Grasshopper APIs — any operation that can be done in C# can be done here.\n" +
                "\n" +
                "Pre-defined global variable:\n" +
                "  RhinoDoc Doc   — the active document (same as RhinoDoc.ActiveDoc)\n" +
                "\n" +
                "Auto-imported namespaces:\n" +
                "  Rhino, Rhino.Geometry, Rhino.DocObjects, Rhino.Display\n" +
                "  System, System.Linq, System.Collections.Generic, System.Text.Json\n" +
                "\n" +
                "Returning values:\n" +
                "  The last EXPRESSION in the script is the return value (JSON-serialized).\n" +
                "  • End with `id` (a Guid) → returns the GUID string\n" +
                "  • End with `new { id = id.ToString(), ok = true }` → returns that object\n" +
                "  • `Console.WriteLine(...)` output is also captured\n" +
                "\n" +
                "Recommended workflow:\n" +
                "  1. Call list_rhinocommon_types to find the right type\n" +
                "  2. Call get_type_members to read the constructor signature\n" +
                "  3. Call run_csharp_script with correct code\n" +
                "\n" +
                "Example script:\n" +
                "  var sphere = new Sphere(new Point3d(0,0,0), 10);\n" +
                "  var id = Doc.Objects.AddSphere(sphere);\n" +
                "  Doc.Views.Redraw();\n" +
                "  id   // ← last expression is returned",
            InputSchema: new InputSchema(
                Type: "object",
                Properties: new Dictionary<string, PropertySchema>
                {
                    ["code"] = new("string",
                        "The C# script body. Multi-line supported. " +
                        "End with an expression to get a return value."),
                },
                Required: new[] { "code" }
            ),
            Categories: new[] { "scripting", "rhinocommon", "advanced" },
            Outputs: new Dictionary<string, string>
            {
                ["success"]            = "true if the script ran without errors",
                ["return_value"]       = "JSON-serialized last expression value (if any)",
                ["console_output"]     = "Anything written to Console.Write / Console.WriteLine",
                ["error"]              = "Runtime exception message (if the script threw)",
                ["compilation_errors"] = "Roslyn compiler diagnostics (if compilation failed)",
            }
        );

        public string Execute(Dictionary<string, string> args)
        {
            if (!args.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
                throw new ArgumentException("'code' is required.");

            var doc = RhinoDoc.ActiveDoc
                ?? throw new InvalidOperationException("No active Rhino document.");

            // Build references from currently-loaded assemblies to avoid
            // cross-AssemblyLoadContext issues (no disk paths needed).
            var refs = new List<Assembly> { typeof(RhinoDoc).Assembly };
            try { refs.Add(typeof(Grasshopper.Instances).Assembly); } catch { }
            foreach (var name in new[]
            {
                "System.Runtime", "System.Private.CoreLib",
                "System.Linq", "System.Collections",
                "System.Text.Json", "System.Console",
            })
            {
                var asm = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == name);
                if (asm is not null) refs.Add(asm);
            }

            var opts = ScriptOptions.Default
                .WithReferences(refs)
                .WithImports(
                    "Rhino", "Rhino.Geometry", "Rhino.DocObjects", "Rhino.Display",
                    "System", "System.Linq", "System.Collections.Generic", "System.Text.Json");

            var globals = new CSharpScriptGlobals { Doc = doc };

            // Capture Console output (process-wide — not safe for concurrent calls)
            var consoleSb = new StringBuilder();
            var prevOut   = Console.Out;
            Console.SetOut(new StringWriter(consoleSb));

            ScriptState<object>? state  = null;
            Exception?           runErr = null;
            try
            {
                // Task.Run avoids sync-context deadlocks on UI threads
                state = Task.Run(async () =>
                    await CSharpScript.RunAsync<object>(
                        code, opts, globals, typeof(CSharpScriptGlobals))
                ).GetAwaiter().GetResult();
            }
            catch (Exception ex) { runErr = ex; }
            finally { Console.SetOut(prevOut); }

            doc.Views.Redraw();

            var consoleOut = consoleSb.ToString();

            if (runErr is CompilationErrorException cex)
                return JsonSerializer.Serialize(new
                {
                    success            = false,
                    compilation_errors = cex.Diagnostics.Select(d => d.ToString()).ToList(),
                    console_output     = consoleOut,
                    return_value       = (string?)null,
                    error              = (string?)null,
                });

            if (runErr is not null)
                return JsonSerializer.Serialize(new
                {
                    success            = false,
                    error              = runErr.Message,
                    console_output     = consoleOut,
                    return_value       = (string?)null,
                    compilation_errors = (List<string>?)null,
                });

            return JsonSerializer.Serialize(new
            {
                success            = true,
                return_value       = ScriptSerialize(state?.ReturnValue),
                console_output     = consoleOut,
                error              = (string?)null,
                compilation_errors = (List<string>?)null,
            });
        }

        private static string? ScriptSerialize(object? value)
        {
            if (value is null) return null;
            try
            {
                var json = JsonSerializer.Serialize(value, new JsonSerializerOptions
                {
                    MaxDepth = 5,
                    ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,
                });
                return json.Length > 6000 ? json[..6000] + "…(truncated)" : json;
            }
            catch { return value.ToString(); }
        }
    }
}
