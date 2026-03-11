import os
from dotenv import load_dotenv

# Resolve paths relative to this file so they work regardless of cwd
_HERE = os.path.dirname(os.path.abspath(__file__))
_ROOT = os.path.dirname(_HERE)  # AgentApp/

# Load .env first, then .env.local overrides it (local secrets take priority)
load_dotenv(os.path.join(_ROOT, ".env"))
load_dotenv(os.path.join(_ROOT, ".env.local"), override=True)

# ── Provider switch ───────────────────────────────────────────────────────────
# Set LLM_PROVIDER=gemini in .env.local to route all calls through Google Gemini.
# Set LLM_PROVIDER=local (default) to use the local OpenAI-compatible server.
LLM_PROVIDER = os.getenv("LLM_PROVIDER", "local").lower()

# ── Local LLM (OpenAI-compatible endpoint, e.g. LM Studio) ───────────────────
LLM_ENDPOINT = os.getenv("LLM_ENDPOINT", "http://localhost:1234/v1/chat/completions")
LLM_MODEL = os.getenv("LLM_MODEL", None)          # None = use server default
LLM_TEMPERATURE = float(os.getenv("LLM_TEMPERATURE", "0.2"))
LLM_TIMEOUT = int(os.getenv("LLM_TIMEOUT", "60"))

# ── Google Gemini ─────────────────────────────────────────────────────────────
GOOGLE_API_KEY = os.getenv("GOOGLE_API_KEY", "")
GEMINI_MODEL = os.getenv("GEMINI_MODEL", "gemini-2.5-flash-lite")

# GH MCP Server (the Grasshopper plugin HTTP server)
MCP_GH_ENDPOINT = os.getenv("MCP_GH_ENDPOINT", "http://localhost:5100")
MCP_TIMEOUT = int(os.getenv("MCP_TIMEOUT", "30"))

# Tavily web search
TAVILY_API_KEY = os.getenv("TAVILY_API_KEY", "")
