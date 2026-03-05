"""
GH Tool execution node.
The LLM picks which loaded MCP tool to call and with what args, then
formats the result as a natural-language answer.
"""
import json
from typing import Any, Dict, List

from langchain_core.messages import HumanMessage, SystemMessage, ToolMessage

from models.state import BoxState
from utils.llm_utils import chat_llm


def execute_gh_tool_fn(state: BoxState) -> BoxState:
    """Use LLM + tool-calling to invoke the appropriate GH MCP tool."""
    # Lazily import to avoid circular deps and to pick up any reload
    from tools import TOOL_CLASSES

    user_input: str = state.request.get("user_input", "")

    if not TOOL_CLASSES:
        state.answer = (
            "No GH tools are currently loaded. "
            "Make sure the Grasshopper plugin is running and set to Active, "
            "then reload tools in the sidebar."
        )
        state.done = True
        state.history.append({"node": "execute_gh_tool", "status": "no_tools"})
        return state

    # Build tool descriptions for the system prompt
    tool_list = "\n".join(
        f"- `{t.name}`: {t.description}" for t in TOOL_CLASSES
    )
    system_prompt = (
        "You are a design assistant. The user wants to run one of the following "
        "Grasshopper tools. Pick the most relevant tool, supply the required "
        "arguments, and call it. If several tools are needed, call them in sequence.\n\n"
        f"Available tools:\n{tool_list}"
    )

    llm_with_tools = chat_llm.bind_tools(TOOL_CLASSES)
    messages = [
        SystemMessage(content=system_prompt),
        HumanMessage(content=user_input),
    ]

    # First LLM call — may request tool call(s)
    response = llm_with_tools._generate(messages)
    ai_msg = response.generations[0].message

    results: Dict[str, Any] = {}

    if not ai_msg.tool_calls:
        # LLM answered without calling a tool
        state.answer = ai_msg.content
        state.done = True
        state.history.append({"node": "execute_gh_tool", "status": "direct_answer"})
        return state

    # Execute each requested tool call
    tool_messages: List[ToolMessage] = []
    for tc in ai_msg.tool_calls:
        tool_name: str = tc["name"]
        tool_args: Dict[str, Any] = tc.get("args", {})
        tool_id: str = tc.get("id", tool_name)

        matching = [t for t in TOOL_CLASSES if t.name == tool_name]
        if not matching:
            result_str = f"Error: tool '{tool_name}' not found."
        else:
            result_str = matching[0]._run(**tool_args)

        results[tool_name] = result_str
        tool_messages.append(
            ToolMessage(content=result_str, tool_call_id=tool_id)
        )

    # Second LLM call — synthesise results into natural language
    followup_messages = [*messages, ai_msg, *tool_messages]
    final_response = chat_llm._generate(followup_messages)
    final_answer = final_response.generations[0].message.content

    state.answer = final_answer
    state.tool_results = results
    state.done = True
    state.history.append({
        "node": "execute_gh_tool",
        "tools_called": list(results.keys()),
        "results": results,
    })
    return state
