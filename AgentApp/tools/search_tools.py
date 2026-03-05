import os
from langchain_core.tools import tool
from langchain_tavily import TavilySearch
from dotenv import load_dotenv
from typing import Dict, Any, List

load_dotenv()
TAVILY_API_KEY = os.getenv("TAVILY_API_KEY")

# Initialize Tavily Search
tavily_search = TavilySearch(tavily_api_key=TAVILY_API_KEY, max_results=3)

@tool
def search_web(query: str) -> Dict[str, Any]:
    """Search the web for information related to the query."""
    try:
        results = tavily_search.invoke(query)
        return results
    except Exception as e:
        return {"error": str(e), "results": []}