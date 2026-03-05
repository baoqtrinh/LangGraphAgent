from langchain_core.tools import tool

@tool
def compute_other_dimension(area: float, known_dimension: float) -> float:
    """Compute the other dimension (depth or width) given area and one dimension."""
    return area / known_dimension

@tool
def calculate_aspect_ratio(width: float, depth: float) -> float:
    """Calculate the aspect ratio (width:depth)."""
    return width / depth

@tool
def calculate_total_height(n_floors: int, floor_height: float) -> float:
    """Calculate the total height of the building."""
    return n_floors * floor_height

@tool
def calculate_window_area(floor_area: float, window_ratio: float) -> float:
    """Calculate required window area based on floor area."""
    return floor_area * window_ratio