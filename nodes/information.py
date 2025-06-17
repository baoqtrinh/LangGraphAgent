from models.state import BoxState
from utils.llm_utils import llm
from config.design_rules import DESIGN_GUIDE

def show_guide_fn(state: BoxState) -> BoxState:
    """Return the design guidelines in a readable format."""
    guide_text = "# Building Design Guidelines\n\n"
    
    for rule in DESIGN_GUIDE:
        rule_text = rule["rule"]
        if "min" in rule and "max" in rule:
            guide_text += f"- {rule_text} (Min: {rule['min']}, Max: {rule['max']})\n"
        elif "min" in rule:
            guide_text += f"- {rule_text} (Min: {rule['min']})\n"
        elif "max" in rule:
            guide_text += f"- {rule_text} (Max: {rule['max']})\n"
        elif "condition" in rule:
            guide_text += f"- {rule_text} (When: {rule['condition']})\n"
        else:
            guide_text += f"- {rule_text}\n"
    
    state.answer = guide_text
    state.done = True
    
    state.history.append({
        "node": "show_guide",
        "answer": guide_text
    })
    
    return state

def handle_unknown_fn(state: BoxState) -> BoxState:
    """Handle unknown request types with a helpful message."""
    user_input = state.request.get("user_input", "")
    
    state.answer = f"""
I'm not sure how to process your request: "{user_input}"

I can help you with:
1. Designing a building based on parameters
2. Showing building design guidelines
3. Answering general questions about architecture and building design

Please try rephrasing your request or provide specific building parameters.
"""
    state.done = True
    
    state.history.append({
        "node": "handle_unknown",
        "answer": state.answer
    })
    
    return state