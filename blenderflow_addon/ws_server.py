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
        # Hop to Blender's main thread, capture state, signal via asyncio.Event
        # (no polling race, single-writer for the shared dict).
        loop = asyncio.get_event_loop()
        done = asyncio.Event()
        result = {}

        def read_state():
            try:
                result.update(_get_state_safe())
            finally:
                loop.call_soon_threadsafe(done.set)
            return None

        _run_on_main_thread(read_state)

        try:
            await asyncio.wait_for(done.wait(), timeout=0.5)
            return {"type": "state", **result}
        except asyncio.TimeoutError:
            # Main thread is busy — return a best-effort snapshot tagged as stale.
            return {"type": "state", "stale": True, **_get_state_safe()}

    elif msg_type == "ai_prompt_request":
        _run_on_main_thread(lambda: _show_ai_prompt(websocket))
        return None

    elif msg_type == "ai_error":
        error = msg.get("error", "Unknown error")
        if not isinstance(error, str):
            error = str(error)
        # Clamp so a malicious payload can't hang Blender rendering the dialog.
        error = error[:1000]
        _run_on_main_thread(lambda e=error: _show_ai_error(e))
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

    elif msg_type == "brush_strength":
        delta = _parse_float(msg.get("delta"), -1.0, 1.0)
        value = _parse_float(msg.get("value"), 0.0, 2.0)
        if delta is None and value is None:
            return {"type": "error", "code": "invalid_args", "message": "brush_strength needs delta or value"}
        _run_on_main_thread(lambda d=delta, v=value: _set_brush_strength(d, v))
        return None

    # Keystroke-equivalents that can't be delivered reliably via OS key
    # injection on macOS (Blender's GHOST window layer filters modifier
    # flags off synthetic events). Route them through bpy.ops directly.
    elif msg_type == "undo":
        _run_on_main_thread(_do_undo)
        return None

    elif msg_type == "redo":
        _run_on_main_thread(_do_redo)
        return None

    elif msg_type == "save":
        _run_on_main_thread(_do_save)
        return None

    elif msg_type == "render_image":
        _run_on_main_thread(_do_render_image)
        return None

    elif msg_type == "viewport_orbit":
        delta_deg = _parse_float(msg.get("delta_deg"), -30.0, 30.0)
        if delta_deg is None:
            return {"type": "error", "code": "invalid_args", "message": "viewport_orbit needs delta_deg"}
        _run_on_main_thread(lambda d=delta_deg: _viewport_orbit(d))
        return None

    elif msg_type == "viewport_zoom":
        factor = _parse_float(msg.get("factor"), 0.5, 2.0)
        if factor is None or factor <= 0:
            return {"type": "error", "code": "invalid_args", "message": "viewport_zoom needs factor in (0.5, 2.0]"}
        _run_on_main_thread(lambda f=factor: _viewport_zoom(f))
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


_MODE_COMPATIBLE_TYPES = {
    "EDIT":          {"MESH", "CURVE", "SURFACE", "META", "FONT", "LATTICE", "ARMATURE"},
    "SCULPT":        {"MESH"},
    "TEXTURE_PAINT": {"MESH"},
    "VERTEX_PAINT":  {"MESH"},
    "WEIGHT_PAINT":  {"MESH"},
    "POSE":          {"ARMATURE"},
    "PARTICLE_EDIT": {"MESH"},
    "OBJECT":        None,  # always allowed
}


