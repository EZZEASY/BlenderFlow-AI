"""WebSocket server for BlenderFlow AI Bridge.

Runs in a background thread. All bpy.ops calls are dispatched
to the main thread via bpy.app.timers.register().

Protocol: ws://localhost:9876
Messages: JSON, see design doc for schema.
"""

import asyncio
import json
from pathlib import Path
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

# Whitelists — reject anything else coming over the socket. The server binds
# to localhost only, but any local process can connect, so treat inputs as
# untrusted and enforce the protocol at the boundary.
_ALLOWED_MODES = {"OBJECT", "EDIT", "SCULPT", "POSE", "WEIGHT_PAINT", "VERTEX_PAINT", "TEXTURE_PAINT", "PARTICLE_EDIT"}
_ALLOWED_TOOL_OPS = {"extrude", "bevel", "loopcut", "subdivide"}
_ALLOWED_IMPORT_FORMATS = {"gltf", "glb", "obj", "fbx"}

# Only allow imports from this directory tree (matches AIService._tempDir on C# side).
_IMPORT_ROOT = (Path.home() / "BlenderFlow" / "temp").resolve()

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
        if mode not in _ALLOWED_MODES:
            return {"type": "error", "code": "invalid_mode", "message": f"Unsupported mode: {mode}"}
        _run_on_main_thread(lambda: _set_mode(mode))
        return None

    elif msg_type == "tool":
        op = msg.get("op", "")
        if op not in _ALLOWED_TOOL_OPS:
            return {"type": "error", "code": "invalid_tool", "message": f"Unsupported tool op: {op}"}
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
        if fmt not in _ALLOWED_IMPORT_FORMATS:
            return {"type": "error", "code": "invalid_format", "message": f"Unsupported import format: {fmt}"}
        safe_path = _validate_import_path(path)
        if safe_path is None:
            return {"type": "error", "code": "invalid_path", "message": "Path outside allowed import directory"}
        _run_on_main_thread(lambda p=safe_path, f=fmt: _import_model(p, f))
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


def _validate_import_path(path):
    """Reject anything outside the allowed import root; returns resolved str path, or None."""
    if not path or not isinstance(path, str):
        return None
    try:
        resolved = Path(path).expanduser().resolve()
    except (OSError, RuntimeError):
        return None

    try:
        resolved.relative_to(_IMPORT_ROOT)
    except ValueError:
        print(f"BlenderFlow: rejected import path outside {_IMPORT_ROOT}: {resolved}")
        return None

    if not resolved.is_file():
        print(f"BlenderFlow: rejected import path (not a file): {resolved}")
        return None

    return str(resolved)


def _import_model(path, fmt):
    """Import a 3D model file. Must run on main thread. Path is pre-validated."""
    try:
        if fmt in ("gltf", "glb") or path.endswith((".glb", ".gltf")):
            bpy.ops.import_scene.gltf(filepath=path)
        elif fmt == "obj" or path.endswith(".obj"):
            # Blender 4.x+
            bpy.ops.wm.obj_import(filepath=path)
        elif fmt == "fbx" or path.endswith(".fbx"):
            bpy.ops.import_scene.fbx(filepath=path)
        else:
            bpy.ops.import_scene.gltf(filepath=path)
        print(f"BlenderFlow: imported {path}")
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
_stop_event = None  # asyncio.Event, created on the server loop


def _get_loop():
    return _loop_ref


def request_stop():
    """Signal the running server to exit. Safe to call from any thread."""
    loop = _loop_ref
    event = _stop_event
    if loop is None or event is None:
        return
    try:
        loop.call_soon_threadsafe(event.set)
    except RuntimeError:
        # Loop already closed
        pass


async def run_server(loop):
    """Start the WebSocket server. Exits cleanly when request_stop() is called."""
    global _loop_ref, _stop_event
    _loop_ref = loop
    _stop_event = asyncio.Event()

    try:
        async with serve(handler, WS_HOST, WS_PORT):
            print(f"BlenderFlow: WebSocket server listening on ws://{WS_HOST}:{WS_PORT}")
            await _stop_event.wait()
            print("BlenderFlow: Stop signal received, shutting down server")
    except OSError as e:
        print(f"BlenderFlow: Could not start server: {e}")
    finally:
        # Close every client connection so the async-with exits promptly.
        for client in list(_clients):
            try:
                await client.close()
            except Exception:
                pass
        _clients.clear()
        _loop_ref = None
        _stop_event = None
