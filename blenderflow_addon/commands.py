"""Blender operators for BlenderFlow AI generation.

Exposes user-facing operators for the multi-provider AI generate flow:
  - prompt dialog (invoked from C# side on AI Generate keypress)
  - first-run setup dialog (shown when the selected provider isn't configured)
  - error dialog (shown when generation fails)
  - web links for each provider (signup / console)
  - provider connection test
  - jump to addon preferences
"""

import threading
import time
import traceback
import webbrowser

import bpy

from . import providers
from . import ws_server


# ─── Config readers ────────────────────────────────────────────────────


def _prefs(context):
    try:
        return context.preferences.addons[__package__].preferences
    except (KeyError, AttributeError):
        return None


def _provider_config(prefs) -> tuple[str, dict]:
    """Return (provider_name, config_dict) from addon preferences."""
    if prefs is None:
        # Hyper3D is the default because it ships with a shared trial key,
        # so it works out of the box even before anyone touches preferences.
        return 'hyperrodin', {'api_key': ''}

    if prefs.ai_provider == 'hunyuan3d':
        return 'hunyuan3d', {
            'secret_id': prefs.hunyuan_secret_id,
            'secret_key': prefs.hunyuan_secret_key,
            'api_type': prefs.hunyuan_api_type,
        }
    if prefs.ai_provider == 'hyperrodin':
        return 'hyperrodin', {'api_key': prefs.hyperrodin_api_key}
    return 'tripo', {'api_key': prefs.tripo_api_key}


def _provider_is_configured(prefs) -> bool:
    """Is the currently-selected provider ready to use?"""
    if prefs is None:
        return False
    if prefs.ai_provider == 'hunyuan3d':
        return bool(prefs.hunyuan_secret_id) and bool(prefs.hunyuan_secret_key)
    if prefs.ai_provider == 'hyperrodin':
        # Hyper3D always has a shared trial key as a fallback.
        return True
    return bool(prefs.tripo_api_key)


# ─── AI Generate — prompt dialog ───────────────────────────────────────


class BLENDERFLOW_OT_ai_prompt_dialog(bpy.types.Operator):
    """Ask the user for a text prompt and dispatch to the selected provider."""
    bl_idname = "blenderflow.ai_prompt_dialog"
    bl_label = "AI Generate 3D Model"

    prompt: bpy.props.StringProperty(
        name="",
        description="Describe the 3D model to generate",
        default="a modern wooden chair",
    )

    def execute(self, context):
        prefs = _prefs(context)
        provider_name, config = _provider_config(prefs)

        if provider_name == 'tripo':
            # Tripo still runs through the C# plugin (existing code path).
            ws_server._broadcast({
                "type": "ai_prompt_response",
                "prompt": self.prompt,
                "api_key": config.get("api_key", ""),
            })
            self.report({'INFO'}, f"BlenderFlow: Tripo generating '{self.prompt}'…")
            return {'FINISHED'}

        # Python-side providers run the full create/poll/import lifecycle in
        # a background thread and stream status updates back to C# over WS.
        try:
            provider = providers.build(provider_name, config)
        except providers.ProviderError as e:
            self.report({'ERROR'}, f"{e}")
            return {'CANCELLED'}

        _launch_generation(provider, self.prompt)
        self.report({'INFO'}, f"BlenderFlow: {provider_name} generating…")
        return {'FINISHED'}

    def invoke(self, context, event):
        # Blender 4.1+ lets us rename the OK button with confirm_text, which
        # bypasses the Chinese translation of "OK". Fall back gracefully on
        # older versions.
        try:
            return context.window_manager.invoke_props_dialog(
                self, width=420, confirm_text="Generate"
            )
        except TypeError:
            return context.window_manager.invoke_props_dialog(self, width=420)

    def draw(self, context):
        layout = self.layout
        col = layout.column(align=True)
        col.label(text="Describe your 3D model:")
        col.prop(self, "prompt", text="")

    def cancel(self, context):
        ws_server._broadcast({"type": "ai_prompt_cancelled"})


# ─── First-run guide ───────────────────────────────────────────────────


