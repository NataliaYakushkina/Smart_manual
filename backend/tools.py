"""Tool definitions for the Realtime model. Each tool maps 1:1 to a public StepEngine method in Unity.
Realtime uses the FLAT tool shape: {"type", "name", "description", "parameters"}."""
from content import Content


def _tool(name: str, description: str, properties: dict | None = None, required: list | None = None) -> dict:
    return {
        "type": "function",
        "name": name,
        "description": description,
        "parameters": {
            "type": "object",
            "properties": properties or {},
            "required": required or [],
        },
    }


def build_tools(content: Content) -> list:
    return [
        _tool("start_procedure", "Start a step-by-step guide on the device.",
              {"procedure_id": {"type": "string", "enum": content.procedure_ids}}, ["procedure_id"]),
        _tool("next_step", "Go to the next step (the user finished the current one). Finishes the guide after the last step."),
        _tool("previous_step", "Go back one step."),
        _tool("go_to_step", "Jump to a step of the running guide.",
              {"step": {"type": "integer", "minimum": 1, "description": "Step number, starting at 1."}}, ["step"]),
        _tool("repeat_step", "Play the current step's animation again."),
        _tool("stop_procedure", "Stop the running guide and go back to the start screen."),
        _tool("highlight_part", "Highlight a part of the real device in AR.",
              {"part": {"type": "string", "enum": content.parts}}, ["part"]),
        _tool("point_at", "Point at a spot on the device, e.g. a display area or a battery contact.",
              {"marker": {"type": "string", "enum": content.markers}}, ["marker"]),
        _tool("show_xray", "Show or hide the inside of the battery compartment (x-ray view).",
              {"on": {"type": "boolean"}}, ["on"]),
        _tool("clear_highlights", "Remove all highlights and pointers you added."),
    ]
