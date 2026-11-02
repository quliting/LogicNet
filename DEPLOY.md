# LogicNet 容器化部署（Docker / docker compose）

适用场景：飞牛 NAS 或任意装有 Docker 的 Linux 主机（x86_64 / ARM64 均可，镜像随构建机器架构生成）。

## 前置条件

- Docker Engine 20.10+ 与 docker compose v2（`docker compose version` 可查）
- 一个可用的 MySQL 8.x 实例（复用现有实例或使用 compose 内置 MySQL，见下）

## 1. 准备配置

```bash
cp .env.example .env
```

编辑 `.env`：

| 变量 | 说明 | 生成/取值 |
| --- | --- | --- |
| `APP_PORT` | 宿主对外端口 | 默认 8080，被占用则改其他端口 |
| `JWT_ISSUER_SIGNING_KEY` | JWT 签名密钥（≥16 字符） | `openssl rand -base64 48`，**现场生成，不要复用仓库历史中的旧密钥** |
| `DB_CONNECTION_STRING` | MySQL 连接串 | 见 `.env.example` 注释（外部实例 / 内置 mysql 两种写法） |
| `DB_ROOT_PASSWORD` | 仅内置 MySQL 模式使用 | `openssl rand -base64 32` |

数据库方案切换：默认复用外部 MySQL；改用 compose 内置 MySQL 时，按 `docker-compose.yml` 文件头注释启用 `mysql` 服务并调整连接串（注意宿主 3306 端口冲突，数据持久化在 `logicnet-mysql` 卷）。

## 2. 构建与启动

```bash
docker compose build        # 构建镜像（多阶段，SDK 构建 + aspnet 运行，非 root）
docker compose up -d        # 后台启动
docker compose ps           # 查看状态（healthy 需等 start_period 30s 后）
```

健康检查：

```bash
curl -fsS http://localhost:8080/health
# 期望返回 {"status":"Healthy","checks":{"database":{...}}}
```

首次启动时应用自动执行 CodeFirst 建表并写入角色种子（Admin/User），数据库不可达时仅记录错误日志、不阻断启动（`Logs/Error-*.log`）。

## 3. 首次部署：执行 UserInfo 唯一索引幂等脚本

应用建表只补缺失列，不会为已有表补索引，因此需手工执行一次（幂等，可重复执行）：

```bash
# 先跑脚本内的重复数据检查（SELECT ... GROUP BY UserName / Mobile HAVING COUNT>1），
# 确认无重复数据后再让脚本建索引；有重复需先人工清理。
mysql -h <主机> -u <账号> -p LogicNet < Sql/2026-11-02_P0_UserInfo唯一索引.sql
```

内置 MySQL 模式下：`docker compose exec mysql sh -c 'mysql -uroot -p"$MYSQL_ROOT_PASSWORD" logicnet' < "Sql/2026-11-02_P0_UserInfo唯一索引.sql"`

## 4. 升级

```bash
git pull                  # 或重新传输源码
docker compose build      # 重新构建（层缓存使增量构建很快）
docker compose up -d      # 滚动替换容器
```

## 5. 日志

```bash
docker compose logs -f app        # 控制台日志（每条带 traceId）
docker volume inspect logicnet_logicnet-logs   # 文件日志所在卷（Logs/application-*.log / Error-*.log）
```

想直接在 NAS 文件管理器看日志文件：把 compose 中的命名卷改为绑定挂载（注释中有示例），并执行 `mkdir -p ./volumes/logs && sudo chown -R 1654:1654 ./volumes/logs`（容器以 uid 1654 的非 root 用户运行）。

## 6. 其他说明

- 镜像内不含任何密钥：JWT 密钥与连接串均通过 `.env` → 环境变量在运行时注入。
- 镜像不含 Aspose.Words（本地未跟踪商业二进制）：构建上下文已排除 `LogicNet.Application/Dll/`，依赖它的演示用 `TestService` 在容器构建中自动排除，业务接口不受影响。
- Windows 专用的 COM 引用在非 Windows 构建时自动跳过，不影响 Linux/macOS/容器构建。
