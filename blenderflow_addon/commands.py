"""Blender operators for BlenderFlow AI Bridge.

Includes the AI prompt dialog operator that's invoked
from the WebSocket server when a prompt request arrives.
"""

import bpy
import json
from . import ws_server


class BLENDERFLOW_OT_ai_prompt_dialog(bpy.types.Operator):
    """Show AI prompt input dialog"""
    bl_idname = "blenderflow.ai_prompt_dialog"
    bl_label = "AI Generate 3D Model"

    prompt: bpy.props.StringProperty(
        name="Prompt",
        description="Describe the 3D model to generate",
        default="a modern wooden chair"
    )

    def execute(self, context):
        # Send prompt back to C# plugin via WebSocket
        ws_server._broadcast({
            "type": "ai_prompt_response",
            "prompt": self.prompt
        })
        self.report({'INFO'}, f"BlenderFlow: AI prompt sent: {self.prompt}")
        return {'FINISHED'}

    def invoke(self, context, event):
        return context.window_manager.invoke_props_dialog(self, width=400)

    def draw(self, context):
        layout = self.layout
        layout.prop(self, "prompt")

    def cancel(self, context):
        # User cancelled the dialog
        ws_server._broadcast({"type": "ai_prompt_cancelled"})


_classes = (
    BLENDERFLOW_OT_ai_prompt_dialog,
)


def register():
    # Idempotent: drop stale registrations (e.g. from an interrupted disable)
    # before re-adding, otherwise re-enabling the addon errors out.
    for cls in _classes:
        try:
            bpy.utils.unregister_class(cls)
        except (RuntimeError, ValueError):
            pass
        bpy.utils.register_class(cls)


def unregister():
    for cls in reversed(_classes):
        try:
            bpy.utils.unregister_class(cls)
        except (RuntimeError, ValueError):
            pass
