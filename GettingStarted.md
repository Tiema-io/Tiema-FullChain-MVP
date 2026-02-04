# Tiema 快速上手（Getting Started）

本指南面向想要在本仓库中构建、运行 Tiema 容器与开发插件的工程师。内容简洁、可复制，覆盖构建、运行、配置、插件开发与常见问题排查。

---

## 先决条件
- .NET 10 SDK 已安装  
- Visual Studio 2026 或等效编辑器（可用 `dotnet` CLI）  
- 在仓库根运行命令（示例基于 Windows/命令行）  

常用命令：
- 构建：`dotnet build`
- 运行测试：`dotnet test src/Tiema.Runtime.Tests`
- 运行宿主（开发）：在 `src/Tiema.Runtime` 目录下 `dotnet run`

---

## 运行容器（开发模式）
1. 打开 `src/Tiema.Runtime/tiema.config.json`（或 `AppContext.BaseDirectory/tiema.config.json`）确认 `racks` 与 `modules` 配置正确。宿主要求 `racks[].slots[].id` 明确存在。  
2. 启动（开发）：在仓库根：
   - `cd src/Tiema.Runtime`
   - `dotnet run`
3. 日志会输出启动流程、Tiema 数据总线（Tiema Backplane, TB）（如果启用）、以及模块加载/插槽插入信息。

环境变量（可选）
- `TIEMA_USE_GRPC=1`：启用通过 gRPC 连接 Tiema 数据总线（TB）的模式（宿主/插件可通过 gRPC 连接 TB）。  
- `TIEMA_GRPC_ADDR`：若插件或宿主需要连接远端 Tiema 数据总线（通过 gRPC），可设置地址（例如 `http://127.0.0.1:50051`）。  
- 备注：文档中也会以 “TB 地址” 或 `TIEMA_BACKPLANE_ADDR` 等术语出现；当前实现仍支持使用 `TIEMA_GRPC_ADDR` 作为连接示例。

注意：Host 在 `LoadModule` 中会集中注册插件声明的 tags（不要在 `OnInitialize` 假定已分配句柄）。

---

## 配置示例（关键项）
配置文件路径：`src/Tiema.Runtime/tiema.config.json`  
关键片段示例：
```json
{
  "$schema": "http://json.schemastore.org/tiema.config.json",
  "modules": [
    {
      "id": "ModbusSensor",
      "path": "./plugins/ModbusSensor.dll",
      "type": "tcp",
      "rack": "Rack1",
      "slotId": 1
    }
  ],
  "racks": [
    {
      "name": "Rack1",
      "slots": [
        {
          "id": 1,
          "name": "SensorSlot",
          "type": "modbus",
          "options": {
            "baudRate": 9600,
            "dataBits": 8,
            "parity": "None",
            "stopBits": 1
          }
        }
      ]
    }
  ]
}
```

宿主要求 `racks[].slots[].id` 明确存在，且为整数。建议直接在配置中使用 `slotId`，避免依赖 `slot index`。

---

## 常见问题排查

- 容器无法启动：
  - 检查日志：宿主会输出详细的启动流程与错误信息。
  - 确认配置文件（`tiema.config.json`）中是否包含 `racks` 与 `modules` 配置，且格式正确。
- 模块未能加载：
  - 确认模块路径与名称正确（相对路径应基于宿主启动路径）。
  - 检查模块依赖是否已满足（如 .NET 运行时、相关库等）。
- 插槽未能插入：
  - 确认目标插槽（`slotId`）在配置中已定义且未被其他模块占用。
  - 宿主日志会记录详细的插入失败原因（如 `slot occupied`）。

---

## Tiema 数据总线（Tiema Backplane, TB）调试与测试

Tiema 数据总线（简称 TB）是平台的数据面：注册、发布、订阅与读取 Tag 的统一服务。宿主支持以 gRPC 方式连接 TB（开发时可使用本机内置的 InMemoryBackplane 便于调试）。

- 启用通过 gRPC 连接 TB：设置环境变量 `TIEMA_USE_GRPC=1`，并确保宿主与插件间的地址配置正确。
- 使用 `TIEMA_GRPC_ADDR`（或将来 `TIEMA_BACKPLANE_ADDR`）指定远端 TB 地址（例如 `http://127.0.0.1:50051`）。
- 开发建议：优先在本地使用 InMemoryBackplane（调试更简单）；发布/分布式环境使用 TB（gRPC）以实现多宿主/跨语言互通。

注意：gRPC/网络相关功能需在支持 gRPC 的宿主上测试，确保网络与证书配置（生产环境建议启用 TLS/mTLS）正确。

---

## 迁移建议

从旧实现迁移要点：
- 将所有依赖 slot index 的逻辑迁移为 `slotId`（或先解析 `slotName` -> `slotId` 再查 registry）。  
- 把槽级服务注册集中到宿主的 `IServiceRegistry`（避免分散在 `ISlot` / `IRack` 实现中）。  
- UI/运维工具中将 `slotId` 视为槽的权威身份，`slot.Name` 仅作为可编辑标签。

---

