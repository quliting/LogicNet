#!/usr/bin/env bash
# LogicNet 一键部署：构建 + 启动 + 健康检查。注意：构建完成 ≠ 部署完成，必须执行 up -d 才会创建/更新容器。
# 用法：
#   方案 A（外部 MySQL，默认）：  ./deploy.sh
#   方案 B（compose 内置 MySQL）：USE_INTERNAL_MYSQL=1 ./deploy.sh
#   全量重建（忽略构建缓存）：     ./rebuild.sh
#   显式使用 BuildKit（非飞牛 NAS）：USE_BUILDKIT=1 ./deploy.sh
# 前置：.env（模板 .env.example）、secrets.json（模板 secrets.json.example，属主 uid 1654 权限 600）、
#       resolv.conf（仓库内自带，为容器内非 root 用户提供可读的 DNS 配置）已就位。
set -euo pipefail
cd "$(dirname "$0")"

FILES=(-f docker-compose.yml)
# 方案 B：显式指定 USE_INTERNAL_MYSQL=1，或检测到已存在的内置 mysql 容器（NAS 日常更新无需再传环境变量）
if [ "${USE_INTERNAL_MYSQL:-0}" = "1" ] || docker ps -a --format '{{.Names}}' 2>/dev/null | grep -qx 'logicnet-mysql'; then
  FILES+=(-f docker-compose.mysql.yml)
fi

# 构建前把当前镜像备份为 logicnet:prev，供回滚使用（失败时见 DEPLOY.md「日常更新」）
if docker image inspect logicnet:latest >/dev/null 2>&1; then
  docker tag logicnet:latest logicnet:prev
  echo "已备份当前镜像为 logicnet:prev"
fi

# 飞牛 NAS 的 BuildKit 构建沙箱内执行 apt-get / dotnet publish 会 SIGSEGV（exit 139），
# 因此默认退回 legacy builder；其他平台如需 BuildKit：USE_BUILDKIT=1 ./deploy.sh
if [ "${USE_BUILDKIT:-0}" != "1" ]; then
  export DOCKER_BUILDKIT=0 COMPOSE_DOCKER_CLI_BUILD=0
fi

BUILD_ARGS=()
if [ "${LOGICNET_NO_CACHE:-0}" = "1" ]; then
  BUILD_ARGS+=(--no-cache)
fi

docker compose "${FILES[@]}" build "${BUILD_ARGS[@]}"
docker compose "${FILES[@]}" up -d
docker compose "${FILES[@]}" ps
echo "当前镜像：$(docker images logicnet:latest --format '{{.ID}}  {{.CreatedAt}}')"

# 健康检查：等待 /health 返回 200（数据库连通性由应用内健康检查覆盖）
PORT="$(docker compose "${FILES[@]}" port app 8080 2>/dev/null | awk -F: '{print $NF}')"
PORT="${PORT:-8080}"
for _ in $(seq 1 60); do
  if curl -fsS "http://127.0.0.1:${PORT}/health" >/dev/null 2>&1; then
    echo "健康检查通过：http://127.0.0.1:${PORT}/health 返回 200"
    exit 0
  fi
  sleep 2
done
echo "健康检查未通过（2 分钟），请执行：docker compose ${FILES[*]} logs --tail=200 app" >&2
exit 1