class BLENDERFLOW_OT_show_ai_setup(bpy.types.Operator):
    """Onboarding dialog — shown when the selected provider isn't configured."""
    bl_idname = "blenderflow.show_ai_setup"
    bl_label = "AI Generate needs credentials"

    def invoke(self, context, event):
        return context.window_manager.invoke_popup(self, width=500)

    def draw(self, context):
        prefs = _prefs(context)
        provider = prefs.ai_provider if prefs else 'hyperrodin'

        layout = self.layout
        col = layout.column(align=True)
        if provider == 'hunyuan3d':
            col.label(text="AI Generate needs Tencent Cloud credentials", icon='INFO')
            col.separator()
            col.label(text="Hunyuan3D is Tencent's 3D generation service — works from")
            col.label(text="mainland China without a VPN.")
            col.separator()
            col.label(text="Three steps:")
            col.label(text="   1. Open Tencent Cloud console and sign in.")
            col.label(text="   2. In CAM → API Keys, create SecretId + SecretKey.")
            col.label(text="   3. Paste both into BlenderFlow's addon preferences.")
            col.separator()

            row = layout.row(align=True)
            row.scale_y = 1.3
            row.operator("blenderflow.open_hunyuan_console", icon='URL',
                         text="Open Tencent Cloud")
            row.operator("blenderflow.open_addon_preferences", icon='PREFERENCES',
                         text="Open BlenderFlow Settings")
        elif provider == 'hyperrodin':
            # Hyper3D always has a trial key, so _provider_is_configured
            # returns True even for an empty field. Reaching this dialog
            # for Hyper3D means something else went wrong — be friendly.
            col.label(text="Hyper3D Rodin setup", icon='INFO')
            col.separator()
            col.label(text="Hyper3D ships with a shared free-trial key — it works")
            col.label(text="out of the box for a handful of generations per day.")
            col.label(text="For sustained use, sign up for your own key:")
            col.separator()

            row = layout.row(align=True)
            row.scale_y = 1.3
            row.operator("blenderflow.open_hyperrodin_site", icon='URL',
                         text="Open hyper3d.ai")
            row.operator("blenderflow.open_addon_preferences", icon='PREFERENCES',
                         text="Open BlenderFlow Settings")
        else:
            col.label(text="AI Generate needs a Tripo API key", icon='INFO')
            col.separator()
            col.label(text="To generate 3D models from text, add a free Tripo API key.")
            col.separator()
            col.label(text="Two steps:")
            col.label(text="   1. Sign up on tripo3d.ai and copy your API key.")
            col.label(text="   2. Paste it into BlenderFlow's addon preferences.")
            col.separator()

            row = layout.row(align=True)
            row.scale_y = 1.3
            row.operator("blenderflow.open_tripo_signup", icon='URL',
                         text="Open tripo3d.ai")
            row.operator("blenderflow.open_addon_preferences", icon='PREFERENCES',
                         text="Open BlenderFlow Settings")

    def execute(self, context):
        return {'FINISHED'}


# ─── Error dialog ──────────────────────────────────────────────────────


class BLENDERFLOW_OT_show_ai_error(bpy.types.Operator):
    """Show a user-friendly AI failure message."""
    bl_idname = "blenderflow.show_ai_error"
    bl_label = "AI Generation Failed"

    error_message: bpy.props.StringProperty()

    def invoke(self, context, event):
        return context.window_manager.invoke_popup(self, width=460)

    def draw(self, context):
        layout = self.layout
        col = layout.column(align=True)
        col.label(text="AI Generation Failed", icon='ERROR')
        col.separator()
        for chunk in _wrap(self.error_message, width=70):
            col.label(text=chunk)
        col.separator()
        row = layout.row(align=True)
        row.operator("blenderflow.open_addon_preferences", icon='PREFERENCES',
                     text="Open Settings")

    def execute(self, context):
        return {'FINISHED'}


def _wrap(text, width):
    words = (text or "").split()
    if not words:
        return [""]
    lines = [""]
    for w in words:
        if len(lines[-1]) + len(w) + 1 > width and lines[-1]:
            lines.append(w)
        else:
            lines[-1] = (lines[-1] + " " + w).strip()
    return lines


# ─── External site launchers ──────────────────────────────────────────


class BLENDERFLOW_OT_open_tripo_signup(bpy.types.Operator):
    """Open Tripo's website in the default browser."""
    bl_idname = "blenderflow.open_tripo_signup"
    bl_label = "Get Tripo API Key"

    def execute(self, context):
        webbrowser.open("https://www.tripo3d.ai/")
        self.report(
            {'INFO'},
            "Opened tripo3d.ai — sign up, then visit the API section to copy your key",
        )
        return {'FINISHED'}


class BLENDERFLOW_OT_open_hunyuan_console(bpy.types.Operator):
    """Open the Tencent Cloud CAM API-keys page."""
    bl_idname = "blenderflow.open_hunyuan_console"
    bl_label = "Open Tencent Cloud Console"

    def execute(self, context):
        webbrowser.open("https://console.cloud.tencent.com/cam/capi")
        self.report(
            {'INFO'},
            "Opened Tencent Cloud — sign in and copy your SecretId + SecretKey",
        )
        return {'FINISHED'}


class BLENDERFLOW_OT_open_hyperrodin_site(bpy.types.Operator):
    """Open Hyper3D's website to sign up for a personal API key."""
    bl_idname = "blenderflow.open_hyperrodin_site"
    bl_label = "Open Hyper3D Rodin"

    def execute(self, context):
        webbrowser.open("https://hyper3d.ai/")
        return {'FINISHED'}