def _set_mode(mode):
    """Switch Blender mode. Must run on main thread.

    If no object is active or the active object's type is incompatible with
    the target mode, try to pick a suitable object instead of silently
    dropping the request — the most common "I pressed the key and nothing
    happened" failure is an empty selection.
    """
    try:
        obj = bpy.context.view_layer.objects.active
        compatible = _MODE_COMPATIBLE_TYPES.get(mode)

        # If current active object won't accept this mode, try another.
        if obj is None or (compatible is not None and obj.type not in compatible):
            picked = None
            if compatible:
                for o in bpy.context.view_layer.objects:
                    if o.type in compatible and o.visible_get():
                        picked = o
                        break
            if picked is None:
                why = "no object in scene" if obj is None else f"active is {obj.type}"
                _status_flash(f"BlenderFlow: can't switch to {mode} — {why}")
                print(f"BlenderFlow: set_mode({mode}) skipped — {why}")
                return None
            bpy.context.view_layer.objects.active = picked
            try:
                picked.select_set(True)
            except Exception:
                pass
            obj = picked

        bpy.ops.object.mode_set(mode=mode)
        _broadcast_mode_change(mode)
    except Exception as e:
        _status_flash(f"BlenderFlow: mode_set({mode}) failed — {e}")
        print(f"BlenderFlow: mode_set error: {e}")
    return None


def _status_flash(msg: str, hold_seconds: float = 3.0):
    """Show `msg` in the bottom status bar for a few seconds, then clear.

    Gives invisible-failure modes (no active object, incompatible type) a
    user-visible breadcrumb without needing the system terminal.
    """
    try:
        wm = bpy.context.window_manager
        if not wm:
            return
        for w in wm.windows:
            if w.workspace:
                w.workspace.status_text_set(msg)

        def clear():
            try:
                for w in bpy.context.window_manager.windows:
                    if w.workspace:
                        w.workspace.status_text_set(None)
            except Exception:
                pass
            return None

        bpy.app.timers.register(clear, first_interval=hold_seconds)
    except Exception:
        pass


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


def _parse_float(raw, lo, hi):
    """Coerce an incoming JSON number to a float within [lo, hi], or None on reject."""
    if raw is None:
        return None
    try:
        v = float(raw)
    except (TypeError, ValueError):
        return None
    if v != v:  # NaN
        return None
    return max(lo, min(hi, v))


def _set_brush_strength(delta, value):
    """Adjust the active paint/sculpt brush's strength. Main thread."""
    try:
        mode = bpy.context.mode
        ts = bpy.context.tool_settings
        brush = None
        if mode == "SCULPT" and ts.sculpt:
            brush = ts.sculpt.brush
        elif mode == "PAINT_TEXTURE" and ts.image_paint:
            brush = ts.image_paint.brush
        elif mode == "PAINT_VERTEX" and ts.vertex_paint:
            brush = ts.vertex_paint.brush
        elif mode == "PAINT_WEIGHT" and ts.weight_paint:
            brush = ts.weight_paint.brush
        if brush is None:
            return None
        if value is not None:
            brush.strength = max(0.0, min(2.0, value))
        elif delta is not None:
            brush.strength = max(0.0, min(2.0, brush.strength + delta))
    except Exception as e:
        print(f"BlenderFlow: brush_strength error: {e}")
    return None


def _do_undo():
    try:
        bpy.ops.ed.undo()
    except Exception as e:
        print(f"BlenderFlow: undo error: {e}")
    return None


def _do_redo():
    try:
        bpy.ops.ed.redo()
    except Exception as e:
        print(f"BlenderFlow: redo error: {e}")
    return None


def _do_save():
    try:
        # If the file has never been saved, save_mainfile() raises because
        # there's no path — fall back to Save As dialog in that case.
        if bpy.data.filepath:
            bpy.ops.wm.save_mainfile()
        else:
            bpy.ops.wm.save_mainfile('INVOKE_DEFAULT')
    except Exception as e:
        print(f"BlenderFlow: save error: {e}")
    return None


def _do_render_image():
    try:
        bpy.ops.render.render('INVOKE_DEFAULT')
    except Exception as e:
        print(f"BlenderFlow: render error: {e}")
    return None


def _find_view3d_rv3d():
    """Return the first VIEW_3D area's region_3d, or None if not found."""
    for window in bpy.context.window_manager.windows:
        for area in window.screen.areas:
            if area.type != "VIEW_3D":
                continue
            for space in area.spaces:
                if space.type == "VIEW_3D" and space.region_3d is not None:
                    return area, space.region_3d
    return None, None


