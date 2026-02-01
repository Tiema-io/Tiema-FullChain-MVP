# Copilot Instructions

## General Guidelines
- Use bilingual Chinese-English comments in code and explanations by default.

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