using MassTransit;
using Microsoft.EntityFrameworkCore;
using Seamline.SharedKernel;

namespace Seamline.Modules.Trading.Internal;

internal sealed class TradingDbContext(DbContextOptions<TradingDbContext> options, ITenantContext tenantContext)
    : DbContext(options)
{
    public const string Schema = "trading";

    public DbSet<Trade> Trades => Set<Trade>();
    public DbSet<TradeHistory> TradeHistory => Set<TradeHistory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Trade>(builder =>
        {
            builder.ToTable("trade");
            builder.HasKey(t => t.Id);
            builder.Property(t => t.TenantId)
                .HasConversion(t => t.Value, v => new TenantId(v))
                .HasColumnName("tenant_id");
            builder.Property(t => t.CommodityCode).HasColumnName("commodity_code").HasMaxLength(20).IsRequired();
            builder.Property(t => t.DeliveryPeriod).HasColumnName("delivery_period").HasMaxLength(7).IsRequired();
            builder.Property(t => t.Direction).HasColumnName("direction").HasConversion<string>().HasMaxLength(10);
            builder.Property(t => t.Volume).HasColumnName("volume").HasPrecision(18, 3);
            builder.Property(t => t.Price).HasColumnName("price").HasPrecision(18, 4);
            builder.Property(t => t.CounterpartyId).HasColumnName("counterparty_id");
            builder.Property(t => t.State).HasColumnName("state").HasConversion<string>().HasMaxLength(20);
            builder.Property(t => t.Version).HasColumnName("version");

            builder.HasQueryFilter(t => t.TenantId == tenantContext.TenantId);
        });

        // Append-only: no ValidTo column, no update path anywhere in the app.
        // seamline_app is granted SELECT/INSERT only on this table (see the
        // migration that creates it) — the DB refuses UPDATE/DELETE even if
        // application code ever tried. See ADR-0006.
        modelBuilder.Entity<TradeHistory>(builder =>
        {
            builder.ToTable("trade_history");
            builder.HasKey(h => h.Id);
            builder.Property(h => h.TradeId).HasColumnName("trade_id");
            builder.Property(h => h.TenantId)
                .HasConversion(t => t.Value, v => new TenantId(v))
                .HasColumnName("tenant_id");
            builder.Property(h => h.Version).HasColumnName("version");
            builder.Property(h => h.ValidFrom).HasColumnName("valid_from");
            builder.Property(h => h.ChangedBy).HasColumnName("changed_by").HasMaxLength(100).IsRequired();
            builder.Property(h => h.ChangeReason).HasColumnName("change_reason").HasMaxLength(500).IsRequired();
            builder.Property(h => h.CommodityCode).HasColumnName("commodity_code").HasMaxLength(20).IsRequired();
            builder.Property(h => h.DeliveryPeriod).HasColumnName("delivery_period").HasMaxLength(7).IsRequired();
            builder.Property(h => h.Direction).HasColumnName("direction").HasConversion<string>().HasMaxLength(10);
            builder.Property(h => h.Volume).HasColumnName("volume").HasPrecision(18, 3);
            builder.Property(h => h.Price).HasColumnName("price").HasPrecision(18, 4);
            builder.Property(h => h.CounterpartyId).HasColumnName("counterparty_id");
            builder.Property(h => h.State).HasColumnName("state").HasConversion<string>().HasMaxLength(20);

            builder.HasIndex(h => new { h.TradeId, h.Version }).IsUnique();
            builder.HasQueryFilter(h => h.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<TradeApprovalState>(builder =>
        {
            builder.ToTable("trade_approval_saga");
            builder.HasKey(s => s.CorrelationId);
            builder.Property(s => s.CurrentState).HasColumnName("current_state").HasMaxLength(64);
            builder.Property(s => s.TenantId).HasColumnName("tenant_id");
            builder.Property(s => s.CounterpartyId).HasColumnName("counterparty_id");
            builder.Property(s => s.Notional).HasColumnName("notional").HasPrecision(18, 4);
            builder.Property(s => s.ApprovalTimeoutTokenId).HasColumnName("approval_timeout_token_id");

            // Postgres has no native rowversion column like SQL Server —
            // xmin is a system column every row already has, so a shadow
            // property mapped onto it is the idiomatic Npgsql concurrency
            // token. No DDL needed to create it.
            builder.Property<uint>("xmin").HasColumnName("xmin").IsRowVersion();
        });

        // Transactional outbox: TradeActivated/TradeRejected are written in
        // the same transaction as the trade's state change, then dispatched
        // by MassTransit's bus outbox delivery service. See ADR-0004
        // (outbox), when written. Tables live in messaging, not trading —
        // ADR-0008 calls messaging out as the one cross-cutting schema in
        // the system (transport, not domain); this DbContext just happens
        // to be the one MassTransit is wired to for Trading's outbox.
        const string messagingSchema = "messaging";
        modelBuilder.AddInboxStateEntity(e => e.ToTable("InboxState", messagingSchema));
        modelBuilder.AddOutboxMessageEntity(e => e.ToTable("OutboxMessage", messagingSchema));
        modelBuilder.AddOutboxStateEntity(e => e.ToTable("OutboxState", messagingSchema));
    }
}
