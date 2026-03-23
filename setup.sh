#!/bin/bash
# BlenderFlow AI — 一键安装脚本
# 编译 C# 插件 + 链接 Blender Addon
# Python 依赖由 Addon 在 Blender 里自动安装

set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
ADDON_NAME="blenderflow_addon"

echo "========================================="
echo "  BlenderFlow AI 安装脚本"
echo "========================================="
echo ""

# ─── 编译 C# 插件 ───
echo "▶ 编译 BlenderFlow 插件..."
export DOTNET_ROOT="/opt/homebrew/opt/dotnet@8/libexec"
export PATH="$DOTNET_ROOT:$HOME/.dotnet/tools:$PATH"

if ! command -v dotnet &>/dev/null; then
    echo "❌ 未找到 .NET SDK，请先运行: brew install dotnet@8"
    exit 1
fi

cd "$SCRIPT_DIR/BlenderFlowPlugin"
dotnet build --configuration Debug --verbosity quiet
echo "  ✅ 插件编译成功"

# ─── 注册到 Plugin Service ───
echo ""
echo "▶ 注册插件到 Logi Plugin Service..."
PLUGIN_DIR="$HOME/Library/Application Support/Logi/LogiPluginService/Plugins"
mkdir -p "$PLUGIN_DIR"
echo "$SCRIPT_DIR/BlenderFlowPlugin/bin/Debug/" > "$PLUGIN_DIR/BlenderFlowPlugin.link"
echo "  ✅ 插件已注册"

# ─── 链接 Blender Addon ───
echo ""
echo "▶ 安装 Blender Addon..."
BLENDER_APP="/Applications/Blender.app"
if [ ! -d "$BLENDER_APP" ]; then
    echo "❌ 未找到 Blender.app，请从 blender.org 下载安装"
    exit 1
fi

BLENDER_VERSION=$(ls "$BLENDER_APP/Contents/Resources/" | grep -E '^[0-9]+\.' | head -1)
ADDON_DST="$HOME/Library/Application Support/Blender/$BLENDER_VERSION/scripts/addons/$ADDON_NAME"

mkdir -p "$(dirname "$ADDON_DST")"
rm -rf "$ADDON_DST" 2>/dev/null
ln -sf "$SCRIPT_DIR/$ADDON_NAME" "$ADDON_DST"
echo "  ✅ Addon 已链接 (开发模式，改代码后重启 Blender 即生效)"

# ─── 重载 Plugin Service ───
echo ""
echo "▶ 重载 Logi Plugin Service..."
open "loupedeck:plugin/BlenderFlow/reload" 2>/dev/null || true
echo "  ✅ 重载命令已发送"

# ─── 完成 ───
echo ""
echo "========================================="
echo "  ✅ 安装完成！"
echo "========================================="
echo ""
echo "下一步："
echo "  1. 打开 Blender → Edit → Preferences → Add-ons"
echo "     搜索 'BlenderFlow' → 勾选启用"
echo "     (首次启用会自动安装 Python 依赖，需等几秒)"
echo ""
echo "  2. 打开 Logi Options+ → 设备定制 → All Actions"
echo "     找到 BlenderFlow AI → 拖到按键上"
echo ""
echo "  3. 如需 AI 生成功能："
echo "     export TRIPO_API_KEY=\"你的key\""
echo ""
