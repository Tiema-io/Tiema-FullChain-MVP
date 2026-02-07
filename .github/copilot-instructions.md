# Copilot Instructions

## General Guidelines
- Use English-only comments in code by default, unless a specific file requests bilingual Chinese-English comments.
- Tiema 数据总线 (Tiema Backplane, TB) is an independent service as a unified data layer; any language can interoperate if it follows the protocol.
- Architecture: keep single-process InMemory for debug; support multi-process with gRPC data plane and Named Pipe control plane; future: multiple hosts share a single or bridged Backplane across machines with namespaces, ACL, TLS.
- Debugging: use unique ModuleInstanceId/pluginId, command-line args, and supervisor to manage and attach.
- Implement single-process Tag/backplane first and avoid large expansions; prepare for cross-process (跨进程) plugin modes later, allowing the system to switch between in-process (InMemory) and distributed (gRPC) backplane modes for flexibility in debugging. **Prefer using InMemoryBackplane for debugging; switch to gRPC/remote Backplane for distributed/production.** Prefer applying EtherNet/IP design concepts (I/O assemblies, RPI/implicit IO, batching) over gRPC transport while keeping plugin/adapters extensibility; use InMemory for debugging and gRPC for production. **Adopt the implicit/explicit I/O concepts from CIP on the gRPC adapter, exposing tags in implicit I/O form via the plugin SDK, and first fix current subscription/reuse-related errors before implementing these concepts on the gRPC adapter (not a complete implementation of the EtherNet/IP protocol).**
- Strengthen BuiltInTagService subscription management: ensure single backend subscription per handle, thread-safety, and reliable subscribe/unsubscribe behavior; use InMemory for debug and gRPC for production.
- **TiemaTagAttribute lives in Contracts; TagAutoRegistrar registers after Initialize using ModuleInstanceId.** 
- **TiemaTag 应用默认的 implicit I/O 设计，确保 explicit I/O 通过 Message 进行通信，发布时应确保直接影响 handle 的原始值。**
- Keep work in the current feature branch 'feat/tiema-backplane-mvp' rather than creating many short-lived branches; prefer small commits and pushes on that branch.

## Project Plan for Tiema Backplane (TB)
- **Stage A (MVP)**: Rename `GrpcBackplaneServer` to `TiemaBackplaneServer`, update Program startup logs and documentation to show 'Tiema 数据总线 (Tiema Backplane, TB)'; add optional `Tiema.BackplaneService` project.
- **Stage B (naming & adapters)**: Rename gRPC adapter/transport/client classes to use `TiemaBackplane*` prefix (e.g., `TiemaBackplaneTransport`, `TiemaBackplaneAdapter`, `TiemaBackplaneClient`); update proto comments for TB naming.
- **Stage C (validation & examples)**: Add `TiemaBackplaneClient` (.NET) and cross-language examples (Python/Go); update Getting Started documentation and run tests.
- **Stage D (future)**: Extract TB to a separate repository, add authentication, namespaces, bridging, batching/RPI, and persistence. Ensure InMemory for debug and gRPC TB for production; TagAutoRegistrar/ModuleInstanceId/Contracts rules apply.

## Code Style
- Follow specific formatting rules.
- Maintain consistent naming conventions.
- Update smoke test to use generated proto names: service renamed to TiemaBackplane -> client class TiemaBackplane.TiemaBackplaneClient. Enum names: use TagRole.Producer and QualityCode.QualityGood. Prefer generated property names like TagPath, ModuleInstanceId, SourceModuleInstanceId. Remember these proto-driven naming conventions for future code generation.

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
- **TagValue/TagBatch/RegisterTags should be defined in `tagsystem.proto` and `backplane.proto`; ensure `GrpcBackplaneClient` is used for the gRPC Adapter, and adapt `GrpcBackplaneAdapter` and `GrpcIoAdapter` for registration.** 
- **Move gRPC registration/subscription from `OnInitialize` to `OnStart` in `ModbusSensor` and `TemperatureLogic` so modules no longer assume handles during Initialize; modules should not rely on immediate handle allocation during Initialize.** 
- **Open-source strategy with branding/BuildId, optional license token; avoid gRPC in demo plugins.**