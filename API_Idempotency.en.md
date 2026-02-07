# Idempotency Policy

## Purpose
Brief description of idempotency semantics for Tiema DataConnect RPCs and client retry guidance for robust recovery and observability.

## Overall principles
- Idempotent: repeating the same logical request must produce the same system state/result.
- Safe to Retry: clients may retry idempotent operations on transient failures.
- Deterministic errors: server should return clear errors when retry is unsafe (e.g. gRPC `ALREADY_EXISTS` / `FAILED_PRECONDITION`).

## RegisterTags (Idempotent)
Behavior
- Batch `RegisterTagInfo`; per-item `source_plugin_instance_id` overrides request-level `plugin_instance_id`.
- If path exists with same role and plugin: reuse the existing handle (idempotent).
- If conflict (different role/plugin): server updates or rejects based on configured policy (documented).

Client recommendations
- Use stable `plugin_instance_id` and safely retry RegisterTags on transient failures.
- For strict idempotency keys, send `idempotency-key` in gRPC metadata; server may optionally record and return the same response (requires GC policy).

Implementation hints
- Use business key (path + pluginId + role) and atomic upsert for idempotency (ConcurrentDictionary, DB ON CONFLICT, CAS).
- Return per-item `assigned/failed` in batch responses to avoid hiding partial successes.

## Publish (Write semantics ¡ª not strictly idempotent)
- Publish updates mirror and broadcasts. Repeating Publish overwrites mirror and re-broadcasts (non-idempotent).
- Supports single or batch payloads (TagValue / TagBatch).

Client recommendations
- For idempotent writes, include comparable fields (timestamp, sequence number, app id) or use a confirm/ack flow.
- Consider write-with-sequence semantics at application layer if strict ordering/overwrite semantics are required.

## GetLastValue / Subscribe
- `GetLastValue` is read-only and idempotent.
- `Subscribe` is server-streaming; server sends an initial snapshot as implicit ACK. Use `SubscribeRequest.subscriber_id` for optional server-side deduplication.

## Batch and RPI
- Batch and RPI are performance optimizations; per-item semantics equal single-item semantics. Repeating a batch follows the same overwrite/idempotency rules.

## Retry & Backoff
- Use exponential backoff for retries (e.g., start 200ms, double, max 5 tries). Be cautious retrying non-idempotent writes.

## Versioning & Change Management
- Include idempotency policy in API docs and proto version notes; document migrations when introducing `idempotency-key` or changing behavior.

## Examples
- Retrying identical RegisterTags returns same handle.
- Multiple Publish calls overwrite and re-broadcast; the final mirror equals the last successful write.

## Operational notes (DataConnect)
- DataConnect (gRPC service `DataConnect`) exposes RegisterTags / Publish / Subscribe / GetLastValue. Ensure clients use the generated stubs in `Tiema.Connect.Grpc.V1`.
- Servers may optionally implement per-request idempotency recording (with bounded retention) to provide stronger client guarantees for RegisterTags.
- Monitor and surface metrics: register attempts, register conflicts, publish rate, publish failures, subscribe counts, and re-broadcast rates ¡ª these help diagnose retry storms or misbehaving clients.

## Security & Audit
- Authenticate plugins and include plugin identity in audit logs for all RegisterTags and Publish operations.
- Consider optional signed requests or request provenance fields for critical write operations in high-assurance deployments.

## Notes for Plugin Authors
- When registering tags from a plugin, provide a stable `PluginInstanceId` and avoid generating a new id on each restart if you need idempotent registration behavior.
- Do not rely on handles being available synchronously in `Initialize`; depend on host wiring (e.g., `TagAutoRegistrar` or `OnStart`) to perform registration and wiring, then use returned handles