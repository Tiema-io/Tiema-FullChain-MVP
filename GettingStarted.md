
Key rules
- Initialize(IPluginContext) is the single initialization contract: host MUST call it after creating the plugin instance.  
  `Initialize(IPluginContext)` 是唯一初始化契约：宿主在创建插件实例后必须调用它。
- Plugins control their own execution: after `Start()` the plugin may run an internal loop or background tasks. `PluginBase` provides a default run-loop that periodically calls a protected `Execute()` (PLC-style single-cycle logic).  
  插件自行控制执行：`Start()` 后插件可启动内部循环或后台任务。`PluginBase` 提供默认后台循环，会周期性调用受保护的 `Execute()`（类似 PLC 一周期逻辑）。
- Host responsibility: create plugin instance (parameterless ctor), call `Initialize(context)`, then `Start()`. On shutdown call `Stop()` and wait for plugin termination.  
  宿主职责：创建插件实例（无参构造），调用 `Initialize(context)`，然后 `Start()`。停止时调用 `Stop()` 并等待其结束。

3. PluginLoader & Instantiation / 插件装载策略

- `PluginLoader` loads assemblies and instantiates the first `IPlugin` implementation using a parameterless constructor.  
  `PluginLoader` 使用无参构造创建插件实例（装载第一个 IPlugin 实现）。
- Initialization is performed by the host (container), not by `PluginLoader`. This simplifies lifecycle management and avoids tight coupling via ctor injection.  
  初始化由宿主完成（而不是 PluginLoader）——避免构造器注入带来的耦合。

4. Execution Process / 运行流程

1. Container startup: load configuration, create `TiemaContainer` with built-in Tag/Message services.  
2. `LoadPlugins()`:
   - Resolve plugin path (supports relative paths).
   - Instantiate plugin via `PluginLoader` (parameterless ctor).
   - Create `DefaultPluginContext` and call `Initialize(context)` then `Start()`.  
3. `Run()` blocks the main thread (until `Stop()` is invoked); plugins run independently (internal loops or subscriptions).  
4. On shutdown, container calls `Stop()` for each plugin and waits for graceful termination.

容器启动 -> LoadPlugins（实例化 -> Initialize -> Start）-> Run（阻塞直到停止）-> 停止时 Stop 插件并清理。

5. Plugin development guide / 插件开发要点

- Implement a plugin by inheriting `PluginBase` or implementing `IPlugin`. Prefer `PluginBase` for convenience.  
- Put one-time setup (subscriptions, read config) in `OnInitialize()` / `Initialize()`.  
- Put periodic or PLC-style logic in the protected `Execute()` method (called by default run-loop). Or override `OnStart()` / `OnStop()` to start/stop custom background tasks.  
- Keep `Execute()` fast and non-blocking; use `OnStart()` to spawn separate tasks if needed.

示例（简要说明）:
- `ModbusSensor`: starts an internal loop that periodically writes temperature to Tag system.
- `TemperatureLogic`: reads Tag value and publishes alarm message when threshold exceeded.
- `SimpleAlarm`: subscribes to alarm messages in `Initialize` and sets Tag flags; optionally inspects flags in its internal loop.

6. Build & Run / 构建与运行

- Do not output build artifacts to repository root. Use dedicated artifacts directory or default output:
  - Recommended:
    ```
    dotnet clean "./src/Tiema.Runtime"
    dotnet build "./src/Tiema.Runtime" -c Release -o "./artifacts/Tiema.Runtime"
    ```
  - Or simply:
    ```
    dotnet build "./src/Tiema.Runtime" -c Release
    ```
- For plugin projects, use ProjectReference to `Tiema.Abstractions` to ensure ABI compatibility (avoid mismatched interface versions).

7. Migration notes / 迁移注意

If you previously used constructor injection or the old `Execute(ICycleContext)` model:
- Change plugin to provide a parameterless ctor.
- Move ctor logic into `OnInitialize(IPluginContext)` or `OnStart()`.
- Move per-cycle logic into the protected `Execute()` or into an internal loop started in `OnStart()`.
- Ensure you call `Stop()` cleanup in `OnStop()`.

8. Future work / 后续计划
- Backplane routing (跨进程/跨主机消息背板) — planned as a pluggable transport layer.
- Service registration & discovery (host/plugin DI) — will be added to `Tiema.Abstractions` so plugins can resolve services from the host.
- gRPC/backplane adapters, cross-process tests, docker packaging, documentation improvements.

Summary / 总结
- New lifecycle: host creates instance ⇒ Initialize ⇒ Start; plugins run independently and accept Stop for graceful shutdown.  
- This design reduces host scheduling responsibilities and fits a backplane-driven, autonomous plugin model while keeping a clear initialization contract.

Build and run `Tiema.Runtime` to see the container engine in action!
运行 Tiema.Runtime 以查看容器引擎的实际运行情况！
