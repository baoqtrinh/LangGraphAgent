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
- Supports **live C# scripting** via `run_csharp_script` (Roslyn) — lets the agent write and execute arbitrary RhinoCommon code at runtime.
- Optionally bakes resulting geometry into the Rhino viewport; returns baked object GUIDs so the agent can reference them in follow-up calls.
- Supports **multi-tool per file** (multiple `TOOL_` groups in a single `.gh` file).

**MCP Endpoints (default port `5100`):**

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/health` | Liveness check |
| `GET/POST` | `/api/list_tools` | Returns all discovered tool schemas |
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

**Tech stack:** C# · .NET 7.0-windows · Grasshopper SDK 8 · Roslyn (`Microsoft.CodeAnalysis.CSharp.Scripting`) · `System.Net.HttpListener` · `System.Text.Json`

---

### AgentApp — Python LangGraph Agent

A conversational AI agent for architectural design assistance. It connects to the GrasshopperAgent MCP server at startup to discover all available tools, then routes user messages through a multi-branch LangGraph state machine.

**Intent routing:**

```
classify_input
 ├─ plan             → planner → execute_plan_step (loop) → plan_summary → END
 ├─ use_tool         → execute_gh_tool → END
 ├─ design_building  → retrieve_rules → thinking → execute_action
 │                     → draw_box → compliance_check
 │                          ├─ compliant     → END
 │                          └─ not_compliant → thinking  (ReAct loop)
 ├─ show_guide       → return design guide → END
 ├─ general_question → determine_search_need
 │                          ├─ needs_search  → web_search → answer → END
 │                          └─ no_search     → answer → END
 └─ unknown          → handle_unknown → END
```

**Key features:**
- Dynamically discovers MCP tools at startup and wraps each as a LangChain `BaseTool` (`DynamicMCPTool`).
- **Plan mode** — decomposes multi-step requests into an ordered tool-call sequence; each step's result feeds into the next.
- ReAct loop for iterative building design: adjusts dimensions until compliance constraints are satisfied.
- Vision support — viewport captures are automatically forwarded to the VLM for scene reasoning.
- Optional Tavily web search for general architectural Q&A.
- `MemorySaver` checkpointer — full conversation state persists across turns on the same thread.
- Exposed as a FastAPI REST endpoint (`POST /chat`).

**Tech stack:** Python 3.11 · LangGraph ≥ 0.2 · LangChain · Google Gemini (`gemini-2.5-flash-lite`) · Tavily · FastAPI · Uvicorn · Pydantic

---

## AgentApp Structure

```
AgentApp/
├── settings.py              # Non-secret runtime config (LLM model, endpoints)
├── .env.local               # Secret keys — GOOGLE_API_KEY, TAVILY_API_KEY
├── app.py                   # FastAPI entry point  (POST /chat)
├── run_agent.py             # CLI entry point
│
├── app/
│   └── config.py            # Loads settings.py + .env.local
│
├── config/
│   ├── design_rules.py      # Building compliance rules
│   └── prompts.py           # Shared system-prompt fragments (C# scripting context)
│
├── graphs/
│   └── main_graph.py        # LangGraph StateGraph definition
│
├── models/
│   └── state.py             # BoxState — Pydantic model for graph state
│
├── nodes/                   # Graph nodes, organised by branch
│   ├── classification.py    # Entry node — routes to a branch
│   ├── building_design/     # design_building branch (ReAct loop)
│   ├── information/         # show_guide + handle_unknown branches
│   ├── search/              # general_question branch (± web search)
│   ├── tool_use/            # use_tool branch (single MCP tool call)
│   └── planning/            # plan branch (multi-tool chaining)
│
├── tools/                   # Python-side tool wrappers
│   ├── base.py              # BaseAgentTool — subclass this to add a new tool
│   ├── mcp/                 # DynamicMCPTool — auto-loaded from GH server
│   ├── search/              # WebSearchTool (Tavily)
│   └── building/            # Static building-calculation helpers
│
├── utils/
│   └── llm_utils.py         # LLM factory + reason_about_image() VLM helper
│
└── notebooks/
    ├── tool_test.ipynb       # Direct MCP tool tests (no agent)
    └── agent_system_tests.ipynb  # Full LangGraph agent tests
