# Plugin.Maui.JobQueue

A durable task queue for **.NET MAUI** on **iOS** and **Android**.

This is not `Plugin.Maui.BackgroundTasks`. That package asks the operating system to wake you later (JobScheduler / BGTaskScheduler). This package is an application-level queue: you enqueue typed jobs, they live in **SQLite**, and an in-process worker runs them with retry, backoff, and a dead-letter queue.

```
await queue.EnqueueAsync(new UploadPhotoJob(...));
await queue.EnqueueAsync(new SyncCustomerJob(...));
await queue.EnqueueAsync(new SendAnalyticsJob(...));
```

```
SQLite
   ↓
Job Queue
   ↓
Worker
   ↓
Success → Delete

Failure
   ↓
Retry
   ↓
Backoff
   ↓
Dead Letter
```

Built for offline-first enterprise apps: photos, customer sync, and analytics keep working after process death.

## BackgroundTasks vs JobQueue

| | BackgroundTasks | JobQueue |
| --- | --- | --- |
| What it is | OS background scheduler | Durable work queue |
| Persistence | Schedule metadata | Full job payloads in SQLite |
| Who starts work | Android JobScheduler / iOS BGTaskScheduler | In-process worker (and `DrainAsync`) |
| Retry | OS retry | Exponential backoff, max attempts, dead letter |
| API | `ScheduleAsync(taskId)` | `EnqueueAsync(new UploadPhotoJob(...))` |
| Success | Handler returns `Success` | Row is deleted |
| Use when | You need the OS to wake the app | You need work that must not be lost |

They compose: schedule a BackgroundTasks handler that calls `JobQueue.Current.DrainAsync()`.

## Install

```bash
dotnet add package Plugin.Maui.JobQueue
```

Target frameworks: `net10.0`, `net10.0-android`, `net10.0-ios`.

## Quick start

```csharp
using Plugin.Maui.JobQueue;

builder
    .UseMauiApp<App>()
    .UseMauiJobQueue(options =>
    {
        options.Register<UploadPhotoJob, UploadPhotoJobHandler>();
        options.Register<SyncCustomerJob, SyncCustomerJobHandler>();
        options.Register<SendAnalyticsJob, SendAnalyticsJobHandler>();
    });
```

```csharp
[Job("UploadPhoto", Queue = "uploads", MaxAttempts = 5, RequiresNetwork = true)]
public sealed class UploadPhotoJob : IJob
{
    public string PhotoPath { get; set; } = "";
}

public sealed class UploadPhotoJobHandler : IJobHandler<UploadPhotoJob>
{
    public async Task ExecuteAsync(UploadPhotoJob job, JobContext context, CancellationToken cancellationToken)
    {
        await uploader.UploadAsync(job.PhotoPath, cancellationToken);
    }
}
```

Resolve `IJobQueue` from dependency injection, or use `JobQueue.Current`.

```csharp
await queue.EnqueueAsync(new UploadPhotoJob { PhotoPath = path });
await queue.EnqueueAsync(new SyncCustomerJob { CustomerId = id });
await queue.EnqueueAsync(new SendAnalyticsJob { EventName = "opened" });
```

## What you get

| Capability | How |
| --- | --- |
| **Durable persist** | SQLite under app data. Process death does not drop work. |
| **Typed jobs** | JSON payload + `IJobHandler<T>` resolved from DI. |
| **Success deletes** | Default `DeleteOnSuccess = true`. |
| **Retry + backoff** | Failed attempts wait (`2s, 4s, 8s…` capped) then run again. |
| **Dead letter** | After `MaxAttempts`, or `context.Abort("reason")`. |
| **Replay** | `RequeueDeadLetterAsync` / `RequeueDeadLettersAsync`. |
| **Idempotency** | Same `IdempotencyKey` returns the existing pending job. |
| **Priority / queues** | `uploads`, `sync`, `analytics`, plus `JobPriority`. |
| **Delayed jobs** | `Delay` or `ScheduleAt`. |
| **Network gate** | `RequiresNetwork` skips work while offline. |
| **Lease recovery** | Stuck `Running` rows return to pending after `LeaseDuration`. |
| **Drain** | `DrainAsync()` for tests or an OS background wake. |

## Enqueue options

```csharp
await queue.EnqueueAsync(new UploadPhotoJob { PhotoPath = path }, new JobEnqueueOptions
{
    Queue = "uploads",
    Priority = JobPriority.High,
    Delay = TimeSpan.FromSeconds(10),
    MaxAttempts = 8,
    IdempotencyKey = "photo:" + photoId,
    RequiresNetwork = true
});
```

Throw from a handler to retry. Call `context.Abort("invalid payload")` to dead-letter immediately.

## Inspect and replay

```csharp
var snapshot = await queue.GetSnapshotAsync();
var dead = await queue.GetDeadLettersAsync();
await queue.RequeueDeadLetterAsync(dead[0].Id);
```

## Without the generic host

```csharp
var services = new ServiceCollection();
services.AddMauiJobQueue(options =>
{
    options.UseInMemoryStore = true;
    options.Register<UploadPhotoJob, UploadPhotoJobHandler>();
});
var queue = services.BuildServiceProvider().GetRequiredService<IJobQueue>();
await queue.EnqueueAsync(new UploadPhotoJob { PhotoPath = path });
await queue.DrainAsync();
```

## Platform notes

The queue itself is shared code. Android and iOS both persist to `FileSystem.AppDataDirectory`.

| | Android | iOS | `net10.0` |
| --- | --- | --- | --- |
| Enqueue / persist / retry / dead letter | Yes | Yes | Yes (tests) |
| In-process worker | Yes | Yes | Yes |
| Connectivity gate | `Connectivity` | `Connectivity` | Always online / manual |
| OS wake-ups | Use BackgroundTasks + `DrainAsync` | Same | n/a |

Declare network permissions if handlers upload:

```xml
<uses-permission android:name="android.permission.INTERNET" />
<uses-permission android:name="android.permission.ACCESS_NETWORK_STATE" />
```

iOS needs no extra `Info.plist` keys for the queue. Add BackgroundTasks identifiers only if you compose the two plugins.

## Sample

`samples/Plugin.Maui.JobQueue.Sample` enqueues photo, customer, analytics, flaky-retry, and poison jobs against a live SQLite file.

```bash
dotnet build src/Plugin.Maui.JobQueue/Plugin.Maui.JobQueue.csproj
dotnet pack src/Plugin.Maui.JobQueue/Plugin.Maui.JobQueue.csproj -c Release -o artifacts
dotnet test tests/Plugin.Maui.JobQueue.Tests/Plugin.Maui.JobQueue.Tests.csproj
dotnet build samples/Plugin.Maui.JobQueue.Sample/Plugin.Maui.JobQueue.Sample.csproj -f net10.0-android
```

## Pack from source

```bash
dotnet pack src/Plugin.Maui.JobQueue/Plugin.Maui.JobQueue.csproj -c Release -o artifacts
```

The `.nupkg` is written to `artifacts/Plugin.Maui.JobQueue.1.0.0.nupkg`.

## License

MIT

## Support

> If this plugin saved you a weekend of native plumbing, consider buying me a coffee.
> Your support keeps it maintained, documented, and free.

[![Buy Me A Coffee](https://img.shields.io/badge/Buy%20Me%20a%20Coffee-ffdd00?style=for-the-badge&logo=buy-me-a-coffee&logoColor=black)](https://buymeacoffee.com/npadhy)

This library stays open source. A coffee helps cover time for bug fixes, new features, and docs.
