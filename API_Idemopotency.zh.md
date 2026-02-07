# 幂等策略（Idempotency Policy）

## 目的
简要说明 Tiema 数据总线（Tiema Backplane, TB）在主要 RPC 上的幂等语义与客户端重试建议，便于可靠重试、故障恢复与运维取证。

## 总体原则
- 幂等：同语义请求执行一次或多次应产生相同系统状态与可重现的结果。
- 可重试：客户端在遇到网络或非致命错误时可安全重试幂等操作。
- 明确错误：在无法安全重复时，服务器应返回确定性错误（例如 gRPC 的 `ALREADY_EXISTS` / `FAILED_PRECONDITION`）。

## RegisterTags（注册 Tags） — 幂等语义
- 批量提交 `RegisterTagInfo`；每条的 `source_module_instance_id` 优先，空则使用 request-level `module_instance_id`。
- 若同一路径已存在且 role 与 module 相同：返回已分配的 handle（复用，幂等）。
- 若存在冲突（role/module 不同）：按服务策略选择更新或返回冲突（在 API 文档中明确）。

客户端建议
- 使用稳定的 `module_instance_id`，并在网络错误时安全重试 RegisterTags。
- 若需严格幂等键，可在 gRPC metadata 中传 `idempotency-key`，服务器可选支持记录与返回相同响应（需配合 GC 策略）。

实现要点
- 用业务键（path + moduleId + role）做原子 upsert（ConcurrentDictionary、DB 的 ON CONFLICT 或 compare-and-swap）。
- 对批量请求逐条返回 `assigned/failed`，避免整体掩盖部分成功。

## Publish（发布 Tag 值） — 写入语义（非严格幂等）
- Publish 写入镜像并广播。重复 Publish 会覆盖镜像并再次广播（非幂等写操作）。
- 支持单条与批量（TagValue / TagBatch）。

客户端建议
- 若需幂等效果，客户端应携带可比较字段（timestamp、sequence number、app id）或采用确认流程。

## GetLastValue / Subscribe
- `GetLastValue`：只读，天然幂等。
- `Subscribe`：server-stream；服务端在建立订阅时发送初始快照作为隐式 ACK。可通过 `SubscribeRequest.subscriber_id` 提供稳定 ID 以支持去重。

## 批量（Batch）与 RPI
- 批量与 RPI 主要用于性能：批量内每条的语义与单条相同；重复批量请求遵循相同覆盖/幂等语义。

## 重试与退避（Retry & Backoff）
- 建议客户端对可重试操作使用指数退避（例：初始 200ms，指数回退，最多 5 次）。对非幂等写操作需谨慎重试或使用上层幂等策略。

## 版本化与变更管理
- 将幂等策略纳入 API 文档与 proto 的版本说明；任何引入 `idempotency-key` 或策略改变都要做版本说明与迁移指南。

## 示例
- RegisterTags 重试：重复发送相同 RegisterTags 请求，服务器返回相同 handle（幂等）。
- Publish 多次：多次 Publish 最终以最后一次写入为准并多次广播。