# BlenderFlow AI

A Logitech MX Creative Console plugin that turns the console into a Blender
control surface — mode switching, mesh editing, viewport navigation, and
one-press AI 3D generation.

## What it does

BlenderFlow AI maps the MX Creative Console's keys and dials to the Blender
workflow. A key taps through modes, another bevels the selection, a dial
orbits the viewport — and one key runs a text-to-3D model into your scene.

Built-in actions:

- **Modes**: Object, Edit, Sculpt, Texture Paint, plus a single **Mode
  Cycle** key that rotates through all four with a color-coded icon.
- **Mesh tools**: Extrude, Bevel, Loop Cut.
- **Viewport**: Orbit and Zoom dials (smooth, non-discrete), Shading Pie,
  Operator Search, Quick Favorites, direct views for Front / Right / Top /
  Camera.
- **Brush**: Size and Strength dials (sculpt + paint).
- **Timeline**: Frame Scrub (press dial to play/pause).
- **File**: Undo, Redo, Save, Render Image.
- **AI**: one-press text-to-3D — pick a provider, type a prompt, get a
  model imported into the scene.

## Architecture

```
   MX Creative Console
          │
          ▼
   Logi Plugin Service  (C# plugin — BlenderFlowPlugin/)
          │
          │  WebSocket on ws://localhost:9876
          ▼
   Blender addon        (Python — blenderflow_addon/)
          │
          ▼
        bpy.ops
```

The C# half runs inside Logi Options+'s plugin host and renders icons on
the console. The Python half runs as a regular Blender addon and executes
Blender operators. All routing of shortcuts (undo, save, mode switch, orbit)
flows over the WebSocket rather than synthetic keystrokes — macOS strips
modifier keys off synthetic events, so Cmd+Z sent as a keystroke never
reached Blender.

## Requirements

- **macOS (recommended)** — the only platform actively tested. Windows
  builds and most actions have been wired up to be cross-platform but are
  not routinely exercised; if you're on Windows, expect rough edges and
  please file issues.
- [Blender](https://www.blender.org/) 4.1 or newer
- [Logi Options+](https://www.logitech.com/software/logi-options-plus.html)
  with the Logi Plugin Service
- [.NET 8 SDK](https://dotnet.microsoft.com/)
  - macOS: `brew install dotnet@8`
  - Windows: installer from dotnet.microsoft.com
- A paired Logitech MX Creative Console

## Install

```bash
git clone https://github.com/EZZEASY/BlenderFlow-AI.git
cd BlenderFlow-AI
```

Then run the installer for your platform:

**macOS:**
```bash
bash setup.sh
```

**Windows** (PowerShell, with Developer Mode enabled under
Settings → Privacy & Security → For developers — needed for the
symlink step):
```powershell
powershell -ExecutionPolicy Bypass -File .\setup.ps1
```

Either installer does three things:

1. Builds the C# plugin with `dotnet build`
2. Registers it with Logi Plugin Service via a `.link` file (dev-mode
   install — code changes are picked up on the next rebuild without
   reinstalling)
3. Symlinks the addon into Blender's `scripts/addons/` directory

After the script finishes:

1. **Blender** → Preferences → Add-ons → search `BlenderFlow` → enable.
   The first enable installs the addon's Python deps, which takes a few
   seconds.
2. **Logi Options+** → device customization → Actions → `BlenderFlow AI`
   → drag actions onto console keys.

## AI 3D generation

Three providers, picked in the addon's preferences:

| Provider | Setup | Notes |
|---|---|---|
| **Hyper3D Rodin** (default) | Works out of the box with a shared free-trial key. | [hyper3d.ai](https://hyper3d.ai/) for your own key — the shared key is rate-limited across all users. |
| **Hunyuan3D** (Tencent Cloud) | Needs a SecretId + SecretKey from [console.cloud.tencent.com](https://console.cloud.tencent.com/cam/capi). | Reachable from mainland China without a VPN. |
| **Tripo** | API key from [tripo3d.ai](https://www.tripo3d.ai/). | May require a VPN from some regions. |

Each provider has a "Test connection" button in the preferences that pings
the actual API so credential problems surface before you hit Generate.

## Development

After the initial `setup.sh`:

- **Edit Python addon** (`commands.py`, `providers.py`, `ws_server.py`) →
  disable + re-enable the addon in Blender. Its `register()` calls
  `importlib.reload` on those submodules, so the new code is picked up
  without a restart.
- **Edit Python addon `__init__.py`** → full Blender restart (or
  F3 → Reload Scripts). Top-level module code is not reloaded by
  `importlib.reload`.
- **Edit C# plugin** → rerun `setup.sh` (or just `dotnet build` inside
  `BlenderFlowPlugin/`), then restart Logi Options+ so the plugin host
  picks up the new DLLs.

## Layout

```
BlenderFlowPlugin/        C# plugin loaded by Logi Plugin Service
  src/Actions/            Command (keys) and Adjustment (dials) classes
  src/Services/           WebSocket client, AI service
  src/Helpers/            Icon drawing, theme colors
  src/package/            Loupedeck package metadata

blenderflow_addon/        Blender addon (Python)
  __init__.py             bl_info, preferences, register/unregister
  ws_server.py            WebSocket server, Blender-side handlers
  commands.py             Operator definitions (dialogs, setup, etc.)
  providers.py            AI-provider implementations
  vendor/                 Bundled pure-Python deps (websockets, etc.)
```

## Credits

- Hyper3D Rodin free-trial key and the Tencent TC3-HMAC-SHA256 signature
  code are adapted from
  [blender-mcp](https://github.com/ahujasid/blender-mcp) by Siddharth Ahuja
  (MIT).
