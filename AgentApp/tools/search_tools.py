import os
from langchain_core.tools import tool
from dotenv import load_dotenv
from typing import Dict, Any

load_dotenv()
TAVILY_API_KEY = os.getenv("TAVILY_API_KEY")

# Only instantiate if key is available
_tavily = None
if TAVILY_API_KEY:
    from langchain_tavily import TavilySearch
    _tavily = TavilySearch(tavily_api_key=TAVILY_API_KEY, max_results=3)

@tool
def search_web(query: str) -> Dict[str, Any]:
    """Search the web for information related to the query."""
    if _tavily is None:
        return {"error": "TAVILY_API_KEY not set — web search unavailable.", "results": []}
    try:
        results = _tavily.invoke(query)
        return results
    except Exception as e:
        return {"error": str(e), "results": []}