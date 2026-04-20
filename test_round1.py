#!/usr/bin/env python3
"""
Round 1 smoke test — 自动验证 F2（路径/输入白名单）和部分 F3（server 生命周期）。

使用前提：
  1. Blender 已打开，BlenderFlow 插件已启用（面板显示 ● Server Running）
  2. Logi 插件可以断开（测试独立于 C# 端）

运行：
  python3 -m pip install --user websockets  # 若未装
  python3 test_round1.py
"""
import asyncio
import json
import sys
import os
from pathlib import Path

try:
    import websockets
except ImportError:
    print("需要先安装: python3 -m pip install --user websockets")
    sys.exit(1)

WS_URL = "ws://localhost:9876"

# ANSI 颜色
GREEN, RED, YELLOW, RESET = "\033[32m", "\033[31m", "\033[33m", "\033[0m"


async def send_and_recv(ws, msg, timeout=1.5):
    """发一条消息，等一条响应（有些消息服务器不回复）。"""
    await ws.send(json.dumps(msg))
    try:
        reply = await asyncio.wait_for(ws.recv(), timeout=timeout)
        return json.loads(reply)
    except asyncio.TimeoutError:
        return None


def check(label, cond, detail=""):
    ok = bool(cond)
    mark = f"{GREEN}PASS{RESET}" if ok else f"{RED}FAIL{RESET}"
    print(f"  [{mark}] {label}" + (f" — {detail}" if detail else ""))
    return ok


async def run_tests():
    print(f"\n=== 连接 {WS_URL} ===")
    try:
        ws = await websockets.connect(WS_URL)
    except Exception as e:
        print(f"{RED}✗ 连不上服务器{RESET}: {e}")
        print(f"{YELLOW}  确认 Blender 已打开且 BlenderFlow 面板显示 ● Server Running{RESET}")
        return False

    print(f"{GREEN}✓ 已连上{RESET}")
    results = []

    async with ws:
        # ── F2a: 非法 mode 被拒绝 ────────────────────────────────
        print(f"\n=== F2a: 非法 mode 必须被拒绝 ===")
        r = await send_and_recv(ws, {"type": "set_mode", "mode": "HACKER_MODE"})
        results.append(check(
            "mode=HACKER_MODE 返回 invalid_mode",
            r and r.get("type") == "error" and r.get("code") == "invalid_mode",
            f"收到: {r}",
        ))

        # 合法 mode 应该被接受（无 error 回复，或非 error）
        r = await send_and_recv(ws, {"type": "set_mode", "mode": "OBJECT"}, timeout=0.8)
        results.append(check(
            "mode=OBJECT 不被拒绝",
            r is None or r.get("type") != "error",
            f"收到: {r}",
        ))

        # ── F2b: 非法 tool op 被拒绝 ─────────────────────────────
        print(f"\n=== F2b: 非法 tool op 必须被拒绝 ===")
        r = await send_and_recv(ws, {"type": "tool", "op": "rm_rf"})
        results.append(check(
            "op=rm_rf 返回 invalid_tool",
            r and r.get("type") == "error" and r.get("code") == "invalid_tool",
            f"收到: {r}",
        ))

        # ── F2c: 非法 format 被拒绝 ──────────────────────────────
        print(f"\n=== F2c: 非法 import 格式必须被拒绝 ===")
        r = await send_and_recv(ws, {
            "type": "import_model",
            "path": str(Path.home() / "BlenderFlow/temp/x.glb"),
            "format": "exe",
        })
        results.append(check(
            "format=exe 返回 invalid_format",
            r and r.get("type") == "error" and r.get("code") == "invalid_format",
            f"收到: {r}",
        ))

        # ── F2d: 路径穿越必须被拒绝 ─────────────────────────────
        print(f"\n=== F2d: 路径穿越必须被拒绝 ===")
        evil_paths = [
            "/etc/passwd",
            "/etc/hosts",
            str(Path.home() / ".ssh/id_rsa"),
            str(Path.home() / "Desktop/anything.glb"),  # 用户桌面也不在白名单
            str(Path.home() / "BlenderFlow/temp/../../../etc/passwd"),  # 相对穿越
            "/tmp/evil.glb",
        ]
        for p in evil_paths:
            r = await send_and_recv(ws, {
                "type": "import_model",
                "path": p,
                "format": "gltf",
            })
            results.append(check(
                f"path={p}",
                r and r.get("type") == "error" and r.get("code") == "invalid_path",
                f"收到: {r}",
            ))

        # ── F2e: 空路径 / 异常类型 ──────────────────────────────
        print(f"\n=== F2e: 空/异常 path 必须被拒绝 ===")
        for p in ["", None, 123, "../../etc/passwd"]:
            r = await send_and_recv(ws, {
                "type": "import_model",
                "path": p,
                "format": "gltf",
            })
            results.append(check(
                f"path={p!r}",
                r and r.get("type") == "error" and r.get("code") == "invalid_path",
                f"收到: {r}",
            ))

        # ── 未知 type 仍然返回 error（回归） ──────────────────────
        print(f"\n=== 回归: 未知 type 应返回 unknown_type ===")
        r = await send_and_recv(ws, {"type": "drop_all_tables"})
        results.append(check(
            "未知 type 返回 unknown_type",
            r and r.get("type") == "error" and r.get("code") == "unknown_type",
            f"收到: {r}",
        ))

        # ── 合法 get_state 仍然能工作（F8 回归 + 新 event 实现） ──
        print(f"\n=== 回归: get_state 仍然返回 state ===")
        r = await send_and_recv(ws, {"type": "get_state"}, timeout=2.0)
        results.append(check(
            "get_state 返回 state 消息",
            r and r.get("type") == "state",
            f"收到: {r}",
        ))
        results.append(check(
            "get_state 携带 mode 字段",
            r and isinstance(r.get("mode"), str) and r.get("mode"),
            f"mode={r.get('mode') if r else None!r}",
        ))

        # ── F8: 连续多次 get_state 每次都能返回（event-based 实现回归） ──
        print(f"\n=== F8: 连续 5 次 get_state 均返回完整 state ===")
        all_ok = True
        for i in range(5):
            r = await send_and_recv(ws, {"type": "get_state"}, timeout=2.0)
            if not (r and r.get("type") == "state" and r.get("mode")):
                all_ok = False
                print(f"    迭代 {i} 失败: {r}")
                break
        results.append(check("5 次 get_state 全部成功", all_ok))

    passed = sum(results)
    total = len(results)
    print()
    if passed == total:
        print(f"{GREEN}✓ 全部通过 {passed}/{total}{RESET}")
        return True
    else:
        print(f"{RED}✗ 失败 {total - passed}/{total}（通过 {passed}）{RESET}")
        return False


if __name__ == "__main__":
    ok = asyncio.run(run_tests())
    sys.exit(0 if ok else 1)
