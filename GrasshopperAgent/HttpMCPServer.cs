using GrasshopperAgent.Protocol;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using static GrasshopperAgent.Protocol.ArgumentHelpers;

namespace GrasshopperAgent
{
    /// <summary>
    /// Lightweight HTTP server that exposes loaded GH tools over the MCP protocol.
    /// Runs on a background thread; started/stopped by the GH component.
    /// </summary>
    public class HttpMCPServer : IDisposable
    {
        private readonly ToolRegistry _registry;
        private readonly NativeTools.NativeToolRegistry _native;
        private readonly GHScriptRunner _runner;
        private readonly int _port;
        private HttpListener? _listener;
        private CancellationTokenSource? _cts;
        private Task? _serverTask;

        private static readonly JsonSerializerOptions _json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };

        public bool IsRunning => _listener?.IsListening == true;
        public string BaseUrl => $"http://localhost:{_port}";

        public HttpMCPServer(ToolRegistry registry, GHScriptRunner runner, int port)
        {
            _registry = registry;
            _native  = new NativeTools.NativeToolRegistry();
            _runner  = runner;
            _port    = port;
        }

        public void Start()
        {
            if (IsRunning) return;
            _cts = new CancellationTokenSource();
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{_port}/");
            _listener.Start();
            _serverTask = Task.Run(() => Loop(_cts.Token));
        }

        public void Stop()
        {
            _cts?.Cancel();
            try { _listener?.Stop(); } catch { }
            try { _serverTask?.Wait(2000); } catch { }
        }

        private async Task Loop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _listener?.IsListening == true)
            {
                HttpListenerContext ctx;
                try { ctx = await _listener.GetContextAsync(); }
                catch { break; }
                _ = Task.Run(() => HandleAsync(ctx), ct);
            }
        }

        private async Task HandleAsync(HttpListenerContext ctx)
        {
            var req = ctx.Request;
            var resp = ctx.Response;

            // CORS — allow the Python agent (any localhost origin)
            resp.AddHeader("Access-Control-Allow-Origin", "*");
            resp.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            resp.AddHeader("Access-Control-Allow-Headers", "Content-Type");

            if (req.HttpMethod == "OPTIONS")
            {
                resp.StatusCode = 204;
                resp.Close();
                return;
            }

            var path = req.Url?.AbsolutePath.TrimEnd('/').ToLower() ?? "/";

            try
            {
                var body = await new System.IO.StreamReader(req.InputStream).ReadToEndAsync();

                if (path == "/api/health" && req.HttpMethod == "GET")
                {
                    await WriteJson(resp, new HealthResponse("ok", _registry.Tools.Count));
                }
                else if (path == "/api/list_tools")
                {
                    // Native (built-in C#) tools first, then .gh-file tools
                    var all = _native.ToMCPDefinitions();
                    all.AddRange(_registry.ToMCPDefinitions());
                    await WriteJson(resp, new ListToolsResponse(all));
                }
                else if (path == "/api/call_tool" && req.HttpMethod == "POST")
                {
                    await HandleCallTool(body, resp);
                }
                else
                {
                    resp.StatusCode = 404;
                    await WriteJson(resp, new { error = "Not found" });
                }
            }
            catch (Exception ex)
            {
                resp.StatusCode = 500;
                await WriteJson(resp, new { error = ex.Message });
            }
        }

        private async Task HandleCallTool(string body, HttpListenerResponse resp)
        {
            CallToolRequest? request;
            try { request = JsonSerializer.Deserialize<CallToolRequest>(body, _json); }
            catch
            {
                resp.StatusCode = 400;
                await WriteJson(resp, new CallToolResponse(null, "Invalid JSON body"));
                return;
            }

            if (request is null || string.IsNullOrEmpty(request.Name))
            {
                resp.StatusCode = 400;
                await WriteJson(resp, new CallToolResponse(null, "Missing 'name' field"));
                return;
            }

            // ── Try built-in C# tools first ───────────────────────────────────
            var nativeTool = _native.FindByName(request.Name);
            if (nativeTool is not null)
            {
                try
                {
                    var result = nativeTool.Execute(ArgumentHelpers.Normalize(request.Arguments));
                    await WriteJson(resp, new CallToolResponse(result, null));
                }
                catch (Exception ex)
                {
                    resp.StatusCode = 500;
                    await WriteJson(resp, new CallToolResponse(null, ex.Message));
                }
                return;
            }

            // ── Fall through to .gh-file tools ────────────────────────────────
            var toolDef = _registry.FindByName(request.Name);
            if (toolDef is null)
            {
                resp.StatusCode = 404;
                await WriteJson(resp, new CallToolResponse(null, $"Tool '{request.Name}' not found"));
                return;
            }

            try
            {
                var inputs = ArgumentHelpers.Normalize(request.Arguments);
                var outputNames = toolDef.Outputs.Select(o => o.Name);
                var results = _runner.Execute(toolDef.FilePath, inputs, outputNames, toolDef.ToolGroupName);
                var summary = results.Count > 0
                    ? string.Join("\n", results.Select(kv => $"{kv.Key}: {kv.Value}"))
                    : "(no output)";
                await WriteJson(resp, new CallToolResponse(summary, null));
            }
            catch (Exception ex)
            {
                resp.StatusCode = 500;
                await WriteJson(resp, new CallToolResponse(null, ex.Message));
            }
        }

        private static async Task WriteJson(HttpListenerResponse resp, object data)
        {
            var json = JsonSerializer.Serialize(data, _json);
            var bytes = Encoding.UTF8.GetBytes(json);
            resp.ContentType = "application/json; charset=utf-8";
            resp.ContentLength64 = bytes.Length;
            await resp.OutputStream.WriteAsync(bytes);
            resp.Close();
        }

        public void Dispose() => Stop();
    }
}
