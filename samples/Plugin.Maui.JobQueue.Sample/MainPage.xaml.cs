using Plugin.Maui.JobQueue.Sample.Jobs;

namespace Plugin.Maui.JobQueue.Sample;

public partial class MainPage : ContentPage
{
    readonly IJobQueue _queue;
    int _photo;
    int _customer;
    int _analytics;

    public MainPage(IJobQueue queue)
    {
        InitializeComponent();
        _queue = queue;
        _queue.JobQueued += OnChanged;
        _queue.JobStarted += OnChanged;
        _queue.JobCompleted += OnChanged;
        _queue.JobFailed += OnChanged;
        _queue.JobDeadLettered += OnChanged;
        _queue.JobCancelled += OnChanged;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = RefreshAsync();
    }

    async void OnPhotoClicked(object? sender, EventArgs e)
    {
        _photo++;
        var photoId = $"photo-{_photo}";
        await _queue.EnqueueAsync(
            new UploadPhotoJob { PhotoId = photoId, PhotoPath = $"/media/{photoId}.jpg" },
            new JobEnqueueOptions { IdempotencyKey = photoId, Priority = JobPriority.High });
    }

    async void OnCustomerClicked(object? sender, EventArgs e)
    {
        _customer++;
        await _queue.EnqueueAsync(new SyncCustomerJob { CustomerId = $"cust-{_customer}" });
    }

    async void OnAnalyticsClicked(object? sender, EventArgs e)
    {
        _analytics++;
        await _queue.EnqueueAsync(new SendAnalyticsJob { EventName = $"screen_view_{_analytics}" });
    }

    async void OnFlakyClicked(object? sender, EventArgs e) =>
        await _queue.EnqueueAsync(new FlakySyncJob { Label = "retry-demo" });

    async void OnPoisonClicked(object? sender, EventArgs e) =>
        await _queue.EnqueueAsync(new PoisonJob { Reason = "schema mismatch" });

    async void OnDrainClicked(object? sender, EventArgs e) =>
        await _queue.DrainAsync();

    async void OnReplayClicked(object? sender, EventArgs e) =>
        await _queue.RequeueDeadLettersAsync();

    void OnChanged(object? sender, EventArgs e) =>
        MainThread.BeginInvokeOnMainThread(() => _ = RefreshAsync());

    async Task RefreshAsync()
    {
        try
        {
            var snapshot = await _queue.GetSnapshotAsync();
            SnapshotLabel.Text =
                $"Worker {(snapshot.IsWorkerRunning ? "on" : "off")} · Net {(snapshot.IsOnline ? "online" : "offline")}{Environment.NewLine}" +
                $"Pending {snapshot.Pending} · Scheduled {snapshot.Scheduled} · Running {snapshot.Running}{Environment.NewLine}" +
                $"Retry {snapshot.Failed} · Dead letter {snapshot.DeadLetter} · Kept success {snapshot.Succeeded}";

            var jobs = await _queue.ListAsync(new JobListQuery { Take = 40 });
            if (jobs.Count == 0)
            {
                JobsLabel.Text = "(empty — success deletes the SQLite row)";
                return;
            }

            JobsLabel.Text = string.Join(Environment.NewLine, jobs.Select(job =>
                $"{job.Status,-11} {job.JobType,-14} q={job.Queue,-9} try {job.Attempts}/{job.MaxAttempts}  {Short(job.LastError)}"));
        }
        catch (Exception ex)
        {
            JobsLabel.Text = ex.Message;
        }
    }

    static string Short(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "" : value.Length <= 40 ? value : value[..40];
}
