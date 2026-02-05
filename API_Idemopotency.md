# 幂等策略（Idempotency Policy）  
# Idempotency Policy

目的 / Purpose  
简要说明 Tiema 数据总线（Tiema Backplane, TB）在主要 RPC 上的幂等语义与客户端重试建议，便于可靠重试、故障恢复与运维取证。  
Brief description of idempotency semantics for Tiema Backplane (TB) RPCs and client retry guidance for robust recovery and observability.

---

## 总体原则 / Overall principles
- 幂等（Idempotent）：同语义请求执行一次或多次应产生相同系统状态与可重现的结果。  
  Idempotent: repeating the same logical request must produce the same system state/result.
- 可重试（Safe to Retry）：客户端在遇到网络或非致命错误时可安全重试幂等操作。  
  Safe to Retry: clients may retry idempotent operations on transient failures.
- 明确错误：在无法安全重复时，服务器应返回确定性错误（例如 gRPC 的 `ALREADY_EXISTS` / `FAILED_PRECONDITION`）。  
  Deterministic errors: server should return clear errors when retry is unsafe.

---

## RegisterTags（注册 Tags） — 幂等语义 / RegisterTags (Idempotent)
行为（Behavior）
- 批量提交 `RegisterTagInfo`；每条的 `source_plugin_instance_id` 优先，空则使用 request-level `plugin_instance_id`。  
  Batch `RegisterTagInfo`; per-item `source_plugin_instance_id` overrides request `plugin_instance_id`.
- 若同一路径（path）已存在且 role 与 plugin 相同：返回已分配的 handle（复用，幂等）。  
  If path exists with same role and plugin, reuse the existing handle (idempotent).
- 若存在冲突（role/plugin 不同）：按服务策略选择更新或返回冲突（在 API 文档中明确）。  
  If conflict (different role/plugin), server updates or rejects based on configured policy (documented).

客户端建议（Client recommendations）
- 使用稳定的 `plugin_instance_id`，并在网络错误时安全重试 RegisterTags。  
  Use stable `plugin_instance_id`; safe to retry RegisterTags on transient failures.
- 如需严格幂等键，可在 gRPC metadata 中传 `idempotency-key`，服务器可选支持记录与返回相同响应（需配合 GC 策略）。  
  For strict idempotency, use `idempotency-key` metadata; server may optionally record processed keys.

实现要点（Implementation hints）
- 用业务键（path + pluginId + role）做原子 upsert（ConcurrentDictionary、DB 的 ON CONFLICT 或 compare-and-swap）。  
  Use business key (path+pluginId+role) and atomic upsert for idempotency.
- 对批量请求逐条返回 `assigned/failed`，避免整体掩盖部分成功。  
  Return per-item results in batch responses.

---

## Publish（发布 Tag 值） — 写入语义（非严格幂等） / Publish (Write semantics)
行为（Behavior）
- Publish 写入镜像并广播。重复 Publish 会覆盖镜像并再次广播（默认非幂等写操作）。  
  Publish updates mirror and broadcasts; repeats overwrite and re-broadcast.
- 支持单条与批量（TagValue / TagBatch）。  
  Supports single or batch payloads.

客户端建议（Client recommendations）
- 若需幂等效果，客户端应携带可比较字段（timestamp、sequence number、app id）或采用确认流程。  
  For idempotent writes, include timestamp/sequence or use explicit confirm flow.

---

## GetLastValue / Subscribe
- `GetLastValue`：只读，天然幂等（多次调用返回相同或最新值）。  
  `GetLastValue` is read-only and idempotent.
- `Subscribe`：server-stream；服务端在建立订阅时发送初始快照作为隐式 ACK。若需显式 ACK，可扩展协议。  
  `Subscribe` is server-streaming; server sends initial snapshot as implicit ACK. Explicit ACK can be added via protocol extension.
- 可在 `SubscribeRequest.subscriber_id` 提供稳定 ID，服务端可选实现订阅去重。  
  Provide stable `subscriber_id` to allow optional server-side deduplication.

---

## 批量（Batch）与 RPI
- 批量与 RPI 主要用于性能：批量内每条的语义与单条相同；重复批量请求遵循相同覆盖/幂等语义。  
  Batch/RPI are performance optimizations; per-item semantics equal single-item semantics.

---

## 重试与退避（Retry & Backoff）
- 建议客户端对可重试操作使用指数退避（例：初始 200ms，指数回退，最多 5 次）。对非幂等写操作需谨慎重试或使用上层幂等策略。  
  Clients should use exponential backoff for retries (e.g., start 200ms, double, max 5 tries). Be cautious retrying non-idempotent writes.

---

## 版本化与变更管理
- 把幂等策略纳入 API 文档与 proto 的版本说明（proto v0.1）；任何引入 `idempotency-key` 或策略改变都要做版本说明与迁移指南。  
  Include idempotency policy in API docs and proto version notes; document migrations for changes.

---

## 示例（Examples）
- RegisterTags 重试：重复发送相同 RegisterTags 请求，服务器返回相同 handle（幂等）。  
  Retrying identical RegisterTags returns same handle.
- Publish 多次：多次 Publish 最终以最后一次写入为准并多次广播。  
  Multiple Publish calls overwrite and re-broadcast; final mirror equals last successful write.

---