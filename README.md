# LogicNet

.NET 9 + Furion 后端，前端为 vben（`apps/web-ele`），代码同仓库不同分支：

- 后端：`main`
- 前端：`front` 分支（快照），本地开发目录 `Front/`

## 快速开始

```bash
# 后端：先准备 secrets.json（JWT 密钥 + MySQL 连接串），再启动
dotnet run --project LogicNet.Web.Entry      # 默认 8080，探活 GET /health

# 前端
git clone -b front git@github.com:quliting/LogicNet.git LogicNet-Front
cd LogicNet-Front && pnpm install && pnpm dev:ele
```

## 文档

- [开发进度与交接](docs/开发进度与交接.md) — 已完成功能、换机继续步骤、接口契约、待办
- [菜单维护与动态导航接口](docs/Menu维护接口说明.md)
- [操作审计日志说明](docs/操作审计日志说明.md)
- [容器化部署](DEPLOY.md)
