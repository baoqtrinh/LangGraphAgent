# LangGraphAgent

An AI-driven architectural design assistant that bridges **LangGraph-powered Python agents** with **Rhino/Grasshopper** via a lightweight HTTP MCP (Model Context Protocol) server. Users interact with the agent through natural language to run Grasshopper scripts, generate building geometry, check design compliance, and query web resources — all without leaving Rhino.

---

## Repository Structure

```
LangGraphAgent/
├── AgentApp/           # Python LangGraph agent (AI brain)
├── GrasshopperAgent/   # C# Grasshopper plugin (MCP server)
├── GHTools/            # Sample Grasshopper tool files (.gh)
└── Test/               # Scratch / test files
```

---

## Projects

### GrasshopperAgent — C# Grasshopper Plugin

A Rhino 8 / Grasshopper plugin (`.gha`) that turns a folder of `.gh` script files into an HTTP MCP server, making them callable by the Python agent over a simple REST API.

**Key features:**
- `GH Tool Server` component — drop it on the canvas, point it at a folder, set `Active = True`.
- Scans `.gh` files and extracts tool metadata (name, description, parameter specs) from embedded canvas groups (`INPUT`, `OUTPUT`, `TOOL_*`, `DESCRIPTION`).
- Runs scripts headlessly via `GHScriptRunner` — injects input values, executes the Grasshopper solution, and reads output panels.
- Optionally bakes resulting geometry into the Rhino viewport; returns baked object GUIDs so the agent can reference them in follow-up calls.
- Supports **multi-tool per file** (multiple `TOOL_` groups in a single `.gh` file).

**MCP Endpoints (default port `5100`):**

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/health` | Liveness check |
| `GET` | `/api/list_tools` | Returns all discovered tool schemas |
| `POST` | `/api/call_tool` | Executes a tool by name with JSON arguments |

**Component inputs:**

| Parameter | Default | Description |
|-----------|---------|-------------|
| `Folder` | — | Path to folder containing `.gh` tool files |
| `Port` | `5100` | HTTP port for the MCP server |
| `Active` | `false` | Start / stop the server |
| `ShowInCanvas` | `true` | Run scripts in the active canvas (vs. background) |
| `KeepOpen` | `false` | Keep Grasshopper document open after execution |
| `BakeToViewport` | `false` | Bake geometry outputs to the Rhino viewport |

**Tech stack:** C# · .NET 7.0-windows · Grasshopper SDK 8 · `System.Net.HttpListener` · `System.Text.Json`

---

### AgentApp — Python LangGraph Agent

A conversational AI agent for architectural design assistance. It connects to the GrasshopperAgent MCP server at startup to discover all available tools, then routes user messages through a multi-branch LangGraph state machine.

**Intent routing:**

```
classify_input
 ├─ use_tool          → execute Grasshopper script via MCP → END
 ├─ design_building   → retrieve_rules → thinking → execute_action
 │                      → draw_box → compliance_check
 │                           ├─ compliant   → END
 │                           └─ not_compliant → thinking  (ReAct loop)
 ├─ show_guide        → return design guide → END
 ├─ general_question  → determine_search_need
 │                           ├─ needs_search  → web_search → answer → END
 │                           └─ no_search     → answer → END
 └─ unknown           → handle_unknown → END
