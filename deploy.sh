#!/usr/bin/env bash
# LogicNet 一键部署：构建 + 启动。注意：构建完成 ≠ 部署完成，必须执行 up -d 才会创建/更新容器。
# 用法：
#   方案 A（外部 MySQL，默认）：  ./deploy.sh
#   方案 B（compose 内置 MySQL）：USE_INTERNAL_MYSQL=1 ./deploy.sh
# 前置：.env（模板 .env.example）与 secrets.json（模板 secrets.json.example，属主 uid 1654 权限 600）已就位。
set -euo pipefail
cd "$(dirname "$0")"

FILES=(-f docker-compose.yml)
if [ "${USE_INTERNAL_MYSQL:-0}" = "1" ]; then
  FILES+=(-f docker-compose.mysql.yml)
fi

docker compose "${FILES[@]}" build
docker compose "${FILES[@]}" up -d
docker compose "${FILES[@]}" ps
echo "部署完成。健康检查：curl -fsS http://localhost:${APP_PORT:-8080}/health"
