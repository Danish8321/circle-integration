using Microsoft.EntityFrameworkCore;
using TreasuryServiceOrchestrator.Application.Webhooks;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Infrastructure.Persistence;

public class TreasuryServiceOrchestratorDbContext(DbContextOptions<TreasuryServiceOrchestratorDbContext> options)
    : DbContext(options)
{
    public DbSet<SubAccount> SubAccounts => Set<SubAccount>();
    public DbSet<EntityRegistration> EntityRegistrations => Set<EntityRegistration>();
    public DbSet<AuditRecord> AuditRecords => Set<AuditRecord>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<WebhookInboxEntry> WebhookInboxEntries => Set<WebhookInboxEntry>();
    public DbSet<DepositAddress> DepositAddresses => Set<DepositAddress>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<BalanceSnapshot> BalanceSnapshots => Set<BalanceSnapshot>();
    public DbSet<FundAccount> FundAccounts => Set<FundAccount>();
    public DbSet<Recipient> Recipients => Set<Recipient>();
    public DbSet<Transfer> Transfers => Set<Transfer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SubAccount>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ClientCompanyId).IsRequired().HasMaxLength(64);
            entity.HasIndex(x => x.ClientCompanyId).IsUnique();
            entity.Property(x => x.CircleWalletId).HasMaxLength(64);
        });

        modelBuilder.Entity<EntityRegistration>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ClientCompanyId).IsRequired().HasMaxLength(64);
            entity.Property(x => x.BusinessName).IsRequired().HasMaxLength(200);
            entity.Property(x => x.CircleWalletId).HasMaxLength(64);
            entity.HasIndex(x => x.SubAccountId);
        });

        modelBuilder.Entity<AuditRecord>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.EventType).IsRequired().HasMaxLength(100);
            entity.Property(x => x.ClientCompanyId).IsRequired().HasMaxLength(64);
        });

        modelBuilder.Entity<IdempotencyRecord>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).IsRequired().HasMaxLength(64);
            entity.Property(x => x.IdempotencyKey).IsRequired().HasMaxLength(128);
            entity.Property(x => x.RequestHash).IsRequired().HasMaxLength(64);
            entity.HasIndex(x => new { x.TenantId, x.IdempotencyKey }).IsUnique();
        });

        modelBuilder.Entity<WebhookInboxEntry>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Topic).IsRequired().HasMaxLength(64);
            entity.Property(x => x.CircleEventId).IsRequired().HasMaxLength(128);
            entity.HasIndex(x => x.CircleEventId).IsUnique();
            entity.Property(x => x.ProcessingResult).HasMaxLength(16);
        });

        modelBuilder.Entity<DepositAddress>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Chain).IsRequired().HasMaxLength(32);
            entity.Property(x => x.Currency).IsRequired().HasMaxLength(16);
            entity.Property(x => x.Address).IsRequired().HasMaxLength(128);
            entity.Property(x => x.CircleAddressId).HasMaxLength(64);
            entity.HasIndex(x => new { x.SubAccountId, x.Chain, x.Currency }).IsUnique();
        });

        ConfigureLedgerEntities(modelBuilder);
        ConfigureRecipientAndTransferEntities(modelBuilder);
    }

    private static void ConfigureLedgerEntities(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Transaction>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ClientCompanyId).IsRequired().HasMaxLength(64);
            entity.Property(x => x.ProviderReferenceId).IsRequired().HasMaxLength(128);
            entity.Property(x => x.CorrelationId).IsRequired().HasMaxLength(128);
            entity.Property(x => x.FailureReason).HasMaxLength(500);
            entity.HasIndex(x => x.ProviderReferenceId).IsUnique();
            entity.HasIndex(x => new { x.SubAccountId, x.ClientCompanyId });
            entity.ComplexProperty(x => x.Amount, amount =>
            {
                amount.Property(x => x.Amount).HasColumnName("Amount").HasPrecision(28, 8);
                amount.Property(x => x.CurrencyCode).HasColumnName("CurrencyCode").IsRequired().HasMaxLength(16);
            });
        });

        modelBuilder.Entity<BalanceSnapshot>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ClientCompanyId).IsRequired().HasMaxLength(64);
            entity.HasIndex(x => new { x.SubAccountId, x.ClientCompanyId });
            entity.ComplexProperty(x => x.Balance, balance =>
            {
                balance.Property(x => x.Amount).HasColumnName("Balance").HasPrecision(28, 8);
                balance.Property(x => x.CurrencyCode).HasColumnName("CurrencyCode").IsRequired().HasMaxLength(16);
            });
        });

        modelBuilder.Entity<FundAccount>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ClientCompanyId).IsRequired().HasMaxLength(64);
            entity.HasIndex(x => x.ClientCompanyId).IsUnique();
            entity.ComplexProperty(x => x.Balance, balance =>
            {
                balance.Property(x => x.Amount).HasColumnName("Balance").HasPrecision(28, 8);
                balance.Property(x => x.CurrencyCode).HasColumnName("CurrencyCode").IsRequired().HasMaxLength(16);
            });
        });

    }

    private static void ConfigureRecipientAndTransferEntities(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Recipient>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ClientCompanyId).IsRequired().HasMaxLength(64);
            entity.Property(x => x.Chain).IsRequired().HasMaxLength(32);
            entity.Property(x => x.Address).IsRequired().HasMaxLength(128);
            entity.Property(x => x.Label).IsRequired().HasMaxLength(200);
            entity.Property(x => x.CircleRecipientId).HasMaxLength(64);
            entity.Property(x => x.DenialReason).HasMaxLength(500);
            entity.HasIndex(x => x.CircleRecipientId);
            entity.HasIndex(x => new { x.SubAccountId, x.ClientCompanyId });
        });

        modelBuilder.Entity<Transfer>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ClientCompanyId).IsRequired().HasMaxLength(64);
            entity.Property(x => x.CircleTransferId).HasMaxLength(64);
            entity.Property(x => x.FailureReason).HasMaxLength(500);
            entity.Property(x => x.CorrelationId).IsRequired().HasMaxLength(128);
            entity.HasIndex(x => x.CircleTransferId);
            entity.HasIndex(x => new { x.SubAccountId, x.ClientCompanyId });
            entity.ComplexProperty(x => x.Amount, amount =>
            {
                amount.Property(x => x.Amount).HasColumnName("Amount").HasPrecision(28, 8);
                amount.Property(x => x.CurrencyCode).HasColumnName("CurrencyCode").IsRequired().HasMaxLength(16);
            });
        });
    }
}
