namespace Plugin.Maui.JobQueue.Tests;

public sealed class SqliteStoreTests
{
    [Fact]
    public async Task Survives_reopen()
    {
        var path = Path.Combine(Path.GetTempPath(), $"jobqueue-{Guid.NewGuid():N}.db3");
        try
        {
            var first = new SqliteJobStore(path);
            await first.InsertAsync(new JobRecord
            {
                Id = "job-1",
                JobType = "UploadPhotoJob",
                PayloadJson = """{"path":"disk.jpg"}""",
                Queue = "uploads",
                Priority = JobPriority.High,
                Status = JobStatus.Pending,
                MaxAttempts = 5,
                CreatedAt = DateTimeOffset.Parse("2026-08-28T12:00:00Z"),
                NextAttemptAt = DateTimeOffset.Parse("2026-08-28T12:00:00Z"),
                IdempotencyKey = "photo:disk",
                RequiresNetwork = true
            });
            await first.DisposeAsync();

            var second = new SqliteJobStore(path);
            var loaded = await second.FindByIdAsync("job-1");
            Assert.NotNull(loaded);
            Assert.Equal("UploadPhotoJob", loaded.JobType);
            Assert.Equal("uploads", loaded.Queue);
            Assert.Equal(JobPriority.High, loaded.Priority);
            Assert.Equal("photo:disk", loaded.IdempotencyKey);
            Assert.True(loaded.RequiresNetwork);

            var claimed = await second.ClaimNextAsync(new ClaimRequest(
                DateTimeOffset.Parse("2026-08-28T12:00:01Z"),
                IsOnline: true,
                "worker",
                DateTimeOffset.Parse("2026-08-28T12:02:01Z")));

            Assert.NotNull(claimed);
            Assert.Equal(JobStatus.Running, claimed.Status);
            Assert.Equal(1, claimed.Attempts);
            await second.DisposeAsync();
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task Offline_skips_network_jobs()
    {
        var path = Path.Combine(Path.GetTempPath(), $"jobqueue-{Guid.NewGuid():N}.db3");
        try
        {
            var store = new SqliteJobStore(path);
            await store.InsertAsync(new JobRecord
            {
                Id = "net",
                JobType = "NetworkJob",
                PayloadJson = "{}",
                Status = JobStatus.Pending,
                MaxAttempts = 3,
                CreatedAt = DateTimeOffset.UtcNow,
                NextAttemptAt = DateTimeOffset.UtcNow,
                RequiresNetwork = true
            });

            var claimed = await store.ClaimNextAsync(new ClaimRequest(
                DateTimeOffset.UtcNow,
                IsOnline: false,
                "worker",
                DateTimeOffset.UtcNow.AddMinutes(2)));

            Assert.Null(claimed);
            await store.DisposeAsync();
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
