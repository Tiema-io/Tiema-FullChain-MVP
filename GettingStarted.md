# GettingStarted / 快速上手

本文档为 Tiema 容器与模块（插件）开发的完整上手指南，包含运行流程、配置要点、服务注册/发现约定、插槽（slot）策略与示例代码。文档为中英对照，便于开发与运维参考。  
This document is a complete Getting Started guide for the Tiema runtime and module/plugin development. It covers runtime flow, configuration, service registry conventions, slot policy and example code. Chinese and English are provided side-by-side.

---

## 1. 总览 / Overview

Tiema 是一个以宿主（container）管理模块为核心的运行时框架。宿主负责加载模块、注入上下文、管理机架/插槽并提供统一的服务注册表（registry）。模块自行控制运行逻辑（周期性循环或后台任务），并通过上下文访问 Tag、Message 与宿主的 `IServiceRegistry`。  
Tiema is a host-centric runtime. The host loads modules, injects contexts, manages racks/slots and exposes a single service registry. Modules control their own execution (periodic cycles or background tasks) and access Tags, Messages and the host's `IServiceRegistry` via the provided context.

关键原则（摘要）：
- 宿主提供单一的服务注册表：所有服务以精确键 `(rackName, slotId, serviceName)` 注册与查找（registry is single source of truth）。  
- 插槽的身份以不可变整数 `slotId` 为准；`slot.Name` 仅为可编辑显示标签（slot identity is immutable `slotId`; `Name` is a mutable label）。  
- 插槽由配置在容器启动时预建；Plug 操作严格按 `slotId` 插入，不会在 Plug 时自动创建槽或机架（strict plug by slotId, no auto-create）。  

---

## 2. 运行流程 / Execution flow

1. 容器启动：读取 `tiema.config.json`，创建 `TiemaContainer`，并按配置预建 `Racks` / `Slots`（使用 `slots[].id` / `slots[].name`）。  
2. 批量加载模块：宿主（可在 `Program.Main`）调用 `LoadModules()`。`ModuleLoader` 加载程序集并通过无参构造实例化模块，宿主随后调用 `Initialize(IModuleContext)`，再调用 `Start()`。  
3. 插槽插入（可选）：若模块配置了 `rack` 和 `slotId`，宿主会在启动阶段调用 `PlugModuleToSlot(moduleId, rackName, slotId)`。插入要求目标 `rack` 与 `slotId` 事先存在，若不存在则操作失败并记录错误。  
4. 运行：`Run()` 阻塞主线程，模块在自己的线程/循环中运行。  
5. 停止：收到退出/中断后，宿主调用 `UnplugModule` / `Stop()`，模块应在 `OnUnplugged` / `OnStop` 中释放资源并优雅退出。  

1. Container startup: load `tiema.config.json`, create `TiemaContainer`, and pre-create racks/slots from configuration.  
2. Load modules: call `LoadModules()`. `ModuleLoader` instantiates modules via parameterless ctor; host calls `Initialize(IModuleContext)` then `Start()`.  
3. Plug modules: if configured with `rack` and `slotId`, host calls `PlugModuleToSlot(moduleId, rackName, slotId)`. The slot must already exist; host will NOT auto-create on plug.  
4. Run: `Run()` blocks; modules run independently.  
5. Stop: host calls `UnplugModule`/`Stop()` for graceful shutdown.

---

## 3. 配置（示例）/ Configuration (example)

示例 `tiema.config.json` 片段：

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

关键配置项说明：
- **modules**: 需加载的模块列表，包含每个模块的 `id`、`path` 和网络 `type`。  
- **racks**: 机架及插槽配置，指定每个插槽的 `id`、`name` 和类型 `type`。插槽选项在此定义。  

要点：
- `racks[].slots[].id` 必须在机架内唯一且为整数（`slotId`）。宿主在初始化时用这些 `id`/`name` 创建插槽并将 `parameters` 注册到 `IServiceRegistry`（以 `slotId` 为键）。  
- `modules[].configuration` 应优先使用 `slotId`（若仍使用 `slotIndex`，容器会将其视为 `slotId` 兼容处理，但不会在 Plug 时创建槽）。  

---

## 4. 服务注册与发现 / Service registration and discovery

