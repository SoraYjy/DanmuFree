#!/usr/bin/env bash
# 打包「绿色文件夹」分发版：self-contained 单文件 exe + 打包 node.exe。
# 产出 dist/DanmuFree/，双击 DanmuFree.exe 即用 —— 用户无需装 .NET / Node。
#
# 用法：
#   ./pack.sh                   # 自动找本机 node.exe
#   ./pack.sh /path/to/node.exe # 指定 node.exe（standalone，可从 nodejs.org 下载 Windows Binary）
set -e
cd "$(dirname "$0")"

OUT=dist/DanmuFree

# 定位 node.exe（抖音签名用，随包分发）。优先用第 1 个参数；否则按常见安装位置查找；
# 仍找不到时从 PATH 里的 node 解析。
NODE_SRC="${1:-}"
if [ -z "$NODE_SRC" ]; then
  for c in \
    "/c/Program Files/nodejs/node.exe" \
    "/c/Program Files (x86)/nodejs/node.exe" \
    "$PROGRAMFILES/nodejs/node.exe" \
    "$LOCALAPPDATA/Programs/node/node.exe"; do
    [ -f "$c" ] && NODE_SRC="$c" && break
  done
fi
if [ -z "$NODE_SRC" ] && command -v node >/dev/null 2>&1; then
  r="$(command -v node)"; [ -f "$r" ] && NODE_SRC="$r"
  [ -z "$NODE_SRC" ] && [ -f "${r}.exe" ] && NODE_SRC="${r}.exe"
fi
[ -f "$NODE_SRC" ] || { echo "找不到 node.exe。请运行：./pack.sh <node.exe 路径>（从 https://nodejs.org 下载 Windows Binary 即得）"; exit 1; }
echo "node.exe 源：$NODE_SRC"

echo "[1/3] publish（self-contained · single-file · win-x64）..."
rm -rf "$OUT"
dotnet publish src/DanmuFree.App -c Release -r win-x64 --self-contained \
  -p:PublishSingleFile=true -o "$OUT"

echo "[2/3] 复制 node.exe + 验证签名..."
mkdir -p "$OUT/node"
cp "$NODE_SRC" "$OUT/node/node.exe"
SIG=$("$OUT/node/node.exe" "$OUT/sign/sign_runner.js" d41d8cd98f00b204e9800998ecf8427e 2>/dev/null)
[ -n "$SIG" ] && echo "  签名自检 OK（X-Bogus=$SIG）" || { echo "  签名自检失败"; exit 1; }

echo "[3/3] 完成。"
echo "产出：$OUT  （$(du -sh "$OUT" | cut -f1)）"
echo "  双击 $OUT/DanmuFree.exe 即用（无需安装 .NET / Node）"
echo "  分发：把整个 DanmuFree 文件夹打成 zip 发给用户即可"
