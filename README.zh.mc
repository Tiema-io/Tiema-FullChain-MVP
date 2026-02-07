# Tiema Platform

Tiema 是一个面向工业场景的插件平台，支持从 PLC 控制到企业系统的全栈插件开发与运行。此仓库为 Tiema 全栈 MVP（最小可行性产品）实现。

主要变更（最近）
- 名称与术语统一：
  - 原来称作 Backplane 的组件已全面重命名为 **DataConnect**（协议与生成代码、服务名、启动横幅等均已同步）。
  - 运行时与 SDK 术语统一为 **Plugin / PluginInstanceId**（原 Module/ModuleInstanceId 迁移完成）。
- 项目重命名建议：
  - `Tiema.Backplane.Core` → `Tiema.DataConnect.Core`
  - `Tiema.BackplaneService` → `Tiema.DataConnect.Service`（DataConnect 的独立宿主）
- Protobuf：
  - Service 名称：`DataConnect`（生成的 C# 命名空间为 `Tiema.Connect.Grpc.V1`）
  - Tag 相关类型在 `tagsystem.proto` 下，生成命名空间为 `Tiema.Tags.Grpc.V1`
  - 字段采用 plugin 术语（`PluginInstanceId`, `SourcePluginInstanceId` 等）

概览（MVP 目标）
- 插件（Plugin）定义数据与行为，Tiema DataConnect 负责连接、路由、订阅/发布与基础治理（MVP 只实现数据平面：RegisterTags / Publish / Subscribe / GetLastValue）。
- 运行时（Tiema.Runtime）托管插件生命周期、rack/slot 编排与宿主级服务注册（IServiceRegistry）。
- SDK（Tiema.Plugin.Sdk）提供 `PluginBase`、`IPluginContext` 等供插件实现使用。
- 测试：包含单机 InMemory backplane 与 gRPC DataConnect 的 smoke tests（Register → Publish → Get → Subscribe）。

已完成
- 项目骨架与模块/插槽支持
- Tag 系统与订阅机制（InMemory + gRPC transport）
- 将 Backplane 重命名为 DataConnect 并同步 proto / 生成代码
- Plugin 术语统一（IPlugin / IPluginContext / PluginBase 等）
- 基本的示例插件（ModbusSensor、TemperatureLogic、SimpleAlarm 等）
- CI 中增加 DataConnect 构建/Smoke 测试（请检查 .github/workflows）

快速上手（MVP）
- 本地调试优先使用 InMemory backplane：
  - 在 `tiema.config.json` 中将 messaging.transport 设为 `inmemory`（默认）；
  - 运行 `Tiema.Runtime` 启动宿主并加载插件。
- 启动独立 DataConnect 服务：
  - 可执行文件（示例）: `Tiema.DataConnect.Service.exe`，或容器化运行；
  - 环境变量（兼容旧名）：`TIEMA_BACKPLANE_HOST` / `TIEMA_BACKPLANE_PORT`（建议新增别名 `TIEMA_DATACONNECT_HOST` / `TIEMA_DATACONNECT_PORT`）。

开发与构建
- 目标框架： .NET 10
- 生成 Protobuf：由项目内 msbuild / proto 配置生成 C# stubs（生成命名空间见上文）
- 常用命令：
  - 构建：`dotnet build`
  - 运行测试：`dotnet test`
  - 运行 DataConnect 服务（示例）：启动 `Tiema.DataConnect.Service` 项目
  - 运行宿主：启动 `Tiema.Runtime` 项目

注意与迁移提示
- 这是 MVP 阶段：我们选择一次性统一命名（去掉兼容 shim），以便后续清晰迭代；生产化（集群/一致性/持久化/认证）将在后续阶段分步引入。
- 如果你在本地遇到编译或找不到生成类型（例如 `DataConnect.BindService` / `Tiema.Tags.Grpc.V1`），请先清理 `bin/obj` 并重新构建以强制重新生成 proto stubs。
- 插件开发注意：在 Initialize 中不要依赖已分配的句柄（handle）；把依赖注册的逻辑放在 OnStart 或由宿主在加载后统一调用 `TagAutoRegistrar`。

贡献与支持
- 欢迎提交 Issue 和 PR。请在 `feat/tiema-backplane-mvp` 分支上保持小步提交与 CI 通过。
- 联系：在 GitHub 提交 Issues 或发送邮件到 896294580@qq.com

许可证
- 本项目采用 MIT 许可证。
