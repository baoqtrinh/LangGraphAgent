import os
from dotenv import load_dotenv

load_dotenv()

# LLM configuration (OpenAI-compatible endpoint, e.g. LM Studio)
LLM_ENDPOINT = os.getenv("LLM_ENDPOINT", "http://localhost:1234/v1/chat/completions")
LLM_MODEL = os.getenv("LLM_MODEL", None)          # None = use server default
LLM_TEMPERATURE = float(os.getenv("LLM_TEMPERATURE", "0.2"))
LLM_TIMEOUT = int(os.getenv("LLM_TIMEOUT", "60"))

# GH MCP Server (the Grasshopper plugin HTTP server)
MCP_GH_ENDPOINT = os.getenv("MCP_GH_ENDPOINT", "http://localhost:5100")
MCP_TIMEOUT = int(os.getenv("MCP_TIMEOUT", "30"))

# Tavily web search
TAVILY_API_KEY = os.getenv("TAVILY_API_KEY", "")
