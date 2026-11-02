# 多阶段构建：SDK 9.0 发布 -> aspnet 9.0 运行
# 构建（仓库根目录为上下文）：docker build -f LogicNet.Web.Entry/Dockerfile -t logicnet:latest .

# ---- 构建阶段 ----
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# 先拷贝 csproj 还原依赖，利用 Docker 层缓存（源码变动不触发重复 restore）
COPY ["LogicNet.Web.Entry/LogicNet.Web.Entry.csproj", "LogicNet.Web.Entry/"]
COPY ["LogicNet.Web.Core/LogicNet.Web.Core.csproj", "LogicNet.Web.Core/"]
COPY ["LogicNet.Core/LogicNet.Core.csproj", "LogicNet.Core/"]
COPY ["LogicNet.Application/LogicNet.Application.csproj", "LogicNet.Application/"]
RUN dotnet restore "LogicNet.Web.Entry/LogicNet.Web.Entry.csproj"

COPY . .
# 发布为可移植的框架依赖输出：不指定 RID、不启用 R2R/单文件，
# 同一个 Dockerfile 在 x86_64 与 ARM64（如部分 NAS）上都能构建出匹配架构的镜像。
# 注意：构建上下文中不含 LogicNet.Application/Dll（见 .dockerignore），
# 因此 Aspose.Words 相关 TestService 按 csproj 条件自动排除，镜像内不含 Aspose。
RUN dotnet publish "LogicNet.Web.Entry/LogicNet.Web.Entry.csproj" \
      -c Release -o /app/publish \
      /p:UseAppHost=false /p:PublishReadyToRunComposite=false

# ---- 运行阶段：只拷贝发布产物，非 root 运行 ----
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

# 密钥与连接串一律通过环境变量注入（ConnectionConfigs__0__ConnectionString /
# JWTSettings__IssuerSigningKey，见 .env.example），镜像内不包含任何机密。
ENV ASPNETCORE_HTTP_PORTS=8080

# aspnet 基础镜像不含 curl 且本项目中在飞牛 NAS 的 BuildKit 构建沙箱内 apt-get 会段错误（exit 139），
# 因此不安装额外工具：健康检查由 compose 用镜像自带 bash 的 /dev/tcp 实现（见 docker-compose.yml）。
# 预建日志目录并授权给运行用户，保证非 root 下可写 Logs/。
USER root
RUN mkdir -p /app/Logs && chown -R $APP_UID:$APP_UID /app
USER $APP_UID

COPY --from=build /app/publish .

EXPOSE 8080
ENTRYPOINT ["dotnet", "LogicNet.Web.Entry.dll"]