```

---

## Getting Started

### Prerequisites

- **Rhino 8** with Grasshopper
- **.NET 7.0 SDK** (for building the C# plugin)
- **Python 3.11** (conda env recommended)
- A **Google Gemini API key** — or a local OpenAI-compatible LLM endpoint

### 1 — Build and install the Grasshopper plugin

```bash
cd GrasshopperAgent
dotnet build -c Debug
```

Copy `bin/Debug/net7.0-windows/GrasshopperAgent.gha` to your Rhino plugins folder:
```
%APPDATA%\Grasshopper\Libraries\
```

Restart Rhino. The **GH Tool Server** component will appear under **MCP › Server**.

### 2 — Set up the Python agent

```bash
cd AgentApp
pip install -r requirements.txt
```

Edit `settings.py` to select your LLM provider and model:

```python
LLM_PROVIDER = "gemini"          # or "local" for an OpenAI-compatible endpoint
GEMINI_MODEL = "gemini-2.5-flash-lite"
MCP_GH_ENDPOINT = "http://localhost:5100"
```

Create `.env.local` in `AgentApp/` with your secret keys:

```env
GOOGLE_API_KEY=AIza...
TAVILY_API_KEY=tvly-...          # optional — only needed for web search
```

### 3 — Start the agent

```bash
cd AgentApp
python app.py
```

The FastAPI server starts on `http://localhost:8000`. Send requests to `POST /chat`:

```json
{
  "message": "Create a sphere at origin with radius 5 and capture the viewport"
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
│  │  .gh tools + run_csharp_script (Roslyn)   │ │ │
│  │  Bakes geometry · Captures viewport       │ │ │
│  └───────────────────────────────────────────┘ │ │
└──────────────────────────┬───────────────────────┘
                           │ HTTP (localhost:5100)
┌──────────────────────────┴───────────────────────┐
│           Python AI Agent  (AgentApp/)            │
│                                                   │
│  LangGraph StateGraph + MemorySaver               │
│   classify → plan / use_tool / design / search    │
│   DynamicMCPTool — auto-loaded from GH server     │
│   VLM viewport reasoning · Tavily web search      │
│   FastAPI  POST /chat                             │
└──────────────────────────────────────────────────┘
```

---

## Configuration Reference

| Setting | Location | Default | Description |
|---------|----------|---------|-------------|
| `LLM_PROVIDER` | `settings.py` | `"gemini"` | `"gemini"` or `"local"` |
| `GEMINI_MODEL` | `settings.py` | `"gemini-2.5-flash-lite"` | Gemini model name |
| `LLM_ENDPOINT` | `settings.py` | `http://localhost:1234/v1/...` | Local LLM URL |
| `MCP_GH_ENDPOINT` | `settings.py` | `http://localhost:5100` | GrasshopperAgent URL |
| `MCP_TIMEOUT` | `settings.py` | `30` | Request timeout (seconds) |
| `GOOGLE_API_KEY` | `.env.local` | — | Gemini API key |
| `TAVILY_API_KEY` | `.env.local` | — | Tavily search key (optional) |

---

## Authoring Grasshopper Tools

Each `.gh` file in the tools folder becomes an MCP tool. To define a tool:

1. Create a canvas **Group** labelled `INPUT` — add sliders/panels inside for each input parameter.
2. Create a canvas **Group** labelled `OUTPUT` — add panels inside for each output.
3. Add a **Panel** outside the groups named `DESCRIPTION` containing a plain-text description of what the tool does.
4. *(Optional)* For multi-tool files, use groups labelled `TOOL_MyToolName` instead of `INPUT`/`OUTPUT`.

The `ToolRegistry` reads these groups at server start-up and generates JSON schemas automatically.

## Authoring Python Tools

To add a new Python-side tool, subclass `BaseAgentTool` in the appropriate `tools/<category>/tools.py`:

```python
from tools.base import BaseAgentTool

class MyTool(BaseAgentTool):
    name: str = "my_tool"
    description: str = "Does something useful."
    categories: list = ["tool_use"]   # graph branch(es) that use this tool

    def _run(self, param: str) -> str:
        return f"result for {param}"
```

Then re-export it from the sub-package's `__init__.py`.

---

## License

MIT
