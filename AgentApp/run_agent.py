"""Design Agent — terminal REPL.

Usage (from AgentApp/ directory):
    python run_agent.py

Commands during chat:
    reload   — re-fetch GH tools from the MCP server
    tools    — list currently loaded GH tools
    quit / exit / Ctrl-C  — exit
"""
import os
import sys
import textwrap

from dotenv import load_dotenv

# ── Bootstrap path so imports work when run directly ─────────────────────────
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
load_dotenv()

HR  = "─" * 72
HR2 = "═" * 72


def _check_llm() -> bool:
    """Ping the LLM endpoint. Returns True if reachable."""
    import requests
    try:
        from app.config import LLM_ENDPOINT
    except ImportError:
        LLM_ENDPOINT = os.getenv("LLM_ENDPOINT", "http://localhost:1234/v1/chat/completions")

    # Derive the /models probe URL (works for LM Studio, Ollama, OpenAI-compat)
    base = LLM_ENDPOINT.rstrip("/").removesuffix("/chat/completions").removesuffix("/v1")
    probe = f"{base}/v1/models"
    try:
        resp = requests.get(probe, timeout=3)
        if resp.status_code < 500:
            print(f"  [llm] OK  {LLM_ENDPOINT}")
            return True
    except Exception:
        pass
    print(f"  [llm] UNREACHABLE  {LLM_ENDPOINT}")
    print("        Make sure LM Studio (or your local LLM) is running before sending prompts.")
    return False


def _banner():
    print(HR2)
    print("  Design Agent  (terminal mode)")
    print(HR2)
    print("  Commands:  reload · tools · quit")
    print(HR)


def _print_tools():
    try:
        from tools import TOOL_CLASSES
        if not TOOL_CLASSES:
            print("  [tools] No GH tools loaded — is the Grasshopper plugin running?")
        else:
            print(f"  [tools] {len(TOOL_CLASSES)} tool(s) loaded:")
            for t in TOOL_CLASSES:
                print(f"    • {t.name}  —  {t.description[:80]}")
    except Exception as exc:
        print(f"  [tools] error: {exc}")


def _reload_tools():
    try:
        from tools import reload_mcp_tools
        tools = reload_mcp_tools()
        print(f"  [reload] {len(tools)} tool(s) loaded.")
    except Exception as exc:
        print(f"  [reload] error: {exc}")


def _run(graph, user_input: str):
    from models.state import BoxState
    state = BoxState(request={"user_input": user_input})
    print()
    print(f"  ┊ input: {user_input}")
    try:
        result = graph.invoke(state, config={"recursion_limit": 50})
    except Exception as exc:
        print(f"\n  [error] {exc}")
        return

    answer = result.get("answer") or ""
    request_type = result.get("request_type", "?")
    tool_results  = result.get("tool_results") or {}

    print(f"\n[{request_type}]")
    print(HR)

    if answer:
        for line in textwrap.wrap(answer, width=70) or [answer]:
            print(" ", line)
    elif result.get("box"):
        box = result["box"]
        print(f"  Compliant : {result.get('compliant')}")
        issues = result.get("issues") or []
        print(f"  Issues    : {', '.join(issues) if issues else 'none'}")
        print("  Box       :")
        for k, v in box.items():
            print(f"    {k}: {v}")
    else:
        print("  (no answer)")

    if tool_results:
        print(HR)
        print("  Tool results:")
        for name, val in tool_results.items():
            print(f"    [{name}] {str(val)[:200]}")

    print(HR)


def main():
    _banner()
    _check_llm()
    _print_tools()
    print()

    from graphs.main_graph import build_main_graph
    graph = build_main_graph()

    # ── save + open graph image on startup ───────────────────────────────────
    try:
        import subprocess
        os.makedirs("visualizations", exist_ok=True)
        png_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "visualizations", "graph.png")
        png_bytes = graph.get_graph().draw_mermaid_png()
        with open(png_path, "wb") as f:
            f.write(png_bytes)
        print(f"  [graph] saved → {png_path}")
        subprocess.Popen(["start", png_path], shell=True)
    except Exception as exc:
        print(f"  [graph] could not render image: {exc}")
    print()

    while True:
        try:
            user_input = input("You: ").strip()
        except (KeyboardInterrupt, EOFError):
            print("\nBye.")
            break

        if not user_input:
            continue

        if user_input.lower() in ("quit", "exit", "q"):
            print("Bye.")
            break
        elif user_input.lower() == "reload":
            _reload_tools()
            continue
        elif user_input.lower() == "tools":
            _print_tools()
            continue

        _run(graph, user_input)


if __name__ == "__main__":
    main()


