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
import importlib

# ─── 把内嵌的 vendor 目录加入 Python 搜索路径 ───
_vendor_dir = os.path.join(os.path.dirname(__file__), "vendor")
if _vendor_dir not in sys.path:
    sys.path.insert(0, _vendor_dir)

from . import ws_server
from . import commands
from . import providers


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

def _run_loop(loop):
    """Thread target: drive the asyncio loop until run_server() returns."""
    asyncio.set_event_loop(loop)
    try:
        loop.run_until_complete(ws_server.run_server(loop))
    finally:
        try:
            loop.close()
        except Exception as e:
            print(f"BlenderFlow: loop close error: {e}")


def start_server():
    global _server_thread, _server_loop
    if _server_thread and _server_thread.is_alive():
        return

    _server_loop = asyncio.new_event_loop()
    _server_thread = threading.Thread(
        target=_run_loop,
        args=(_server_loop,),
        daemon=True,
        name="BlenderFlow-WS",
    )
    _server_thread.start()
    print("BlenderFlow: WebSocket server started on ws://localhost:9876")


def stop_server():
    """Signal the server to stop and wait for the thread to exit.

    Must be synchronous: Blender's unregister() must fully release the port
    before a subsequent register() tries to bind it again.
    """
    global _server_thread, _server_loop
    thread = _server_thread
    loop = _server_loop
    _server_thread = None
    _server_loop = None

    if loop is not None:
        ws_server.request_stop()

    if thread is not None and thread.is_alive():
        thread.join(timeout=3.0)
        if thread.is_alive():
            print("BlenderFlow: WARNING — server thread did not exit within 3s")

    print("BlenderFlow: WebSocket server stopped")


# ─── Addon Preferences (UI for AI providers) ───


class BlenderFlowPreferences(bpy.types.AddonPreferences):
    bl_idname = __name__  # resolves to "blenderflow_addon"

    # Which provider the AI Generate button dispatches to.
    ai_provider: bpy.props.EnumProperty(
        name="AI Provider",
        description="Which service to use for AI 3D generation",
        items=[
            ('hyperrodin', "Hyper3D Rodin",
             "hyperhuman.deemos.com — has a shared free-trial key, good quality"),
            ('hunyuan3d', "Hunyuan3D (Tencent)",
             "Tencent Cloud Hunyuan3D — works from mainland China without a VPN"),
            ('tripo', "Tripo",
             "tripo3d.ai — good quality, but requires a VPN from some regions"),
        ],
        default='hyperrodin',
    )

    # ─── Tripo ──────────────────────────────────────────────────
    tripo_api_key: bpy.props.StringProperty(
        name="API Key",
        description="Bearer token from tripo3d.ai",
        default="",
        subtype='PASSWORD',
    )

    # ─── Hunyuan3D (Tencent Cloud) ──────────────────────────────
    hunyuan_secret_id: bpy.props.StringProperty(
        name="SecretId",
        description="Tencent Cloud API SecretId (console.cloud.tencent.com → CAM → API Keys)",
        default="",
    )
    hunyuan_secret_key: bpy.props.StringProperty(
        name="SecretKey",
        description="Tencent Cloud API SecretKey",
        default="",
        subtype='PASSWORD',
    )
    hunyuan_api_type: bpy.props.EnumProperty(
        name="Model Type",
        description="PRO = richer output, RAPID = faster but image-only",
        items=[
            ('PRO', "PRO (text or image)",
             "Higher quality. Accepts either a prompt OR an image, not both"),
            ('RAPID', "RAPID (image only)",
             "Faster. Requires an input image"),
        ],
        default='PRO',
    )

    # ─── Hyper3D Rodin ──────────────────────────────────────────
    hyperrodin_api_key: bpy.props.StringProperty(
        name="API Key",
        description="Bearer token from hyperhuman.deemos.com. Leave empty to use the shared free-trial key.",
        default="",
        subtype='PASSWORD',
    )

    def draw(self, context):
        layout = self.layout

        # Active provider selector
        layout.prop(self, "ai_provider", expand=True)
        layout.separator()

        if self.ai_provider == 'hunyuan3d':
            self._draw_hunyuan(layout)
        elif self.ai_provider == 'hyperrodin':
            self._draw_hyperrodin(layout)
        else:
            self._draw_tripo(layout)

    def _draw_tripo(self, layout):
        box = layout.box()
        box.label(text="Tripo configuration", icon='OUTLINER_OB_MESH')
        box.prop(self, "tripo_api_key")

        row = box.row(align=True)
        row.operator(
            "blenderflow.open_tripo_signup",
            text="Get free API key",
            icon='URL',
        )
        row.operator(
            "blenderflow.test_provider_connection",
            text="Test connection",
            icon='PLAY',
        )

        if not self.tripo_api_key:
            box.label(
                text="No key set — AI Generate will prompt you to configure one.",
                icon='INFO',
            )

    def _draw_hunyuan(self, layout):
        box = layout.box()
        box.label(text="Hunyuan3D configuration", icon='OUTLINER_OB_MESH')
        box.prop(self, "hunyuan_api_type")
        box.prop(self, "hunyuan_secret_id")
        box.prop(self, "hunyuan_secret_key")

        row = box.row(align=True)
        row.operator(
            "blenderflow.open_hunyuan_console",
            text="Open Tencent Cloud console",
            icon='URL',
        )
        row.operator(
            "blenderflow.test_provider_connection",
            text="Test connection",
            icon='PLAY',
        )

        if not (self.hunyuan_secret_id and self.hunyuan_secret_key):
            box.label(
                text="SecretId/SecretKey missing — AI Generate will guide you.",
                icon='INFO',
            )

    def _draw_hyperrodin(self, layout):
        box = layout.box()
        box.label(text="Hyper3D Rodin configuration", icon='OUTLINER_OB_MESH')
        box.prop(self, "hyperrodin_api_key")

        row = box.row(align=True)
        row.operator(
            "blenderflow.use_rodin_free_trial",
            text="Use free trial key",
            icon='SOLO_ON',
        )
        row.operator(
            "blenderflow.open_hyperrodin_site",
            text="Get your own key",
            icon='URL',
        )
        row.operator(
            "blenderflow.test_provider_connection",
            text="Test connection",
            icon='PLAY',
        )

        if not self.hyperrodin_api_key:
            box.label(
                text="Empty = shared trial key (rate-limited). Get your own for sustained use.",
                icon='INFO',
            )


# ─── 注册 ───

_classes = (
    BlenderFlowPreferences,
    BLENDERFLOW_OT_start_server,
    BLENDERFLOW_OT_stop_server,
    BLENDERFLOW_PT_panel,
)


def register():
    # Reload submodules on every register() so disable+enable picks up
    # edits without a full Blender restart. Order matters: providers is
    # imported by commands, so reload providers *first* — otherwise
    # commands keeps a stale reference to the old providers module.
    importlib.reload(providers)
    importlib.reload(ws_server)
    importlib.reload(commands)
    for cls in _classes:
        try:
            bpy.utils.unregister_class(cls)
        except (RuntimeError, ValueError):
            pass
        bpy.utils.register_class(cls)
    commands.register()
    start_server()


def unregister():
    stop_server()
    commands.unregister()
    for cls in reversed(_classes):
        try:
            bpy.utils.unregister_class(cls)
        except (RuntimeError, ValueError):
            pass
