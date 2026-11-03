# LogicNet 容器化部署（Docker / docker compose）

适用场景：飞牛 NAS 或任意装有 Docker 的 Linux 主机（x86_64 / ARM64 均可，镜像随构建机器架构生成）。

## 前置条件

- Docker Engine 20.10+ 与 docker compose v2（`docker compose version` 可查）
- 一个可用的 MySQL 8.x 实例（复用现有实例，或使用 compose 内置 MySQL）

## 关键机制：密钥为什么走 secrets.json 而不是环境变量

经实测：Furion 在 `Serve.Run` 宿主路径下捕获的 `App.Configuration` **不包含进程环境变量**
（显式 `docker run -e JWTSettings__IssuerSigningKey=...` 启动仍会触发"未配置 JWT 密钥"的 fail-fast）。
因此 JWT 密钥与数据库连接串通过 **挂载 secrets.json** 注入（Furion 会自动扫描输出目录的 *.json，
后加载源覆盖先加载源中的同名字段）。镜像内不含任何密钥；`.env` 只保留 compose 插值用的
`APP_PORT` 与（方案 B 的）`DB_ROOT_PASSWORD`。

## 1. 准备配置

```bash
cp .env.example .env                      # 按需修改 APP_PORT；方案 B 必填 DB_ROOT_PASSWORD
cp secrets.json.example secrets.json      # 填入真实 JWT 密钥与数据库连接串
```

`secrets.json` 要点：

| 字段 | 说明 | 生成/取值 |
| --- | --- | --- |
| `JWTSettings:IssuerSigningKey` | JWT 签名密钥（≥16 字符） | `openssl rand -base64 48`，**现场生成，不要复用仓库历史中的旧密钥** |
| `ConnectionConfigs:0:ConnectionString` | MySQL 连接串 | 方案 A 指向已有实例；方案 B 用 `Data Source=mysql;Database=logicnet;User ID=root;Password=<DB_ROOT_PASSWORD>;...;SslMode=none;AllowPublicKeyRetrieval=True` |

> MySQL 8 默认使用 `caching_sha2_password`：在 `SslMode=none` 的连接串中必须追加
> `AllowPublicKeyRetrieval=True`，否则应用日志报 `Authentication method 'caching_sha2_password' failed`。

权限（容器以 uid 1654 非 root 运行，须能读取该文件；其他宿主用户不可读）：

```bash
chmod 600 secrets.json
# 把属主改为容器内 app 用户（借助已有镜像免 sudo）：
docker run --rm -u 0 -v "$PWD":/mnt mcr.microsoft.com/dotnet/aspnet:9.0 chown 1654:1654 /mnt/secrets.json
```

`resolv.conf` 为仓库自带文件，无需修改：部分 NAS（如飞牛）的 Docker Engine 生成的 `/etc/resolv.conf`
是 `640 root:root`，容器内非 root 用户读不到，会导致服务名（如 `mysql`）解析失败。compose 将其只读
挂载到容器内 `/etc/resolv.conf`，显式使用 Docker 内置 DNS（127.0.0.11），保持非 root 运行。

数据库方案切换：默认方案 A 复用外部 MySQL；方案 B（compose 内置 MySQL）启动时追加覆盖文件，
并把 `secrets.json` 连接串指向 `Data Source=mysql`（密码与 `.env` 的 `DB_ROOT_PASSWORD` 一致）：

```bash
docker compose -f docker-compose.yml -f docker-compose.mysql.yml up -d
# 或 export COMPOSE_FILE=docker-compose.yml:docker-compose.mysql.yml 后省略 -f 参数
```

注意方案 B 下宿主不要再占用 3306，数据持久化在 `logicnet-mysql` 卷。

## 2. 构建与启动

**构建完成 ≠ 部署完成**：`build` 只产出镜像，`up -d` 才会创建/更新容器。推荐一键脚本：

```bash
./deploy.sh                        # 默认：已存在 logicnet-mysql 容器时自动按方案 B 处理
USE_INTERNAL_MYSQL=1 ./deploy.sh   # 显式指定方案 B（首次部署时用）
./rebuild.sh                       # 忽略构建缓存全量重建（镜像存在但内容未更新时使用）
```

`deploy.sh` 默认使用 legacy builder（`DOCKER_BUILDKIT=0`），以绕开飞牛 NAS 上 BuildKit 构建沙箱的
段错误（exit 139）；其他平台想用 BuildKit 时加 `USE_BUILDKIT=1`。脚本内含 `up -d` 和健康检查等待，
不会出现"构建完成但容器仍跑旧镜像"的情况。

