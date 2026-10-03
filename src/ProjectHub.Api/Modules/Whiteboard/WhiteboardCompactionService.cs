namespace ProjectHub.Api.Modules.Whiteboard;

/// <summary>
/// Compacts whiteboards in the background (ADR 0009). Runs in every instance; the advisory lock per
/// whiteboard makes sure only one of them works on a whiteboard at a time.
/// </summary>
internal sealed class WhiteboardCompactionService(
    IServiceScopeFactory scopes,
    WhiteboardOptions options,
    ILogger<WhiteboardCompactionService> logger) : BackgroundService
{
    private const int BatchSize = 20;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.CompactionInterval <= TimeSpan.Zero)
        {
            return;
        }

        using var timer = new PeriodicTimer(options.CompactionInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Whiteboard compaction failed");
            }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        IReadOnlyList<Guid> due;
        using (var scope = scopes.CreateScope())
        {
            due = await scope.ServiceProvider.GetRequiredService<WhiteboardDocumentStore>()
                .DueForCompactionAsync(options.QuietPeriod, options.MaxPendingUpdates, BatchSize, ct);
        }

        foreach (var whiteboardId in due)
        {
            // One scope per whiteboard, so a failure leaves no half-done state in a shared DbContext.
            using var scope = scopes.CreateScope();
            try
            {
                await scope.ServiceProvider.GetRequiredService<WhiteboardDocumentStore>().CompactAsync(whiteboardId, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Compaction of whiteboard {WhiteboardId} failed", whiteboardId);
            }
        }
    }
}
