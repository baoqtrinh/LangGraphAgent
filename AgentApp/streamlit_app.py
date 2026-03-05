"""
Design Agent — Streamlit UI
Run with:  streamlit run streamlit_app.py  (from the AgentApp/ directory)
"""
import sys
import os

# Ensure AgentApp/ is on the path so all relative imports work
sys.path.insert(0, os.path.dirname(__file__))

import streamlit as st
import requests

# ── Page config (must be first Streamlit call) ────────────────────────────────
st.set_page_config(
    page_title="Design Agent",
    page_icon="🏗",
    layout="wide",
    initial_sidebar_state="expanded",
)

# ── Session state boot ────────────────────────────────────────────────────────
if "messages" not in st.session_state:
    st.session_state.messages = []          # chat history [{role, content, history}]
if "gh_tools" not in st.session_state:
    st.session_state.gh_tools = []          # loaded tool names
if "config_saved" not in st.session_state:
    st.session_state.config_saved = False


# ── Helper: apply sidebar config to env vars ─────────────────────────────────
def apply_config():
    os.environ["LLM_ENDPOINT"] = st.session_state.get("cfg_llm", "http://localhost:1234/v1/chat/completions")
    os.environ["LLM_MODEL"] = st.session_state.get("cfg_model", "") or ""
    os.environ["MCP_GH_ENDPOINT"] = st.session_state.get("cfg_mcp", "http://localhost:5100")
    os.environ["TAVILY_API_KEY"] = st.session_state.get("cfg_tavily", "") or ""


def load_gh_tools() -> list[str]:
    """Try to list tools from the GH MCP server. Returns list of tool names."""
    mcp = st.session_state.get("cfg_mcp", "http://localhost:5100")
    try:
        resp = requests.post(f"{mcp}/api/list_tools", json={}, timeout=3)
        if resp.status_code == 200:
            tools = resp.json().get("tools", [])
            return [t["name"] for t in tools]
    except Exception:
        pass
    return []


def build_graph():
    """Lazy-build the LangGraph (so config is applied first)."""
    apply_config()
    from graphs.main_graph import build_main_graph
    return build_main_graph()


# ── Sidebar ───────────────────────────────────────────────────────────────────
with st.sidebar:
    st.title("Design Agent")
    st.caption("LangGraph + Grasshopper MCP")

    st.subheader("Configuration")

    from dotenv import load_dotenv
    load_dotenv()

    llm_default = os.getenv("LLM_ENDPOINT", "http://localhost:1234/v1/chat/completions")
    mcp_default = os.getenv("MCP_GH_ENDPOINT", "http://localhost:5100")
    tavily_default = os.getenv("TAVILY_API_KEY", "")
    model_default = os.getenv("LLM_MODEL", "")

    st.text_input("LLM Endpoint", value=llm_default, key="cfg_llm",
                  help="OpenAI-compatible endpoint, e.g. LM Studio")
    st.text_input("LLM Model (optional)", value=model_default, key="cfg_model",
                  help="Leave blank to use the server's default model")
    st.text_input("GH MCP Server", value=mcp_default, key="cfg_mcp",
                  help="HTTP address of the Grasshopper plugin server")
    st.text_input("Tavily API Key", value=tavily_default, key="cfg_tavily",
                  type="password", help="For web search on general questions")

    st.divider()

    st.subheader("Grasshopper Tools")
    col1, col2 = st.columns([2, 1])
    with col1:
        if st.button("Reload Tools", use_container_width=True):
            apply_config()
            # Reload the module-level TOOL_CLASSES
            import importlib
            import tools.mcp_tool_loader as loader
            importlib.reload(loader)
            from tools.mcp_tool_loader import TOOL_CLASSES
            st.session_state.gh_tools = [t.name for t in TOOL_CLASSES]

    # Auto-load on first render
    if not st.session_state.config_saved:
        apply_config()
        st.session_state.gh_tools = load_gh_tools()
        st.session_state.config_saved = True

    if st.session_state.gh_tools:
        for name in st.session_state.gh_tools:
            st.markdown(f"- `{name}`")
    else:
        st.caption("No tools found. Start the Grasshopper plugin and click Reload.")

    st.divider()

    if st.button("Clear Chat", use_container_width=True):
        st.session_state.messages = []
        st.rerun()

    # Agent routing guide
    with st.expander("How it routes requests", expanded=False):
        st.markdown("""
| What you say | Route |
|---|---|
| *"Design a building with 800 sqm..."* | Building design loop |
| *"Show me the design guide"* | Design rules |
| *"What is passive solar design?"* | Web search Q&A |
| *"Run the sun analysis tool..."* | GH Tool execution |
        """)


