using Microsoft.EntityFrameworkCore;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Infrastructure.Persistence;

public class TreasuryServiceOrchestratorDbContext(DbContextOptions<TreasuryServiceOrchestratorDbContext> options)
    : DbContext(options)
{
    public DbSet<SubAccount> SubAccounts => Set<SubAccount>();
    public DbSet<EntityRegistration> EntityRegistrations => Set<EntityRegistration>();
    public DbSet<AuditRecord> AuditRecords => Set<AuditRecord>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

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
    }
}
