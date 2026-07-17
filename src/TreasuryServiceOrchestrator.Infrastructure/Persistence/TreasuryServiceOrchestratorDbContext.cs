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
    }
}
