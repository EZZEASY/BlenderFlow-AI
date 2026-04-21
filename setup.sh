#!/bin/bash
# BlenderFlow AI — one-shot installer.
# Builds the C# plugin and links the Blender addon into their respective
# app-data directories. Python deps are installed on demand by the addon
# the first time it starts inside Blender.

set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
ADDON_NAME="blenderflow_addon"

echo "========================================="
echo "  BlenderFlow AI installer"
echo "========================================="
echo ""

# ─── Build the C# plugin ───
echo "▶ Building BlenderFlow plugin..."
export DOTNET_ROOT="/opt/homebrew/opt/dotnet@8/libexec"
export PATH="$DOTNET_ROOT:$HOME/.dotnet/tools:$PATH"

if ! command -v dotnet &>/dev/null; then
    echo "❌ .NET SDK not found. Install it first: brew install dotnet@8"
    exit 1
fi

cd "$SCRIPT_DIR/BlenderFlowPlugin"
dotnet build --configuration Debug --verbosity quiet
echo "  ✅ Plugin built"

# ─── Register with Logi Plugin Service ───
echo ""
echo "▶ Registering plugin with Logi Plugin Service..."
PLUGIN_DIR="$HOME/Library/Application Support/Logi/LogiPluginService/Plugins"
mkdir -p "$PLUGIN_DIR"
echo "$SCRIPT_DIR/BlenderFlowPlugin/bin/Debug/" > "$PLUGIN_DIR/BlenderFlowPlugin.link"
echo "  ✅ Plugin registered"

# ─── Link the Blender addon ───
echo ""
echo "▶ Installing Blender addon..."
BLENDER_APP="/Applications/Blender.app"
if [ ! -d "$BLENDER_APP" ]; then
    echo "❌ Blender.app not found. Install it from blender.org"
    exit 1
fi

BLENDER_VERSION=$(ls "$BLENDER_APP/Contents/Resources/" | grep -E '^[0-9]+\.' | head -1)
ADDON_DST="$HOME/Library/Application Support/Blender/$BLENDER_VERSION/scripts/addons/$ADDON_NAME"

mkdir -p "$(dirname "$ADDON_DST")"
rm -rf "$ADDON_DST" 2>/dev/null
ln -sf "$SCRIPT_DIR/$ADDON_NAME" "$ADDON_DST"
echo "  ✅ Addon symlinked (dev mode — reload the addon in Blender to pick up edits)"

# ─── Reload Plugin Service ───
echo ""
echo "▶ Reloading Logi Plugin Service..."
open "loupedeck:plugin/BlenderFlow/reload" 2>/dev/null || true
echo "  ✅ Reload signal sent"

# ─── Done ───
echo ""
echo "========================================="
echo "  ✅ Install complete"
echo "========================================="
echo ""
echo "Next steps:"
echo "  1. Open Blender → Edit → Preferences → Add-ons"
echo "     Search 'BlenderFlow' → enable the checkbox"
echo "     (first enable installs Python deps; give it a few seconds)"
echo ""
echo "  2. Open Logi Options+ → device customization → All Actions"
echo "     Find BlenderFlow AI → drag actions onto keys"
echo ""
echo "  3. For AI 3D generation: configure a provider in the addon's"
echo "     preferences (BlenderFlow ships with Hyper3D Rodin enabled by"
echo "     default, using a shared free-trial key so it works out of the"
echo "     box)."
echo ""
