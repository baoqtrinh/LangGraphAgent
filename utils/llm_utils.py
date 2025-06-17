import os
import requests
import re
import json
from dotenv import load_dotenv

load_dotenv()
API_KEY = os.getenv("CLOUDFLARE_API_KEY")
ACCOUNT_ID = os.getenv("CLOUDFLARE_ACCOUNT_ID")

class CloudflareLLM:
    def __init__(self, api_key, account_id, model="@cf/meta/llama-3.3-70b-instruct-fp8-fast"):
        self.api_key = api_key
        self.account_id = account_id
        self.model = model

    def __call__(self, prompt, **kwargs):
        url = f"https://api.cloudflare.com/client/v4/accounts/{self.account_id}/ai/run/{self.model}"
        headers = {
            "Authorization": f"Bearer {self.api_key}",
            "Content-Type": "application/json"
        }
        data = {
            "messages": [
                {"role": "system", "content": "You are a building code compliance expert. "
                "Given building design proposals and code requirements, "
                "determine if the proposal is compliant. "
                "Explain your reasoning step by step."},
                {"role": "user", "content": prompt}
            ]
        }
        response = requests.post(url, headers=headers, json=data)
        response.raise_for_status()
        result = response.json()
        # Return the full result for debugging
        return result

def extract_building_params_with_cf_llm(user_message, account_id=ACCOUNT_ID, api_key=API_KEY):
    """
    Use Cloudflare LLM to extract building parameters and show reasoning.
    Returns a dict with width, depth, n_floors, floor_height, area (None if missing), and reasoning.
    """
    prompt = (
        "You are a helpful assistant. "
        "Given the user's message, first explain your reasoning step by step. "
        "Then, extract the following building parameters and return them as a JSON object: "
        "width (number or null), depth (number or null), n_floors (integer or null), floor_height (number or null), area (number or null). "
        "If a field is missing, set it to null.\n"
        "Example output:\n"
        "Reasoning:\n"
        "The user provided area, number of floors, and floor height, but did not specify width or depth. Therefore, width and depth are set to null.\n"
        "Extracted JSON:\n"
        "{\n"
        "  \"width\": null,\n"
        "  \"depth\": null,\n"
        "  \"n_floors\": 3,\n"
        "  \"floor_height\": 3.5,\n"
        "  \"area\": 800\n"
        "}\n"
    f"User message: {user_message}\n"
    "Reasoning:\n"
    "Extracted JSON:"
    )
    
    llm_instance = CloudflareLLM(api_key, account_id)
    llm_result = llm_instance(prompt)
    
    # Extract the text output from the full result
    if isinstance(llm_result, dict) and "result" in llm_result and "response" in llm_result["result"]:
        response = llm_result["result"]["response"]
    else:
        response = str(llm_result)

    # Try to extract JSON from the response
    match = re.search(r'\{.*\}', response, re.DOTALL)
    json_obj = None
    if match:
        try:
            json_obj = json.loads(match.group(0))
        except Exception:
            json_obj = None

    # Always set llm_reasoning to the full response text
    return {
        "params": json_obj,
        "llm_reasoning": response,
    }

# Initialize global LLM instance for use throughout the application
llm = CloudflareLLM(API_KEY, ACCOUNT_ID)