```

**Key features:**
- Dynamically discovers MCP tools at startup (`GET /api/list_tools`) and wraps each as a LangChain `BaseTool`.
- ReAct loop for iterative building design: adjusts dimensions until compliance constraints are satisfied (depth ≤ 50 m, aspect ratio ≥ 0.33, emergency exit count).
- Optional Tavily web search for general architectural Q&A.
- Exposed as a FastAPI REST endpoint (`POST /chat`).

**Built-in building tools:** `compute_other_dimension`, `calculate_aspect_ratio`, `calculate_total_height`, `calculate_window_area`

**Tech stack:** Python · LangGraph ≥ 0.2 · LangChain · OpenAI · Tavily · FastAPI · Uvicorn · Pydantic

---

## Getting Started

### Prerequisites

- **Rhino 8** with Grasshopper
- **.NET 7.0 SDK** (for building the C# plugin)
- **Python 3.10+**
- An **OpenAI API key** (or compatible LLM endpoint)

### 1 — Build and install the Grasshopper plugin

```bash
cd GrasshopperAgent
dotnet build -c Debug
```

Copy `bin/Debug/GrasshopperAgent.gha` to your Rhino plugins folder:
```
%APPDATA%\Grasshopper\Libraries\
```

Restart Rhino. The **GH Tool Server** component will appear under **MCP › Server**.

### 2 — Set up the Python agent

```bash
cd AgentApp
python -m venv .venv
.venv\Scripts\activate      # Windows
pip install -r requirements.txt
```

Create a `.env` file in `AgentApp/`:

```env
OPENAI_API_KEY=sk-...
MCP_GH_ENDPOINT=http://localhost:5100   # matches the port set in the GH component
TAVILY_API_KEY=tvly-...                 # optional, for web search
```

### 3 — Start the agent

```bash
cd AgentApp
python app.py
```

The FastAPI server starts on `http://localhost:8000`. Send requests to `POST /chat`:

```json
{
  "message": "Create a 30m × 20m rectangular building footprint"
}
```

### 4 — Connect Grasshopper

1. Open Rhino and Grasshopper.
2. Drop the **GH Tool Server** component onto the canvas.
3. Set `Folder` to your `.gh` tools directory (e.g., `GHTools/`).
4. Set `Port` to `5100` and toggle `Active` to `True`.
5. The Python agent will automatically discover and register all tools on the next call.

---

## Architecture Overview

```
┌──────────────────────────────────────────────────┐
│           Rhino + Grasshopper (Desktop)           │
│                                                   │
│  ┌─────────────────────────────────────────────┐ │
│  │  GrasshopperAgent.gha  (port 5100)          │ │
│  │                                             │ │
│  │  GET  /api/list_tools  ◄──────────────────┐ │ │
│  │  POST /api/call_tool   ◄──────────────────┤ │ │
│  │                                           │ │ │
│  │  Scans .gh files → runs GHScriptRunner    │ │ │
│  │  Bakes geometry to Rhino viewport         │ │ │
│  └───────────────────────────────────────────┘ │ │
└──────────────────────────┬───────────────────────┘
                           │ HTTP (localhost:5100)
┌──────────────────────────┴───────────────────────┐
│           Python AI Agent  (AgentApp/)            │
│                                                   │
│  LangGraph StateGraph                             │
│   classify → route → ReAct loop                  │
│   Dynamic MCP tool wrapping                       │
│   Web search (Tavily)                             │
│   FastAPI  POST /chat                             │
└──────────────────────────────────────────────────┘
```

---

## Configuration Reference

| Variable | Default | Description |
|----------|---------|-------------|
| `MCP_GH_ENDPOINT` | `http://localhost:5100` | GrasshopperAgent MCP server URL |
| `OPENAI_API_KEY` | — | OpenAI (or compatible) API key |
| `TAVILY_API_KEY` | — | Tavily search API key (optional) |

---

## Authoring Grasshopper Tools

Each `.gh` file in the tools folder becomes an MCP tool. To define a tool:

1. Create a canvas **Group** labelled `INPUT` — add sliders/panels inside for each input parameter.
2. Create a canvas **Group** labelled `OUTPUT` — add panels inside for each output.
3. Add a **Panel** outside the groups named `DESCRIPTION` containing a plain-text description of what the tool does.
4. *(Optional)* For multi-tool files, use groups labelled `TOOL_MyToolName` instead of `INPUT`/`OUTPUT`.

The `ToolRegistry` reads these groups at server start-up and generates JSON schemas automatically.

---

## License

MIT
