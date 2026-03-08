"""
LLM wrapper for any OpenAI-compatible endpoint (LM Studio, Ollama, etc.).
Drop-in replacement for the old CloudflareLLM — now fully LangChain-compatible
and supports function/tool calling.
"""
import json
import os
from typing import Any, Dict, List, Optional, Sequence

import requests
from pydantic import Field
from langchain_core.language_models.chat_models import BaseChatModel
from langchain_core.messages import (
    AIMessage, BaseMessage, HumanMessage, SystemMessage, ToolMessage
)
from langchain_core.outputs import ChatGeneration, ChatResult
from langchain_core.tools import BaseTool
from langchain_core.utils.function_calling import convert_to_openai_tool
from langchain_core.messages import ToolCall

try:
    from app.config import LLM_ENDPOINT, LLM_MODEL, LLM_TEMPERATURE, LLM_TIMEOUT
except ImportError:
    from dotenv import load_dotenv
    load_dotenv()
    LLM_ENDPOINT = os.getenv("LLM_ENDPOINT", "http://localhost:1234/v1/chat/completions")
    LLM_MODEL = os.getenv("LLM_MODEL", None)
    LLM_TEMPERATURE = float(os.getenv("LLM_TEMPERATURE", "0.2"))
    LLM_TIMEOUT = int(os.getenv("LLM_TIMEOUT", "60"))


class ChatLocalLLM(BaseChatModel):
    """LangChain-compatible chat model for any OpenAI-compatible local endpoint."""

    endpoint: str = LLM_ENDPOINT
    temperature: float = LLM_TEMPERATURE
    model: Optional[str] = LLM_MODEL
    timeout: int = LLM_TIMEOUT
    tools: List[Dict[str, Any]] = Field(default_factory=list)

    @property
    def _llm_type(self) -> str:
        return "custom-local-chat-llm"

    def bind_tools(self, tools: Sequence[BaseTool], **kwargs: Any) -> "ChatLocalLLM":
        """Return a copy of this model with tools bound for function calling."""
        openai_tools = [convert_to_openai_tool(tool) for tool in tools]
        return self.__class__(
            endpoint=self.endpoint,
            temperature=self.temperature,
            model=self.model,
            timeout=self.timeout,
            tools=openai_tools,
        )

    def _generate(
        self,
        messages: List[BaseMessage],
        stop: Optional[List[str]] = None,
        run_manager: Optional[Any] = None,
        **kwargs: Any,
    ) -> ChatResult:
        def _to_openai(m: BaseMessage) -> Dict[str, Any]:
            if isinstance(m, HumanMessage):
                return {"role": "user", "content": m.content}
            elif isinstance(m, SystemMessage):
                return {"role": "system", "content": m.content}
            elif isinstance(m, AIMessage):
                msg: Dict[str, Any] = {"role": "assistant", "content": m.content or ""}
                if hasattr(m, "tool_calls") and m.tool_calls:
                    msg["tool_calls"] = [
                        {
                            "id": tc.get("id", tc["name"]),
                            "type": "function",
                            "function": {
                                "name": tc["name"],
                                "arguments": json.dumps(tc.get("args", {})),
                            },
                        }
                        for tc in m.tool_calls
                    ]
                return msg
            elif isinstance(m, ToolMessage):
                return {
                    "role": "tool",
                    "content": str(m.content),
                    "tool_call_id": getattr(m, "tool_call_id", "unknown"),
                }
            else:
                return {"role": "user", "content": str(m.content)}

        try:
            payload: Dict[str, Any] = {
                "messages": [_to_openai(m) for m in messages],
                "temperature": self.temperature,
            }
            if self.model:
                payload["model"] = self.model
            if stop:
                payload["stop"] = stop
            if self.tools:
                payload["tools"] = self.tools

            resp = requests.post(self.endpoint, json=payload, timeout=self.timeout)
            resp.raise_for_status()

            data = resp.json()
            choice = data.get("choices", [{}])[0]
            message = choice.get("message", {})
            content: str = message.get("content", "") or ""
            raw_tool_calls = message.get("tool_calls", [])

            tool_calls: List[ToolCall] = []
            for tc in raw_tool_calls:
                func_args = tc["function"]["arguments"]
                if isinstance(func_args, str):
                    try:
                        func_args = json.loads(func_args)
                    except json.JSONDecodeError:
                        func_args = {}
                tool_calls.append(
                    ToolCall(
                        name=tc["function"]["name"],
                        args=func_args,
                        id=tc.get("id", tc["function"]["name"]),
                    )
                )

            ai_msg = (
                AIMessage(content=content, tool_calls=tool_calls)
                if tool_calls
                else AIMessage(content=content)
            )
            return ChatResult(generations=[ChatGeneration(message=ai_msg)])

        except Exception as exc:
            raise RuntimeError(f"LLM request failed: {exc}") from exc


# ── Backwards-compatible simple call helper ──────────────────────────────────
# Existing node code calls  llm(prompt)  or  llm(prompt, max_tokens=N).
# This shim keeps that interface working.

class _SimpleLLMShim:
    """Thin wrapper: exposes __call__(prompt) -> str and a .chat property."""

    def __init__(self, timeout: Optional[int] = None) -> None:
        self._chat = ChatLocalLLM() if timeout is None else ChatLocalLLM(timeout=timeout)

    def __call__(self, prompt: str, **_: Any) -> str:
        result = self._chat._generate([HumanMessage(content=prompt)])
        return result.generations[0].message.content

    @property
    def chat(self) -> ChatLocalLLM:
        return self._chat


llm      = _SimpleLLMShim()          # default timeout (60s) — for full responses
fast_llm = _SimpleLLMShim(timeout=15) # short timeout — for classify / yes-no calls
chat_llm = ChatLocalLLM()