# ── Main chat area ────────────────────────────────────────────────────────────
st.title("Design Agent")

# Render existing messages
for msg in st.session_state.messages:
    with st.chat_message(msg["role"]):
        st.markdown(msg["content"])
        if msg.get("history"):
            _hist = msg["history"]
            label = f"Agent trace ({len(_hist)} steps)"
            with st.expander(label, expanded=False):
                for step in _hist:
                    node = step.get("node", "?")
                    st.markdown(f"**`{node}`**")
                    for k, v in step.items():
                        if k != "node":
                            st.markdown(f"- **{k}**: `{v}`")
        if msg.get("tool_results"):
            with st.expander("Tool results", expanded=False):
                import json
                st.code(json.dumps(msg["tool_results"], indent=2), language="json")

# Chat input
if prompt := st.chat_input("Ask about building design, run a GH tool, or search..."):
    # Display user message
    with st.chat_message("user"):
        st.markdown(prompt)
    st.session_state.messages.append({"role": "user", "content": prompt})

    # Run the agent
    with st.chat_message("assistant"):
        with st.spinner("Thinking..."):
            try:
                apply_config()
                graph = build_graph()

                from models.state import BoxState
                state = BoxState(request={"user_input": prompt})
                result = graph.invoke(state, config={"recursion_limit": 50})

                req_type = result.get("request_type", "unknown")
                history = result.get("history", [])
                tool_results = result.get("tool_results")

                # Build response text
                if req_type == "design_building":
                    box = result.get("box", {})
                    compliant = result.get("compliant", False)
                    issues = result.get("issues", [])
                    lines = [
                        f"**Building Design Result** — {'Compliant' if compliant else 'Not compliant'}",
                        "",
                    ]
                    for k, v in (box or {}).items():
                        if v is not None:
                            val = f"{v:.2f}" if isinstance(v, float) else str(v)
                            lines.append(f"- **{k}**: {val}")
                    if issues:
                        lines.append("")
                        lines.append(f"Issues: {', '.join(issues)}")
                    response_text = "\n".join(lines)

                elif req_type in ("show_guide", "general_question", "use_tool", "unknown"):
                    response_text = result.get("answer") or "No answer available."

                else:
                    response_text = result.get("answer") or "I could not process that request."

                st.markdown(response_text)

                # Show trace
                if history:
                    with st.expander(f"Agent trace ({len(history)} steps)", expanded=False):
                        for step in history:
                            node = step.get("node", "?")
                            st.markdown(f"**`{node}`**")
                            for k, v in step.items():
                                if k != "node":
                                    st.markdown(f"- **{k}**: `{v}`")

                # Show tool results
                if tool_results:
                    with st.expander("Tool results", expanded=True):
                        import json
                        st.code(json.dumps(tool_results, indent=2), language="json")

                # Save to session
                st.session_state.messages.append({
                    "role": "assistant",
                    "content": response_text,
                    "history": history,
                    "tool_results": tool_results,
                })

            except Exception as exc:
                err = f"Error: {exc}"
                st.error(err)
                st.session_state.messages.append({
                    "role": "assistant",
                    "content": err,
                    "history": [],
                    "tool_results": None,
                })
