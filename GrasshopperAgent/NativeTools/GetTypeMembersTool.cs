using GrasshopperAgent.Protocol;
using Rhino;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace GrasshopperAgent.NativeTools
{
    /// <summary>Returns public constructors, properties, and methods of a RhinoCommon type via reflection.</summary>
    public sealed class GetTypeMembersTool : INativeTool
    {
        public ToolDefinition Definition { get; } = new ToolDefinition(
            Name: "get_type_members",
            Description:
                "Returns the public constructors, properties, and methods of a .NET type from " +
                "RhinoCommon or Grasshopper, discovered via reflection.\n" +
                "\n" +
                "Use this to learn the exact parameter types for a constructor or method " +
                "before writing code in run_csharp_script. " +
                "Accepts short names ('Box', 'Sphere') or fully-qualified names " +
                "('Rhino.Geometry.Box'). Auto-tries common RhinoCommon namespace prefixes.",
            InputSchema: new InputSchema(
                Type: "object",
                Properties: new Dictionary<string, PropertySchema>
                {
                    ["type_name"] = new("string",
                        "Type to inspect, e.g. 'Box', 'NurbsCurve', 'Rhino.Geometry.Brep', " +
                        "'Grasshopper.Kernel.GH_Component'."),
                },
                Required: new[] { "type_name" }
            ),
            Categories: new[] { "api", "discovery", "reflection" },
            Outputs: new Dictionary<string, string>
            {
                ["full_name"]    = "Fully qualified type name",
                ["kind"]         = "class / abstract / interface / enum",
                ["base_type"]    = "Base class full name",
                ["constructors"] = "Constructor signatures",
                ["properties"]   = "Public property signatures (declared on this type)",
                ["methods"]      = "Public method signatures (declared on this type)",
                ["enum_values"]  = "Enum member names (enum types only)",
            }
        );

        public string Execute(Dictionary<string, string> args)
        {
            if (!args.TryGetValue("type_name", out var typeName) || string.IsNullOrWhiteSpace(typeName))
                throw new ArgumentException("'type_name' is required.");

            typeName = typeName.Trim().TrimStart('!', '_', '-').Trim();

            var assemblies = new List<Assembly> { typeof(RhinoDoc).Assembly };
            try { assemblies.Add(typeof(Grasshopper.Instances).Assembly); } catch { }

            // Try in resolution order: exact, then common namespace prefixes
            var prefixes = new[]
            {
                "", "Rhino.Geometry.", "Rhino.", "Rhino.DocObjects.", "Rhino.Display.",
                "Grasshopper.Kernel.", "Grasshopper.Kernel.Special.", "Grasshopper.Kernel.Types.",
            };

            Type? t = null;
            foreach (var pfx in prefixes)
            {
                foreach (var asm in assemblies)
                {
                    t = asm.GetType(pfx + typeName, throwOnError: false, ignoreCase: true);
                    if (t != null) break;
                }
                if (t != null) break;
            }

            if (t is null)
                return JsonSerializer.Serialize(new
                {
                    error = $"Type '{typeName}' not found in RhinoCommon or Grasshopper assemblies. " +
                            "Use list_rhinocommon_types with a filter to discover the correct name.",
                });

            var flags  = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            var ctors  = t.GetConstructors().Select(FormatSig).OrderBy(s => s).ToList();
            var props  = t.GetProperties(flags)
                          .Select(p =>
                          {
                              var rw   = (p.CanRead ? "get; " : "") + (p.CanWrite ? "set;" : "");
                              var stat = p.GetAccessors(false).Any(a => a.IsStatic) ? "static " : "";
                              return $"{stat}{FormatType(p.PropertyType)} {p.Name} {{ {rw.Trim()} }}";
                          })
                          .OrderBy(s => s).ToList();
            var methods = t.GetMethods(flags)
                           .Where(m => !m.IsSpecialName)
                           .Select(FormatSig)
                           .OrderBy(s => s).ToList();
            var enumVals = t.IsEnum ? Enum.GetNames(t).ToList() : new List<string>();

            return JsonSerializer.Serialize(new
            {
                full_name    = t.FullName,
                @namespace   = t.Namespace,
                kind         = t.IsEnum              ? "enum"
                             : t.IsInterface         ? "interface"
                             : t.IsAbstract && !t.IsSealed ? "abstract"
                             : "class",
                base_type    = t.BaseType?.FullName,
                constructors = ctors,
                properties   = props,
                methods,
                enum_values  = enumVals,
            });
        }

        private static string FormatType(Type t) =>
            !t.IsGenericType
                ? t.Name
                : $"{t.Name.Split('`')[0]}<{string.Join(", ", t.GetGenericArguments().Select(FormatType))}>";

        private static string FormatSig(MethodBase m)
        {
            var ps = string.Join(", ",
                m.GetParameters().Select(p => $"{FormatType(p.ParameterType)} {p.Name}"));
            if (m is MethodInfo mi)
                return $"{(mi.IsStatic ? "static " : "")}{FormatType(mi.ReturnType)} {mi.Name}({ps})";
            return $"new({ps})";
        }
    }
}
