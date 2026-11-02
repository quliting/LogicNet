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
| `ConnectionConfigs:0:ConnectionString` | MySQL 连接串 | 方案 A 指向已有实例；方案 B 用 `Data Source=mysql;Database=logicnet;User ID=root;Password=<DB_ROOT_PASSWORD>;...` |

权限（容器以 uid 1654 非 root 运行，须能读取该文件；其他宿主用户不可读）：

```bash
chmod 600 secrets.json
# 把属主改为容器内 app 用户（借助已有镜像免 sudo）：
docker run --rm -u 0 -v "$PWD":/mnt mcr.microsoft.com/dotnet/aspnet:9.0 chown 1654:1654 /mnt/secrets.json
```

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
./deploy.sh                        # 方案 A
USE_INTERNAL_MYSQL=1 ./deploy.sh   # 方案 B
```

或手工分步：

```bash
docker compose build        # 构建镜像（多阶段，SDK 构建 + aspnet 运行，非 root）
docker compose up -d        # 后台启动（方案 B 请带两个 -f）
docker compose ps           # 查看状态（首次启动 MySQL 初始化较慢，healthy 需等 1-3 分钟）
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

## 4. 升级

```bash
git pull                  # 或重新传输源码
./deploy.sh               # 重新构建并滚动替换容器（构建完成 ≠ 部署完成，脚本内含 up -d）
```

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
- 已知平台兼容：部分 NAS（如飞牛）的定制内核上，BuildKit 构建沙箱内运行 apt-get 会段错误（exit 139），
  因此镜像不安装额外工具，compose 健康检查改用镜像自带 bash 的 /dev/tcp 探测 `/health`。
