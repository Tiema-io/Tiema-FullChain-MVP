# Copilot Instructions

## General Guidelines
- Use bilingual Chinese-English comments in code and explanations by default.
- Tiema 核心（runtime）不应引用 SDK，SDK 仅供插件使用。
- 默认情况下，在提供代码修改建议时，仅显示修改内容并提供可选的 git 提交命令或补丁；仅在明确请求时再执行或自动提交。
- Implement single-process Tag/backplane first and avoid large expansions; prepare for cross-process (跨进程) plugin modes later, allowing the system to switch between in-process (InMemory) and distributed (gRPC) backplane modes for flexibility in debugging. **Prefer using InMemoryBackplane for debugging; switch to gRPC/remote Backplane for distributed/production.** Prefer applying EtherNet/IP design concepts (I/O assemblies, RPI/implicit IO, batching) over gRPC transport while keeping plugin/adapters extensibility; use InMemory for debugging and gRPC for production. **Adopt the implicit/explicit I/O concepts from CIP on the gRPC adapter, exposing tags in implicit I/O form via the plugin SDK, and first fix current subscription/reuse-related errors before implementing these concepts on the gRPC adapter (not a complete implementation of the EtherNet/IP protocol).**
- Strengthen BuiltInTagService subscription management: ensure single backend subscription per handle, thread-safety, and reliable subscribe/unsubscribe behavior; use InMemory for debug and gRPC for production.
- **TiemaTag 声明默认都是 implicit I/O（不设置 implicit/explicit）；explicit I/O 留待以后通过 Message 通道实现；背板在收到 publish 数据时应尽量不拆包，直接按 handle 路由并转发原始载荷。**

## Code Style
- Follow specific formatting rules.
- Maintain consistent naming conventions.

## Project-Specific Rules
- Keep SimpleAlarm plugin's subscription-based demonstration; do not modify its subscription logic.
- Prefer using `Initialize(IPluginContext)` as the canonical plugin initialization method; constructor injection can be optional or deprecated but not required.
- Use rack/slot/module concepts; prefer a single canonical container interface named `IRack` (or `IModuleContainer`) replacing `IPluginContainer` and exposing slot/module management, Tag/Message services, and service registry.
- The repository now defines an `IModule` interface in `Tiema.Abstractions`; `ModuleLoader` should load and return `IModule` instances. Prefer module terminology over plugin.
- Initialize racks/slots from `TiemaConfig.Racks` at startup and register slot parameters as slot-level services in the in-memory slot registry; use `DefaultModuleContext.SetCurrentSlot` when plugging/unplugging.
- Register services at the host level using `IServiceRegistry.RegisterHost` and provide lookup methods that accept a slot/rack identifier (e.g., `GetForSlot`/`TryGetForSlot`), avoiding mandatory registration to slots or racks.
- **Unified service registry**: Register services in a single host registry with keys (rackName, slotId, serviceName); lookups use exact match of rackName, slotId, serviceName. **Avoid accessing slots by index; use slotId (immutable slot identifier) for service registration and lookup.**
- `IServiceRegistry` should provide overloads of `Get`/`TryGet` that accept `slotName` (string) in addition to `slotId` (int), allowing lookup by slot name.
- Keep `IModuleHost` minimal; remove dynamic loading APIs such as `LoadModule` and `LoadRacks` from the interface. Use `TiemaContainer` or a separate `IModuleManager` for dynamic module management, while `TiemaContainer` may keep concrete `LoadModule` methods.
- `TiemaContainer` should expose `PlugModuleToSlot(moduleId, rackName, slotIndex)` to plug a loaded module into a slot, set the module's `DefaultModuleContext.CurrentSlot` via `SetCurrentSlot`, and call `ModuleBase.OnPlugged(slot)`.
- `IModuleContext` now exposes `IServiceRegistry` and defines `CurrentSlot` as a non-nullable `ISlot`; `DefaultModuleContext` must implement this signature (CurrentSlot throws when not set) and expose `Services/Tables` as per host. Prefer modules to use `Context.Services` for service lookup.
- **将 TagValue/TagBatch/RegisterTags 等写入专门的 tagsystem.proto（保留 backplane.proto 不变）；将现有 GrpcBackplaneClient 演进为更广义的 gRPC Adapter（建议命名为 GrpcBackplaneAdapter 或 GrpcIoAdapter）负责 adapter 职责（RegisterTags、assembly 管理、TagBatch 转发）。**
- **命名偏好**：将低层 gRPC 客户端命名为 `GrpcBackplaneTransport`，高层适配器命名为 `GrpcBackplaneAdapter`（或 `GrpcIoAdapter`）；保留两层分离以职责清晰。
- **DefaultModuleContext 应直接暴露注入的 ITagService（移除 ModuleScopedTagService）；宿主（TiemaHost.LoadModule）将执行标签注册；在使用 gRPC 背板时，TiemaHostBuilder 应注入 GrpcTagRegistrationManager，以便注册走远端。模块在 Initialize 期间不应依赖立即可用的句柄。**
- **Move gRPC registration/subscription from OnInitialize to OnStart in ModbusSensor and TemperatureLogic so modules no longer assume handles during Initialize; modules should not rely on immediate handle allocation during Initialize.**