class BLENDERFLOW_OT_use_rodin_free_trial(bpy.types.Operator):
    """Fill the Hyper3D API Key field with the shared free-trial key."""
    bl_idname = "blenderflow.use_rodin_free_trial"
    bl_label = "Use Rodin Free Trial Key"

    def execute(self, context):
        from . import providers
        prefs = _prefs(context)
        if prefs is not None:
            prefs.hyperrodin_api_key = providers.RODIN_FREE_TRIAL_KEY
            self.report(
                {'INFO'},
                "Filled the shared trial key — rate-limited; get your own for sustained use",
            )
        return {'FINISHED'}


# ─── Connection test (provider-dispatch) ──────────────────────────────


class BLENDERFLOW_OT_test_provider_connection(bpy.types.Operator):
    """Test the current provider's credentials against its API."""
    bl_idname = "blenderflow.test_provider_connection"
    bl_label = "Test AI Provider Connection"

    def execute(self, context):
        prefs = _prefs(context)
        provider_name, config = _provider_config(prefs)

        if provider_name == 'tripo':
            # Tripo test still Python-side — it's a lightweight ping.
            return self._test_tripo(config)

        try:
            provider = providers.build(provider_name, config)
        except providers.ProviderError as e:
            self.report({'ERROR'}, str(e))
            return {'CANCELLED'}

        ok, msg = provider.test_connection()
        self.report({'INFO' if ok else 'ERROR'}, msg)
        return {'FINISHED' if ok else 'CANCELLED'}

    def _test_tripo(self, config):
        import json as _json
        import urllib.error
        import urllib.request

        key = config.get('api_key', '')
        if not key:
            self.report({'ERROR'}, "API key is empty — paste your key first")
            return {'CANCELLED'}
        try:
            req = urllib.request.Request(
                "https://api.tripo3d.ai/v2/openapi/user/balance",
                headers={"Authorization": f"Bearer {key}"},
            )
            with urllib.request.urlopen(req, timeout=10) as r:
                if r.status == 200:
                    body = r.read().decode("utf-8", errors="replace")
                    try:
                        data = _json.loads(body)
                        balance = data.get("data", {}).get("balance", "?")
                        self.report({'INFO'}, f"✓ API key valid — balance: {balance}")
                    except (_json.JSONDecodeError, AttributeError):
                        self.report({'INFO'}, "✓ API key valid")
                    return {'FINISHED'}
        except urllib.error.HTTPError as e:
            if e.code in (401, 403):
                self.report({'ERROR'}, "Key rejected — check for typos")
            else:
                self.report({'ERROR'}, f"Tripo returned HTTP {e.code}")
            return {'CANCELLED'}
        except Exception as e:
            self.report({'ERROR'}, f"Network error: {e}")
            return {'CANCELLED'}
        return {'CANCELLED'}


# ─── Jump to addon preferences ────────────────────────────────────────


class BLENDERFLOW_OT_open_addon_preferences(bpy.types.Operator):
    """Open Blender Preferences on the Add-ons tab, filtered to BlenderFlow."""
    bl_idname = "blenderflow.open_addon_preferences"
    bl_label = "Open BlenderFlow Preferences"

    def execute(self, context):
        bpy.ops.screen.userpref_show('INVOKE_DEFAULT')
        context.preferences.active_section = 'ADDONS'
        try:
            context.window_manager.addon_search = "BlenderFlow"
        except (AttributeError, TypeError):
            pass
        return {'FINISHED'}


# ─── Background generation loop ───────────────────────────────────────


# Module-level state driving the bottom status-bar ticker. Background thread
# writes these; the main-thread timer reads them. Simple scalar assignment is
# safe across threads under the GIL.
_ai_active = False
_ai_started_at = 0.0
_ai_state = "idle"
_ai_progress = 0.0
_ai_provider_name = ""
_status_timer_registered = False


def _begin_status(provider_name: str) -> None:
    global _ai_active, _ai_started_at, _ai_state, _ai_progress, _ai_provider_name
    _ai_provider_name = provider_name
    _ai_started_at = time.time()
    _ai_state = "submitting"
    _ai_progress = 0.0
    _ai_active = True
    _ensure_status_timer()


def _update_status(state: str, progress: float) -> None:
    global _ai_state, _ai_progress
    _ai_state = state
    _ai_progress = progress


def _end_status() -> None:
    global _ai_active
    _ai_active = False


def _ensure_status_timer() -> None:
    """Register the main-thread ticker exactly once."""
    global _status_timer_registered
    if _status_timer_registered:
        return
    _status_timer_registered = True

    def register_in_main():
        bpy.app.timers.register(_status_tick, first_interval=0.0)
        return None

    # timers.register is thread-safe, but scheduling via a one-shot timer
    # keeps all bpy.* access on the main thread for sanity.
    bpy.app.timers.register(register_in_main, first_interval=0.0)


