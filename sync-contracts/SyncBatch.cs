namespace RestaurantAgent.Sync.Contracts;

public sealed record SyncBatch
{
    public required string BatchId { get; init; }
    public required string BranchCode { get; init; }
    public required DateTime RangeStart { get; init; }
    public required DateTime RangeEnd { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
    public required string AgentVersion { get; init; }
    public required bool ReconciliationOk { get; init; }
    public List<SaleHeader> Sales { get; init; } = [];
    public List<SaleLine> Lines { get; init; } = [];
    public List<SalePayment> Payments { get; init; } = [];
    public bool TransientSnapshotComplete { get; init; }
    public List<TransientSaleHeader> TransientSales { get; init; } = [];
    public List<TransientSaleLine> TransientLines { get; init; } = [];
    public List<TransientSalePayment> TransientPayments { get; init; } = [];
    public List<Shift> Shifts { get; init; } = [];
    public List<CashierDeclaration> CashierDeclarations { get; init; } = [];
    public List<CashMovement> CashMovements { get; init; } = [];
    public List<CancellationSummary> Cancellations { get; init; } = [];
    public List<ReconciliationCheck> Reconciliation { get; init; } = [];
}

public sealed record CancellationSummary
{
    public required string SnapshotKey { get; init; }
    public DateTime Date { get; init; }
    public long? SourceFolio { get; init; }
    public string? User { get; init; }
    public string? ProductId { get; init; }
    public string? Description { get; init; }
    public decimal? Quantity { get; init; }
    public decimal? Price { get; init; }
    public string? Reason { get; init; }
    public int Occurrences { get; init; }
}

public sealed record ReconciliationCheck
{
    public required string Name { get; init; }
    public decimal Extracted { get; init; }
    public decimal Control { get; init; }
    public bool Match { get; init; }
}
