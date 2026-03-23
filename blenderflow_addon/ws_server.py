"""WebSocket server for BlenderFlow AI Bridge.

Runs in a background thread. All bpy.ops calls are dispatched
to the main thread via bpy.app.timers.register().

Protocol: ws://localhost:9876
Messages: JSON, see design doc for schema.
"""

import asyncio
import json
import os
import bpy

try:
    import websockets
    from websockets.asyncio.server import serve
except ImportError:
    # Fallback for older websockets versions
    import websockets
    serve = websockets.serve

WS_HOST = "localhost"
WS_PORT = 9876

# Connected clients
_clients = set()

# Pending responses to send back (thread-safe queue via asyncio)
_response_queue = asyncio.Queue()


async def handler(websocket):
    """Handle incoming WebSocket connections."""
    _clients.add(websocket)
    print(f"BlenderFlow: Client connected ({len(_clients)} total)")
    try:
        async for raw_message in websocket:
            try:
                msg = json.loads(raw_message)
            except json.JSONDecodeError:
                await websocket.send(json.dumps({
                    "type": "error",
                    "code": "invalid_json",
                    "message": "Could not parse JSON"
                }))
                continue

            msg_type = msg.get("type", "")
            response = await handle_message(msg_type, msg, websocket)
            if response:
                await websocket.send(json.dumps(response))

    except websockets.exceptions.ConnectionClosed:
        pass
    finally:
        _clients.discard(websocket)
        print(f"BlenderFlow: Client disconnected ({len(_clients)} total)")


async def handle_message(msg_type, msg, websocket):
    """Route messages to handlers. Returns response dict or None."""

    if msg_type == "set_mode":
        mode = msg.get("mode", "OBJECT")
        _run_on_main_thread(lambda: _set_mode(mode))
        return None

    elif msg_type == "tool":
        op = msg.get("op", "")
        _run_on_main_thread(lambda op=op: _run_tool(op))
        return None

    elif msg_type == "get_state":
        # Read state on main thread via shared dict + sleep
        result = {"_done": False}

        def read_state():
            result.update(_get_state())
            result["_done"] = True
            return None

        _run_on_main_thread(read_state)

        # Wait for main thread timer to fire (up to 0.5s)
        for _ in range(10):
            await asyncio.sleep(0.05)
            if result.get("_done"):
                break

        result.pop("_done", None)
        if not result.get("mode"):
            result = _get_state_safe()
        return {"type": "state", **result}

    elif msg_type == "ai_prompt_request":
        _run_on_main_thread(lambda: _show_ai_prompt(websocket))
        return None

    elif msg_type == "import_model":
        path = msg.get("path", "")
        fmt = msg.get("format", "gltf")
        _run_on_main_thread(lambda: _import_model(path, fmt))
        return None

    else:
        return {
            "type": "error",
            "code": "unknown_type",
            "message": f"Unknown message type: {msg_type}"
        }


def _run_on_main_thread(func):
    """Schedule a function to run on Blender's main thread."""
    bpy.app.timers.register(func, first_interval=0)


def _set_mode(mode):
    """Switch Blender mode. Must run on main thread."""
    try:
        if bpy.context.active_object:
            bpy.ops.object.mode_set(mode=mode)
            _broadcast_mode_change(mode)
    except Exception as e:
        print(f"BlenderFlow: mode_set error: {e}")
    return None


def _run_tool(op):
    """Execute a mesh tool. Must run on main thread."""
    tool_map = {
        "extrude": lambda: bpy.ops.mesh.extrude_region_move(),
        "bevel": lambda: bpy.ops.mesh.bevel('INVOKE_DEFAULT'),
        "loopcut": lambda: bpy.ops.mesh.loopcut_slide('INVOKE_DEFAULT'),
        "subdivide": lambda: bpy.ops.mesh.subdivide(),
    }
    func = tool_map.get(op)
    if func:
        try:
            func()
        except Exception as e:
            print(f"BlenderFlow: tool {op} error: {e}")
    return None


def _get_state_safe():
    """Get Blender state. Safe to call from any context."""
    try:
        return _get_state()
    except Exception:
        return {
            "mode": "UNKNOWN",
            "active_object": None,
            "vertex_count": 0,
            "face_count": 0,
        }


def _get_state():
    """Get current Blender state."""
    obj = bpy.context.active_object
    state = {
        "mode": bpy.context.mode if hasattr(bpy.context, 'mode') else "UNKNOWN",
        "active_object": obj.name if obj else None,
        "vertex_count": 0,
        "face_count": 0,
    }
    if obj and obj.type == 'MESH' and obj.data:
        state["vertex_count"] = len(obj.data.vertices)
        state["face_count"] = len(obj.data.polygons)
    return state


def _import_model(path, fmt):
    """Import a 3D model file. Must run on main thread."""
    expanded = os.path.expanduser(path)
    if not os.path.exists(expanded):
        print(f"BlenderFlow: import file not found: {expanded}")
        _broadcast_error("import_failed", f"File not found: {expanded}")
        return None

    try:
        if fmt == "gltf" or expanded.endswith((".glb", ".gltf")):
            bpy.ops.import_scene.gltf(filepath=expanded)
        elif fmt == "obj" or expanded.endswith(".obj"):
            # Blender 4.x+
            bpy.ops.wm.obj_import(filepath=expanded)
        elif expanded.endswith(".fbx"):
            bpy.ops.import_scene.fbx(filepath=expanded)
        else:
            bpy.ops.import_scene.gltf(filepath=expanded)
        print(f"BlenderFlow: imported {expanded}")
    except Exception as e:
        print(f"BlenderFlow: import error: {e}")
        _broadcast_error("import_failed", str(e))
    return None


def _show_ai_prompt(websocket):
    """Show AI prompt dialog in Blender. Must run on main thread."""
    bpy.ops.blenderflow.ai_prompt_dialog('INVOKE_DEFAULT')
    return None


def _broadcast_mode_change(mode):
    """Broadcast mode change to all connected clients."""
    _broadcast({"type": "mode_changed", "mode": mode})


def _broadcast_error(code, message):
    """Broadcast error to all connected clients."""
    _broadcast({"type": "error", "code": code, "message": message})


def _broadcast(msg):
    """Send a message to all connected WebSocket clients."""
    data = json.dumps(msg)
    for client in list(_clients):
        try:
            asyncio.run_coroutine_threadsafe(
                client.send(data),
                _get_loop()
            )
        except Exception:
            pass


_loop_ref = None


def _get_loop():
    return _loop_ref


async def run_server(loop):
    """Start the WebSocket server."""
    global _loop_ref
    _loop_ref = loop

    try:
        async with serve(handler, WS_HOST, WS_PORT):
            print(f"BlenderFlow: WebSocket server listening on ws://{WS_HOST}:{WS_PORT}")
            await asyncio.Future()  # run forever
    except OSError as e:
        print(f"BlenderFlow: Could not start server: {e}")