def _status_tick():
    """Main-thread: refresh the bottom status bar once per second."""
    global _status_timer_registered
    if not _ai_active:
        _clear_status_bar()
        _status_timer_registered = False
        return None  # stop the timer

    elapsed = int(time.time() - _ai_started_at)
    pct = int(round(_ai_progress * 100))
    msg = (
        f"BlenderFlow AI [{_ai_provider_name}] — "
        f"{_ai_state}  {pct}%  ({elapsed}s)"
    )
    _set_status_bar(msg)
    return 1.0


def _set_status_bar(msg):
    try:
        wm = bpy.context.window_manager
        if not wm:
            return
        for w in wm.windows:
            if w.workspace:
                w.workspace.status_text_set(msg)
    except Exception:
        pass


def _clear_status_bar():
    _set_status_bar(None)


def _launch_generation(provider: providers.AIProvider, prompt: str) -> None:
    """Fire off a worker thread that runs the full create/poll/import cycle."""
    _begin_status(provider.name)

    def run():
        try:
            handle = provider.generate(prompt)
            _update_status("submitted", 0.05)
            ws_server._broadcast({
                "type": "ai_progress",
                "state": "submitted",
                "progress": 0.05,
            })
        except providers.ProviderError as e:
            _fail(str(e))
            return
        except Exception as e:
            _fail(f"Unexpected error while submitting: {e}")
            traceback.print_exc()
            return

        poll_interval = 3.0
        poll_ceiling_s = 300.0  # 5 minutes of polling — more than long enough
        started = time.time()

        while True:
            if time.time() - started > poll_ceiling_s:
                _fail("Generation timed out after 5 minutes.")
                return
            time.sleep(poll_interval)

            try:
                status = provider.poll(handle)
            except providers.ProviderError as e:
                _fail(str(e))
                return
            except Exception as e:
                _fail(f"Poll failed: {e}")
                traceback.print_exc()
                return

            state = status.get("state", "running")
            progress = float(status.get("progress", 0) or 0)
            _update_status(state, progress)
            ws_server._broadcast({
                "type": "ai_progress",
                "state": state,
                "progress": progress,
            })

            if state == "done":
                _import_on_main_thread(provider, handle, prompt)
                return
            if state == "failed":
                _fail(status.get("error") or "Provider reported failure")
                return

    thread = threading.Thread(target=run, daemon=True, name="BlenderFlow-AI-Gen")
    thread.start()


def _import_on_main_thread(provider, handle, prompt):
    """Schedule the bpy.ops import on Blender's main thread."""
    _update_status("downloading", 0.95)

    def do_import():
        try:
            name = _safe_object_name(prompt)
            imported = provider.download_and_import(handle, name)
            _update_status("done", 1.0)
            _end_status()
            ws_server._broadcast({
                "type": "ai_completed",
                "object_name": imported,
            })
        except providers.ProviderError as e:
            _fail(str(e))
        except Exception as e:
            _fail(f"Import failed: {e}")
            traceback.print_exc()
        return None  # unregister timer

    bpy.app.timers.register(do_import, first_interval=0)


def _safe_object_name(prompt: str) -> str:
    clean = re.sub(r"[^A-Za-z0-9_-]+", "_", (prompt or "AIModel"))[:24].strip("_")
    return clean or "AIModel"


def _fail(msg: str) -> None:
    _update_status("failed", _ai_progress)
    _end_status()
    ws_server._broadcast({"type": "ai_failed", "error": msg})
    # Surface the failure to the user directly too — the C# path will also
    # forward this, but broadcasting twice is harmless and guards against
    # the plugin being disconnected mid-generation.
    def show():
        try:
            bpy.ops.blenderflow.show_ai_error('INVOKE_DEFAULT', error_message=msg)
        except Exception:
            pass
        return None
    bpy.app.timers.register(show, first_interval=0)


# re module imported locally to keep the module-level import list narrow.
import re  # noqa: E402


# ─── Registration ──────────────────────────────────────────────────────


_classes = (
    BLENDERFLOW_OT_ai_prompt_dialog,
    BLENDERFLOW_OT_show_ai_setup,
    BLENDERFLOW_OT_show_ai_error,
    BLENDERFLOW_OT_open_tripo_signup,
    BLENDERFLOW_OT_open_hunyuan_console,
    BLENDERFLOW_OT_open_hyperrodin_site,
    BLENDERFLOW_OT_use_rodin_free_trial,
    BLENDERFLOW_OT_test_provider_connection,
    BLENDERFLOW_OT_open_addon_preferences,
)


def register():
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
