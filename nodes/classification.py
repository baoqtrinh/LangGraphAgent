from models.state import BoxState
from utils.llm_utils import llm

def classify_input_fn(state: BoxState) -> BoxState:
    """Classify the user input using LLM in Plan style."""
    user_input = state.request.get("user_input", "")
    
    # If user_input is not provided, default to building design with parameters
    if not user_input:
        state.request_type = "design_building"
        return state
    
    # Generate classification using LLM in Plan style
    prompt = f"""
    You are an architectural assistant that classifies user requests into specific categories.
    
    Given the following user request: "{user_input}"
    
    Classify it into EXACTLY ONE of these categories:
    
    1. design_building: The user wants to design or create a building with specific parameters
    2. show_guide: The user wants to see design guidelines, rules, or constraints
    3. general_question: The user is asking a question about architecture or building design
    4. unknown: The request doesn't clearly fit into any of the above categories
    
    First, analyze the request and think about which category it best fits.
    Then output ONLY the category name (design_building, show_guide, general_question, or unknown).
    
    Your classification:
    """
    
    response = llm(prompt, max_tokens=20)
    if isinstance(response, dict) and 'result' in response and 'response' in response['result']:
        classification = response['result']['response'].strip().lower()
    else:
        classification = str(response).strip().lower()
    
    # Extract just the category name if there's additional text
    if "design_building" in classification:
        state.request_type = "design_building"
    elif "show_guide" in classification:
        state.request_type = "show_guide"
    elif "general_question" in classification:
        state.request_type = "general_question"
    else:
        state.request_type = "unknown"
    
    print(f"Classified input: '{user_input}' as {state.request_type}")
    
    state.history.append({
        "node": "classify_input",
        "request_type": state.request_type,
        "user_input": user_input
    })
    
    return state