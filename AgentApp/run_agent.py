import os
from dotenv import load_dotenv
from models.state import BoxState
from graphs.main_graph import build_main_graph

# Load environment variables
load_dotenv()

# Build the graph
graph = build_main_graph()

# Import necessary libraries for visualization
from IPython.display import Image, display

# Save the graph visualization to a PNG file
def save_graph_visualization():
    """Save the agent graph visualization as a PNG file."""
    try:
        # Create a 'visualizations' directory if it doesn't exist
        os.makedirs("visualizations", exist_ok=True)
        
        # Generate and save the graph visualization
        graph_png = graph.get_graph().draw_mermaid_png()
        with open("visualizations/building_design_agent_graph.png", "wb") as f:
            f.write(graph_png)
        
        print("✅ Graph visualization saved to 'visualizations/building_design_agent_graph.png'")
        
        # Try to display the image if in a notebook environment
        try:
            display(Image("visualizations/building_design_agent_graph.png"))
        except:
            pass  # Not in a notebook environment, so we can't display the image
            
    except Exception as e:
        print(f"❌ Failed to save graph visualization: {str(e)}")

def run_single_interaction():
    """Run a single interaction with the architectural assistant."""
    # Get user input
    print("\n" + "="*80)
    print("🏢 ARCHITECTURAL ASSISTANT")
    print("="*80)
    print("\nI can help you with:")
    print("1. Designing a building based on parameters")
    print("2. Showing building design guidelines")
    print("3. Answering questions about architecture and building design")
    
    print("\nExample requests:")
    print("• \"Design a building with 1000 square meters of area and 3 floors\"")
    print("• \"Show me the design guidelines\"")
    print("• \"What are the latest trends in sustainable architecture?\"")
    
    user_query = input("\nWhat would you like to do? (Enter a question or press Enter to design a building): ")
    
    if user_query:
        # User provided input
        params = {
            "user_input": user_query
        }
    else:
        # No input, use default design parameters
        print("\nUsing default building parameters (area: 800 sqm, floors: 2, floor height: 3m)")
        params = {
            "area": 800,
            "n_floors": 2,
            "floor_height": 3
        }
    
    # Create initial state
    state = BoxState(request=params)
    
    # Print processing message
    print("\nProcessing your request...")
    
    # Run the agent
    final_state_dict = graph.invoke(state, config={"recursion_limit": 50})
    
    # Display results based on request type
    if final_state_dict.get("answer"):
        print("\n" + "="*80)
        print("RESPONSE:")
        print("="*80)
        print(final_state_dict.get("answer"))
        
        # If we have search results, show them as sources
        if final_state_dict.get("search_results"):
            print("\n" + "-"*80)
            print("SOURCES:")
            for i, result in enumerate(final_state_dict.get("search_results")):
                print(f"\n[Source {i+1}] {result.get('title', 'No title')}")
                print(f"URL: {result.get('url', 'No URL')}")
    else:
        # Print the design results with beautiful formatting
        print("\n" + "="*80)
        print("FINAL BUILDING DESIGN:")
        print("="*80)
        
        print(f"\n📊 BUILDING SPECIFICATIONS:")
        print(f"  • Compliant: {final_state_dict.get('compliant')}")
        if final_state_dict.get('issues'):
            print(f"  • Issues: {', '.join(final_state_dict.get('issues'))}")
        else:
            print(f"  • Issues: None")
        
        print("\n📐 FINAL DIMENSIONS:")
        box = final_state_dict.get('box', {})
        for key, value in box.items():
            if isinstance(value, (int, float)) and value is not None:
                print(f"  • {key.replace('_', ' ').title()}: {value:.2f}" if isinstance(value, float) else f"  • {key.replace('_', ' ').title()}: {value}")
            elif value is not None:
                print(f"  • {key.replace('_', ' ').title()}: {value}")
        
        print("\n🧠 DESIGN REASONING PROCESS:")
        steps = []
        for i, h in enumerate(final_state_dict.get("history", [])):
            if h.get("thought") or h.get("action") or h.get("observation"):
                steps.append(h)
        
        for i, step in enumerate(steps):
            print(f"\n----- ITERATION {i+1} -----")
            if step.get("thought"):
                print(f"💭 THOUGHT:")
                print(f"  {step.get('thought')[:200]}..." if len(step.get('thought', '')) > 200 else f"  {step.get('thought')}")
            
            if step.get("action"):
                print(f"🛠️ ACTION:")
                print(f"  {step.get('action')}")
            
            if step.get("observation"):
                print(f"👁️ OBSERVATION:")
                print(f"  {step.get('observation')}")
    
    return final_state_dict

def run_interactive_mode():
    """Run the architectural assistant in interactive mode."""
    # Save and display graph visualization
    save_graph_visualization()
    
    continue_session = True
    
    while continue_session:
        # Run a single interaction
        run_single_interaction()
        
        # Ask if the user wants to continue
        print("\n" + "-"*80)
        continue_response = input("Would you like to make another request? (y/n): ")
        continue_session = continue_response.lower() == 'y'
    
    print("\nThank you for using the Architectural Assistant. Goodbye!")

if __name__ == "__main__":
    run_interactive_mode()