或手工分步（NAS 上同样需要 `DOCKER_BUILDKIT=0`）：

```bash
DOCKER_BUILDKIT=0 docker compose build   # 构建镜像（多阶段，SDK 构建 + aspnet 运行，非 root）
docker compose up -d                     # 后台启动（方案 B 请带两个 -f）
docker compose ps                        # 查看状态（首次启动 MySQL 初始化较慢，healthy 需等 1-3 分钟）
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

内置 MySQL 模式下：`docker compose -f docker-compose.yml -f docker-compose.mysql.yml exec mysql sh -c 'mysql -uroot -p"$MYSQL_ROOT_PASSWORD" logicnet' < "Sql/2026-11-02_P0_UserInfo唯一索引.sql"`

## 4. 日常更新

NAS 上 `~/logicnet` 是 **git archive 源码快照（无 `.git`）**：私有仓库凭据不在 NAS 上，因此由开发机推送更新。

```bash
# 在开发机仓库根目录，一条命令：同步 HEAD → 远端构建 → 滚动更新 → 健康检查
scripts/update.sh
```

等价的手工步骤（`scripts/update.sh` 内部就是这两步）：

```bash
git archive --format=tar HEAD | ssh quliting@192.168.2.104 "tar -xf - -C ~/logicnet"
ssh quliting@192.168.2.104 "cd ~/logicnet && ./deploy.sh"
```

要点：

- **只同步 git 跟踪文件**：NAS 上的 `.env`、`secrets.json`、`Logs/`、Docker 数据卷（MySQL 数据）都不会被覆盖。
  因此 `.env` / `secrets.json` 不要提交进仓库；任一端修改配置都不会被更新流程抹掉。
- `deploy.sh` 会自动识别已存在的 `logicnet-mysql` 容器并叠加 `docker-compose.mysql.yml`（方案 B），
  日常更新无需再传 `USE_INTERNAL_MYSQL=1`。
- 每次构建前旧镜像会打上 `logicnet:prev` 标签；`deploy.sh` 内含 `up -d` 与 `/health` 等待（最长 2 分钟）。

**回滚**（在 NAS 上执行，回到上一个成功部署的镜像）：

```bash
docker tag logicnet:prev logicnet:latest
docker compose -f docker-compose.yml -f docker-compose.mysql.yml up -d --force-recreate app
curl -fsS http://127.0.0.1:8080/health
```

**注意**：解包是覆盖式，仓库中已删除的文件不会从 NAS 快照中消失；确需彻底对齐可在 NAS 上重建
`~/logicnet`（先备份 `.env`、`secrets.json`，数据卷不受影响）。

## 5. 日志

```bash
docker compose logs -f app        # 控制台日志（每条带 traceId）
docker volume inspect logicnet_logicnet-logs   # 文件日志所在卷（Logs/application-*.log / Error-*.log）
```

想直接在 NAS 文件管理器看日志文件：把 compose 中的命名卷改为绑定挂载（注释中有示例），并执行 `mkdir -p ./volumes/logs && sudo chown -R 1654:1654 ./volumes/logs`（容器以 uid 1654 的非 root 用户运行）。

## 6. 其他说明

- 镜像内不含任何密钥：JWT 密钥与连接串经 `secrets.json` 只读挂载注入。
- 镜像不含 Aspose.Words（本地未跟踪商业二进制）：构建上下文已排除 `LogicNet.Application/Dll/`，依赖它的演示用 `TestService` 在容器构建中自动排除，业务接口不受影响。
- Windows 专用的 COM 引用在非 Windows 构建时自动跳过，不影响 Linux/macOS/容器构建。
- 已知平台兼容（飞牛 NAS）：
  1. BuildKit 构建沙箱内运行 apt-get / dotnet publish 会段错误（exit 139）。镜像不安装额外工具，
     compose 健康检查改用镜像自带 bash 的 /dev/tcp 探测 `/health`；构建默认走 legacy builder（见第 2 节）。
  2. Docker Engine 生成的 `/etc/resolv.conf`、`/etc/hosts` 为 `640 root:root`，容器内非 root 用户
     无法读取，导致域名解析失败。compose 只读挂载仓库内 `resolv.conf`（127.0.0.11）解决，见第 1 节。
