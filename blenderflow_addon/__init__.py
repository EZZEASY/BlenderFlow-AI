bl_info = {
    "name": "BlenderFlow AI Bridge",
    "blender": (3, 6, 0),
    "category": "Interface",
    "version": (1, 0, 0),
    "author": "Leo L",
    "description": "WebSocket bridge for Logitech MX Creative Console",
}

import bpy
import sys
import os
import threading
import asyncio

# ─── 把内嵌的 vendor 目录加入 Python 搜索路径 ───
_vendor_dir = os.path.join(os.path.dirname(__file__), "vendor")
if _vendor_dir not in sys.path:
    sys.path.insert(0, _vendor_dir)

from . import ws_server
from . import commands


_server_thread = None
_server_loop = None


# ─── Operators & UI ───

class BLENDERFLOW_OT_start_server(bpy.types.Operator):
    bl_idname = "blenderflow.start_server"
    bl_label = "Start BlenderFlow Server"

    def execute(self, context):
        start_server()
        self.report({'INFO'}, "BlenderFlow WebSocket server started on ws://localhost:9876")
        return {'FINISHED'}


class BLENDERFLOW_OT_stop_server(bpy.types.Operator):
    bl_idname = "blenderflow.stop_server"
    bl_label = "Stop BlenderFlow Server"

    def execute(self, context):
        stop_server()
        self.report({'INFO'}, "BlenderFlow WebSocket server stopped")
        return {'FINISHED'}


class BLENDERFLOW_PT_panel(bpy.types.Panel):
    bl_label = "BlenderFlow AI"
    bl_idname = "BLENDERFLOW_PT_panel"
    bl_space_type = 'VIEW_3D'
    bl_region_type = 'UI'
    bl_category = "BlenderFlow"

    def draw(self, context):
        layout = self.layout
        if _server_thread and _server_thread.is_alive():
            layout.label(text="● Server Running", icon='CHECKMARK')
            layout.operator("blenderflow.stop_server")
        else:
            layout.label(text="○ Server Stopped")
            layout.operator("blenderflow.start_server")


# ─── Server 管理 ───

def start_server():
    global _server_thread, _server_loop
    if _server_thread and _server_thread.is_alive():
        return

    _server_loop = asyncio.new_event_loop()
    _server_thread = threading.Thread(
        target=lambda: _server_loop.run_until_complete(ws_server.run_server(_server_loop)),
        daemon=True,
        name="BlenderFlow-WS"
    )
    _server_thread.start()
    print("BlenderFlow: WebSocket server started on ws://localhost:9876")


def stop_server():
    global _server_thread, _server_loop
    if _server_loop:
        _server_loop.call_soon_threadsafe(_server_loop.stop)
        _server_loop = None
    _server_thread = None
    print("BlenderFlow: WebSocket server stopped")


# ─── 注册 ───

_classes = (
    BLENDERFLOW_OT_start_server,
    BLENDERFLOW_OT_stop_server,
    BLENDERFLOW_PT_panel,
)


def register():
    for cls in _classes:
        bpy.utils.register_class(cls)
    commands.register()
    start_server()


def unregister():
    stop_server()
    commands.unregister()
    for cls in reversed(_classes):
        bpy.utils.unregister_class(cls)