def _viewport_orbit(delta_deg):
    """Rotate the 3D viewport around world Z (turntable) by delta_deg degrees."""
    try:
        import math
        from mathutils import Quaternion
        area, rv3d = _find_view3d_rv3d()
        if rv3d is None:
            return None
        angle = math.radians(delta_deg)
        rotation = Quaternion((0.0, 0.0, 1.0), angle)
        rv3d.view_rotation = rotation @ rv3d.view_rotation
        area.tag_redraw()
    except Exception as e:
        print(f"BlenderFlow: viewport_orbit error: {e}")
    return None


def _viewport_zoom(factor):
    """Zoom the 3D viewport by dividing view_distance by factor (>1 = zoom in)."""
    try:
        area, rv3d = _find_view3d_rv3d()
        if rv3d is None:
            return None
        new_dist = rv3d.view_distance / factor
        rv3d.view_distance = max(0.01, min(1000.0, new_dist))
        area.tag_redraw()
    except Exception as e:
        print(f"BlenderFlow: viewport_zoom error: {e}")
    return None


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
    """Entry point from C# on AI-Generate keypress. Checks that the selected
    provider is configured and redirects to the setup dialog otherwise — the
    user should never see a silent failure on the console key.
    """
    try:
        prefs = bpy.context.preferences.addons["blenderflow_addon"].preferences
        provider = getattr(prefs, "ai_provider", "hyperrodin")
        if provider == "hunyuan3d":
            configured = bool(
                getattr(prefs, "hunyuan_secret_id", "")
                and getattr(prefs, "hunyuan_secret_key", "")
            )
        elif provider == "hyperrodin":
            # Hyper3D has a shared free-trial fallback, so it's always
            # configured enough to attempt a generation.
            configured = True
        else:  # tripo
            configured = bool(getattr(prefs, "tripo_api_key", ""))
    except (KeyError, AttributeError) as e:
        print(f"BlenderFlow: cannot read addon preferences: {e}")
        configured = False

    if not configured:
        bpy.ops.blenderflow.show_ai_setup('INVOKE_DEFAULT')
        # Tell C# no prompt is coming so it resets its 'waiting' state.
        _broadcast({"type": "ai_prompt_cancelled"})
    else:
        bpy.ops.blenderflow.ai_prompt_dialog('INVOKE_DEFAULT')
    return None


def _show_ai_error(message):
    """Pop up a user-friendly error dialog from the main thread."""
    try:
        bpy.ops.blenderflow.show_ai_error('INVOKE_DEFAULT', error_message=message)
    except Exception as e:
        print(f"BlenderFlow: could not show error dialog: {e}")
    return None


def _broadcast_mode_change(mode):
    """Broadcast mode change to all connected clients."""
    _broadcast({"type": "mode_changed", "mode": mode})


def _broadcast_error(code, message):
    """Broadcast error to all connected clients."""
    _broadcast({"type": "error", "code": code, "message": message})


async def _send_with_timeout(client, data):
    """Send to a single client with a 1s ceiling; drop the client on timeout/error."""
    try:
        await asyncio.wait_for(client.send(data), timeout=1.0)
    except (asyncio.TimeoutError, Exception) as e:
        print(f"BlenderFlow: dropping slow/broken client: {e}")
        _clients.discard(client)
        try:
            await client.close()
        except Exception:
            pass


def _broadcast(msg):
    """Send a message to all connected WebSocket clients.

    A slow or wedged client must not block siblings, so we schedule an
    independent fire-and-forget coroutine per client.
    """
    data = json.dumps(msg)
    loop = _get_loop()
    if loop is None:
        return
    for client in list(_clients):
        try:
            asyncio.run_coroutine_threadsafe(_send_with_timeout(client, data), loop)
        except Exception as e:
            print(f"BlenderFlow: broadcast schedule error: {e}")


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
