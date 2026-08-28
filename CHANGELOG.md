# Changelog

## 1.0.0

- Durable SQLite job queue for .NET MAUI on iOS and Android
- Typed `IJob` / `IJobHandler<T>` with DI, named queues, and priority
- Success deletes the row; failure retries with exponential backoff
- Dead-letter queue, replay, abort, and idempotency keys
- Process-death lease recovery, delayed jobs, and a network gate
- In-process worker plus `DrainAsync` for tests and OS background wakes
