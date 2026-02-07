# 幂等策略（Idempotency Policy）

## 目的
说明 Tiema DataConnect（原 Backplane）在主要 RPC 上的幂等语义与客户端重试建议，便于可靠重试、故障恢复与运维取证。

## 总体原则
- 幂等：语义相同的请求执行一次或多次应产生相同系统状态与可复现的结果。  
- 可重试：客户端在遇到短暂性网络或可恢复错误时可安全重试幂等操作。  
- 明确错误：在无法安全重复时，服务端应返回确定性错误（例如 gRPC 的 `ALREADY_EXISTS` / `FAILED_PRECONDITION`）。

## RegisterTags（注册 Tags） — 幂等语义
行为
- 支持批量 `RegisterTagInfo`：每条记录的 `source_plugin_instance_id` 优先级高于请求级别的 `plugin_instance_id`。  
- 若同一路径已存在且角色（role）与插件（plugin）相同：复用已分配的 handle（幂等）。  
- 若存在冲突（不同 role/plugin）：服务可根据策略更新或返回冲突，须在 API 文档中明确。

客户端建议
- 使用稳定的 `PluginInstanceId` 并在短暂失败时安全重试 RegisterTags。  
- 若需要严格幂等（端到端相同响应），可在 gRPC metadata 中附带 `idempotency-key`；服务可选实现记录并返回相同响应（需有过期/回收策略）。

实现要点
- 用业务键（path + pluginId + role）做原子 upsert（ConcurrentDictionary、数据库的 ON CONFLICT 或 CAS）。  
- 批量请求返回逐条 `assigned/failed` 结果，避免掩盖部分成功。

## Publish（发布 Tag 值） — 写入语义（非严格幂等）
- Publish 写入镜像并广播。重复 Publish 会覆盖镜像并再次广播（写入非幂等）。  
- 支持单条或批量（TagValue / TagBatch）。

客户端建议
- 若需幂等写入，客户端应携带可比对字段（timestamp、sequence number、app id）或采用确认/应答流程。  
- 若需严格顺序/幂等语义，可在应用层实现带序号的写入协议。

## GetLastValue / Subscribe
- `GetLastValue`：只读、天然幂等。  
- `Subscribe`：服务器流；在建立订阅时服务端应发送初始快照作为隐式确认。使用 `SubscribeRequest.subscriber_id` 可让服务端做去重/合并订阅。

## 批量（Batch）与 RPI（Requested Packet Interval）
- 批量与 RPI 为性能优化；批量内每条的语义等同单条。重复批量遵循相同的覆盖/幂等规则。

## 重试与退避（Retry & Backoff）
- 对可重试操作建议使用指数退避（例如初始 200ms，指数增长，最多 5 次）。对非幂等写操作要谨慎重试，或由上层实现幂等策略。

## 版本化与变更管理
- 将幂等策略写入 API 文档和 proto 的版本说明；任何引入 `idempotency-key` 或行为改变都要做版本说明与迁移指南。

## 示例
- 重试相同的 RegisterTags 返回相同 handle（如果 pluginId/role/path 相同）。  
- 多次 Publish：多次发布将覆盖并重新广播，最终镜像等于最后一次成功写入。

## 运行与可观测性（针对 DataConnect）
- DataConnect（gRPC 服务名为 `DataConnect`，生成的 C# 命名空间为 `Tiema.Connect.Grpc.V1`；Tag 类型在 `Tiema.Tags.Grpc.V1`）应对外暴露 RegisterTags / Publish / Subscribe / GetLastValue。  
- 运营建议采集并监控指标：注册尝试数、注册冲突数、发布速率/失败率、订阅数量与广播重试率，这些指标有助于诊断重试风暴或异常客户端行为。  
- 服务端可选实现幂等记录（有界保留）以增强 RegisterTags 的语义保证。

## 安全与审计
- 对插件进行认证，并在审计日志中记录 `PluginInstanceId`、RegisterTags 与 Publish 操作的来源。  
- 对关键写入场景可考虑请求签名或请求溯源字段以满足高保证环境的合规与审计需求。

## 给插件作者的提示
- 注册标签时请提供稳定的 `PluginInstanceId`；若每次重启都换 id，则无法享受注册的幂等语义。  
- 不要在 `Initialize` 中假定句柄（handle）已可用；把依赖注册与订阅的逻辑放到 `OnStart` 或让宿主在加载后统一调用 `TagAutoRegistrar` 来完成注册与 wiring，然后使用返回的 handle。  