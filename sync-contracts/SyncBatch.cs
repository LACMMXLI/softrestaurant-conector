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
    public List<ProductCatalogItem> Products { get; init; } = [];
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
    // Snapshot normalizado de productos cancelados. La tabla temporal se envía completa en
    // cada ciclo; el histórico se re-lee en la ventana incremental del lote.
    public bool TransientCancellationsSnapshotComplete { get; init; }
    public List<ProductCancellationEvent> ProductCancellations { get; init; } = [];
    public List<ReconciliationCheck> Reconciliation { get; init; } = [];
}

public sealed record ProductCancellationEvent
{
    public required string EventKey { get; init; }
    public required string SourceKind { get; init; } // HISTORICAL | TRANSIENT
    public DateTime? CancelledAt { get; init; }
    public long? SourceFolio { get; init; }
    public long? SourceTempFolio { get; init; }
    public string? SaleDetailId { get; init; }
    public string? Comanda { get; init; }
    public string? ProductId { get; init; }
    public string? Description { get; init; }
    public decimal? Quantity { get; init; }
    public decimal? UnitPrice { get; init; }
    public string? User { get; init; }
    public string? Reason { get; init; }
    public string? ReasonId { get; init; }
    public string? ReasonDescription { get; init; }
    public int? ShiftId { get; init; }
    public string? AreaId { get; init; }
    public string? AreaDescription { get; init; }
    public string? CompanyId { get; init; }
    public string? CompanyName { get; init; }
    public DateTime? AccountOpenedAt { get; init; }
    public DateTime? AccountClosedAt { get; init; }
    public bool? AccountPaid { get; init; }
    public bool? AccountCancelled { get; init; }
    public decimal? AccountFinalTotal { get; init; }
    public int SourceDuplicateCount { get; init; } = 1;
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
