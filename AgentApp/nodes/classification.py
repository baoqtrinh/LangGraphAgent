from models.state import BoxState
from utils.llm_utils import llm


def classify_input_fn(state: BoxState) -> BoxState:
    """Classify the user input into one of five routing categories."""
    user_input = state.request.get("user_input", "")

    if not user_input:
        state.request_type = "design_building"
        return state

    # Dynamically describe available tools in the prompt
    try:
        from tools import TOOL_CLASSES
        tool_names = ", ".join(t.name for t in TOOL_CLASSES) if TOOL_CLASSES else "none loaded"
        tool_section = (
            f"5. use_tool: The user wants to run one of these Grasshopper tools: {tool_names}"
        )
    except Exception:
        tool_section = "5. use_tool: The user wants to run a Grasshopper tool"

    prompt = f"""You are an architectural assistant that routes user requests.

User request: "{user_input}"

Classify it into EXACTLY ONE of these categories:

1. design_building: The user wants to design or size a building with specific parameters
2. show_guide: The user wants to see design guidelines, rules, or constraints
3. general_question: The user is asking a general architecture or building design question
4. {tool_section}
5. unknown: Does not fit any category above

Output ONLY the category name (design_building, show_guide, general_question, use_tool, or unknown).

Classification:"""

    response = llm(prompt)
    classification = str(response).strip().lower()

    if "design_building" in classification:
        state.request_type = "design_building"
    elif "show_guide" in classification:
        state.request_type = "show_guide"
    elif "general_question" in classification:
        state.request_type = "general_question"
    elif "use_tool" in classification:
        state.request_type = "use_tool"
    else:
        state.request_type = "unknown"

    print(f"[classify] '{user_input}' → {state.request_type}")

    state.history.append({
        "node": "classify_input",
        "request_type": state.request_type,
        "user_input": user_input,
    })
    return state
