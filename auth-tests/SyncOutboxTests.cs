using RestaurantAgent.Extractor;
using RestaurantAgent.Sync.Contracts;
using Microsoft.Data.Sqlite;
using Xunit;

namespace RestaurantAgent.Auth.Tests;

public sealed class SyncOutboxTests
{
    [Fact]
    public async Task Pending_batch_survives_restart_and_repeated_enqueue_without_duplicates()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"restaurant-agent-outbox-{Guid.NewGuid():N}.sqlite");
        var batch = new SyncBatch
        {
            BatchId = Guid.NewGuid().ToString("N"),
            BranchCode = "test-branch",
            RangeStart = DateTime.UtcNow.AddMinutes(-5),
            RangeEnd = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow,
            AgentVersion = "test",
            ReconciliationOk = true
        };

        try
        {
            var firstRun = new SyncOutbox(databasePath);
            await firstRun.InitializeAsync(CancellationToken.None);
            await firstRun.EnqueueAsync(batch, CancellationToken.None);

            var restarted = new SyncOutbox(databasePath);
            await restarted.InitializeAsync(CancellationToken.None);
            var pending = await restarted.GetNextAsync(CancellationToken.None);

            Assert.NotNull(pending);
            Assert.Equal(batch.BatchId, pending.Batch.BatchId);
            await restarted.MarkFailedAsync(pending, "central unavailable", CancellationToken.None);

            var afterRetryScheduled = new SyncOutbox(databasePath);
            await afterRetryScheduled.InitializeAsync(CancellationToken.None);
            await afterRetryScheduled.EnqueueAsync(batch, CancellationToken.None);
            Assert.Equal(1, await afterRetryScheduled.CountAsync(CancellationToken.None));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath)) File.Delete(databasePath);
            if (File.Exists(databasePath + "-wal")) File.Delete(databasePath + "-wal");
            if (File.Exists(databasePath + "-shm")) File.Delete(databasePath + "-shm");
        }
    }
}
