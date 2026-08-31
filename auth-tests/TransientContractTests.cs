using System.Text.Json;
using RestaurantAgent.Sync.Contracts;
using Xunit;

namespace RestaurantAgent.Auth.Tests;

public sealed class TransientContractTests
{
    [Fact]
    public void Temp_folio_is_scoped_by_shift_when_assigned()
    {
        var first = new TransientSaleHeader { TempFolio = 7, IdTurno = 423 };
        var reused = new TransientSaleHeader { TempFolio = 7, IdTurno = 424 };

        Assert.Equal("423:7", first.IdempotencyKey);
        Assert.Equal("424:7", reused.IdempotencyKey);
        Assert.NotEqual(first.IdempotencyKey, reused.IdempotencyKey);
    }

    [Fact]
    public void Unassigned_header_and_children_share_workspace_scoped_key()
    {
        var header = new TransientSaleHeader { TempFolio = 6, WorkspaceId = "header-6" };
        var line = new TransientSaleLine
        {
            TempFolio = 6,
            HeaderWorkspaceId = "header-6",
            Movimiento = 1
        };

        Assert.Equal("sin-turno:header-6", header.IdempotencyKey);
        Assert.Equal(header.IdempotencyKey, line.HeaderKey);
    }

    [Fact]
    public void Legacy_batch_does_not_claim_a_complete_transient_snapshot()
    {
        var batch = JsonSerializer.Deserialize<SyncBatch>("""
            {
              "batchId":"legacy",
              "branchCode":"branch",
              "rangeStart":"2026-08-31T00:00:00",
              "rangeEnd":"2026-09-01T00:00:00",
              "createdAtUtc":"2026-08-31T12:00:00Z",
              "agentVersion":"old",
              "reconciliationOk":true
            }
            """,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(batch);
        Assert.False(batch.TransientSnapshotComplete);
        Assert.Empty(batch.TransientSales);
        Assert.Empty(batch.TransientLines);
        Assert.Empty(batch.TransientPayments);
    }
}
