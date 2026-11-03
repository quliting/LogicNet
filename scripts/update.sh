#!/usr/bin/env bash
# 本地一键更新 NAS（在开发机上执行，仓库任意分支均可）：
#   git archive HEAD → SSH 管道解包到 NAS 的 ~/logicnet → 远端 ./deploy.sh（构建 + up -d + 健康检查等待）
#
# 为什么用 git archive 而不是 NAS 上 git clone：私有仓库凭据不在 NAS 上，且 NAS 无 GitHub 部署公钥。
# git archive 只包含 git 跟踪的文件，因此 NAS 上的 .env、secrets.json、Logs、Docker 数据卷均不受影响。
# 增量、可重复执行：同一 HEAD 连跑多次结果一致（见 DEPLOY.md「日常更新」）。
#
# 用法：
#   scripts/update.sh              # 更新到本地 HEAD 并部署
#   REF=v1.2.0 scripts/update.sh   # 指定提交/标签
#   NAS_HOST=192.168.2.104 NAS_USER=quliting scripts/update.sh
set -euo pipefail
cd "$(dirname "$0")/.."

NAS_HOST="${NAS_HOST:-192.168.2.104}"
NAS_USER="${NAS_USER:-quliting}"
NAS_DIR="${NAS_DIR:-/home/quliting/logicnet}"
REF="${REF:-HEAD}"

git rev-parse --git-dir >/dev/null 2>&1 || { echo "当前目录不是 git 仓库" >&2; exit 1; }

echo "==> 同步 $REF 到 $NAS_USER@$NAS_HOST:$NAS_DIR（仅 git 跟踪文件）"
git archive --format=tar "$REF" | ssh -o BatchMode=yes "$NAS_USER@$NAS_HOST" \
  "tar -xf - -C '$NAS_DIR' && echo '源码同步完成'"

echo "==> 远端构建并滚动更新"
ssh -o BatchMode=yes "$NAS_USER@$NAS_HOST" "cd '$NAS_DIR' && ./deploy.sh"

echo "==> 更新完成：http://$NAS_HOST:8080/health"