宿主提供单一 `IServiceRegistry` 以便统一注册与发现服务。主要 API（示例）：
- `Register(string rackName, int slotId, string serviceName, object implementation)` — 注册服务。  
- `Unregister(string rackName, int slotId, string serviceName)` — 注销服务。  
- `TryGet<T>(string rackName, int slotId, string serviceName, out T? instance)` —  精确查找。  
- `GetBySlotName<T>(string rackName, string slotName, string serviceName)` — 便捷查找（宿主内部把 `slotName` 映射为 `slotId` 再查找）。

Conventions:
- 使用三元键 `(rackName, slotId, serviceName)` 做为 registry 的精确键，避免使用索引作为身份标识。  
- `slotId` 是权威身份；`slot.Name` 是可编辑标签，仅用于显示与便捷查找。  
- 建议在容器初始化时把内置服务（如 `Tags`、`Messages`）也注册到 registry（例如 `rack="host", slotId=0, serviceName="platform.tags"`），以便模块统一通过 registry 发现平台服务。

示例：通过 slot name 查找 LED 指示器并调用：

示例：在模块插入（OnSlotPlugged）时注册槽级服务：

---

## 5. 插槽策略（独占）/ Slot policy (exclusive)

Tiema 的插槽策略为“独占（exclusive）”。简要规则：
- 每个 `slotId` 同一时间只能有一个模块占用（exclusive slot）。多个模块不得共享同一插槽。  
- `PlugModuleToSlot` 在尝试插入时会检查 `slot.IsOccupied`，若已被占用则返回失败并记录日志。宿主不会把多个模块绑定到同一 `slotId`。  
- 若需要多模块协作，应通过服务（registry）或消息（Messages）进行，而不是共享插槽。  

Policy:
- Slots are exclusive. A slot may be occupied by at most one module at a time. Plug attempts into an occupied slot are rejected.  
- For coordination between modules, use services/messages rather than sharing a slot.

冲突处理建议：
- 插入失败（占用）时宿主记录明确日志（`slot occupied`），并可暴露管理 API 以查看占用或强制卸载。  
- 在自动化部署场景，先确保配置中为每个模块分配不冲突的 `slotId`。  

---

## 6. 模块开发指南（实践）/ Plugin developer guide (practical)

推荐实践：
- 推荐继承 `ModuleBase`（内含 `OnInitialize`、`OnSlotPlugged`、`OnSlotUnplugged` 与默认 RunLoop `Execute()`）。  
- 在 `OnInitialize` 做一次性初始化（读取模块配置、订阅消息）。  
- 在 `OnSlotPlugged(ISlot slot)` 中完成插槽相关初始化：通过 `slot.Id` 从 registry 读取参数、注册槽级服务、绑定硬件资源等。  
- `Execute()` 中实现周期逻辑，确保快速返回；若有长时间或并行任务，在 `OnStart` 中创建并在 `OnStop` 中停止。  
- 在 `OnSlotUnplugged` / `OnStop` 中注销槽级服务并释放资源。  

示例（温度报警触发 LED）：

---

## 7. 调试与常见问题 / Debugging & common issues

- 插入失败（PlugModuleToSlot 返回失败）：
  - 检查日志：宿主会记录 `rack` 或 `slotId` 不存在或 `slot` 已被占用的错误信息。  
  - 验证 `tiema.config.json` 中是否存在对应的 `racks` / `slots` 定义（包括 `id` 与 `name`）。  
- 未收到 OnPlugged：
  - 确认宿主在 `slot.Plug(module)` 成功后调用了 `entry.Context.SetCurrentSlot(slot)` 并调用了 `entry.Module.OnPlugged(slot)`（宿主实现在调试模式会打印这些步骤）。  
- 服务查找失败：
  - 确认初始化阶段 `InitializeRacksFromConfig()` 已把 `slot.name` 与参数写入 registry；使用 `GetBySlotName` 时宿主会把 name 映射为 id 后再查找。

---

## 8. 构建与运行 / Build & run

- 构建建议：

````````

- 运行前务必确认 `tiema.config.json` 中已明确列出 `racks` / `slots`（含 `id` 与 `name`），因为 Plug 操作不会自动创建槽。  

---

## 9. 迁移建议 / Migration notes

从旧实现迁移要点：
- 将所有依赖 slot index 的逻辑迁移为 `slotId`（或先解析 `slotName` -> `slotId` 再查 registry）。  
- 把槽级服务注册集中到宿主的 `IServiceRegistry`（避免分散在 `ISlot` / `IRack` 实现中）。  
- UI/运维工具中将 `slotId` 视为槽的权威身份，`slot.Name` 仅作为可编辑标签。

---

