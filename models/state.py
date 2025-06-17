from pydantic import BaseModel, Field
from typing import Dict, Any, List, Optional, Union, Literal

class BoxState(BaseModel):
    """State model for the architectural assistant agent."""
    # Basic request and response
    request: Dict[str, Any]
    answer: Optional[str] = None
    done: Optional[bool] = None
    
    # Input classification
    request_type: Optional[Literal["design_building", "show_guide", "general_question", "unknown"]] = None
    
    # Building design
    box: Optional[Dict[str, Any]] = None
    compliant: Optional[bool] = None
    issues: Optional[List[str]] = None
    rules: Optional[List[Dict[str, Any]]] = None
    current_width: Optional[float] = None
    
    # ReAct components
    thought: Optional[str] = None
    action: Optional[Dict[str, Any]] = None
    observation: Optional[str] = None
    
    # Search components
    search_query: Optional[str] = None
    search_results: Optional[List[Dict[str, Any]]] = Field(default_factory=list)
    needs_search: Optional[bool] = None
    
    # Additional parameters for expanded constraints
    window_area: Optional[float] = None
    emergency_exits: Optional[bool] = None
    aspect_ratio: Optional[float] = None
    
    # History tracking
    history: List[Dict[str, Any]] = Field(default_factory=list)