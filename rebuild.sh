#!/usr/bin/env bash
# 全量重建：忽略 Docker 构建缓存，适用于"源码已同步但镜像仍是旧内容"的场景。
# 用法：
#   方案 A（外部 MySQL，默认）：  ./rebuild.sh
#   方案 B（compose 内置 MySQL）：USE_INTERNAL_MYSQL=1 ./rebuild.sh
# 实际构建/启动/健康检查逻辑统一在 deploy.sh 中，避免两份流程不一致。
set -euo pipefail
exec env LOGICNET_NO_CACHE=1 "$(dirname "$0")/deploy.sh" "$@"
