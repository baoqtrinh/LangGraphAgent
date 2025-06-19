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

# Initialize global LLM instance for use throughout the application
llm = CloudflareLLM(API_KEY, ACCOUNT_ID)