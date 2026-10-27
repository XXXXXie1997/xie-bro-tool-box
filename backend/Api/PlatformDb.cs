global using MySQLModelBuilderExtensions = MySql.EntityFrameworkCore.Extensions.MySQLModelBuilderExtensions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace XieBox.Api;

public sealed class PlatformUser : IdentityUser
{
    public string? PublicId { get; set; }
    public bool Disabled { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
public sealed class PlatformSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = "";
    public string SecurityStamp { get; set; } = "";
    public string AccessHash { get; set; } = "";
    public string RefreshHash { get; set; } = "";
    public string Purpose { get; set; } = "desktop";
    public DateTime AccessExpiresAt { get; set; }
    public DateTime RefreshExpiresAt { get; set; }
    public bool Revoked { get; set; }
}
public sealed class ConsumedRefresh
{
    public string Hash { get; set; } = "";
    public string SessionId { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
}
public sealed class ModuleRecord
{
    public string Id { get; set; } = "";
    public string DefinitionJson { get; set; } = "";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
public sealed class AuditEntry
{
    public long Id { get; set; }
    public string ActorId { get; set; } = "";
    public string Action { get; set; } = "";
    public string TargetId { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
public sealed class PendingRegistration
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Email { get; set; } = "";
    public string NormalizedEmail { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string ProtectedCode { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public DateTime LastSentAt { get; set; }
    public int Attempts { get; set; }
}
public sealed class EmailChallenge
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Purpose { get; set; } = "";
    public string Email { get; set; } = "";
    public string? UserId { get; set; }
    public string? PasswordHash { get; set; }
    public string ProtectedCode { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public int Attempts { get; set; }
    public bool Consumed { get; set; }
}
public sealed class PlatformDb(DbContextOptions<PlatformDb> options) : IdentityDbContext<PlatformUser>(options)
{
    public DbSet<EmailChallenge> EmailChallenges => Set<EmailChallenge>();
    public DbSet<PlatformSession> Sessions => Set<PlatformSession>();
    public DbSet<ConsumedRefresh> ConsumedRefreshes => Set<ConsumedRefresh>();
    public DbSet<ModuleRecord> Modules => Set<ModuleRecord>();
    public DbSet<AuditEntry> Audit => Set<AuditEntry>();
    public DbSet<PendingRegistration> PendingRegistrations => Set<PendingRegistration>();
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        MySQLModelBuilderExtensions.HasCharSet(builder, "utf8mb4");
        MySQLModelBuilderExtensions.UseCollation(builder, "utf8mb4_bin");
        builder.Entity<PlatformUser>().Property(x => x.Id).HasMaxLength(128);
        builder.Entity<PlatformUser>().Property(x => x.PublicId).HasMaxLength(8);
        builder.Entity<PlatformUser>().HasIndex(x => x.PublicId).IsUnique();
        builder.Entity<PlatformUser>().HasIndex(x => x.NormalizedEmail).HasDatabaseName("EmailIndex").IsUnique();
        builder.Entity<EmailChallenge>().Property(x => x.Id).HasMaxLength(32);
        builder.Entity<EmailChallenge>().Property(x => x.Purpose).HasMaxLength(16);
        builder.Entity<EmailChallenge>().Property(x => x.Email).HasMaxLength(254);
        builder.Entity<EmailChallenge>().Property(x => x.UserId).HasMaxLength(128);
        builder.Entity<EmailChallenge>().HasIndex(x => new { x.Email, x.CreatedAt });
        builder.Entity<EmailChallenge>().HasIndex(x => x.CreatedAt);
        builder.Entity<IdentityRole>().Property(x => x.Id).HasMaxLength(128);
        builder.Entity<IdentityUserClaim<string>>().Property(x => x.UserId).HasMaxLength(128);
        builder.Entity<IdentityUserLogin<string>>().Property(x => x.UserId).HasMaxLength(128);
        builder.Entity<IdentityUserLogin<string>>().Property(x => x.LoginProvider).HasMaxLength(128);
        builder.Entity<IdentityUserLogin<string>>().Property(x => x.ProviderKey).HasMaxLength(128);
        builder.Entity<IdentityUserRole<string>>().Property(x => x.UserId).HasMaxLength(128);
        builder.Entity<IdentityUserRole<string>>().Property(x => x.RoleId).HasMaxLength(128);
        builder.Entity<IdentityRoleClaim<string>>().Property(x => x.RoleId).HasMaxLength(128);
        builder.Entity<IdentityUserToken<string>>().Property(x => x.UserId).HasMaxLength(128);
        builder.Entity<IdentityUserToken<string>>().Property(x => x.LoginProvider).HasMaxLength(128);
        builder.Entity<IdentityUserToken<string>>().Property(x => x.Name).HasMaxLength(128);
        builder.Entity<PlatformSession>().Property(x => x.Id).HasMaxLength(32);
        builder.Entity<PlatformSession>().Property(x => x.UserId).HasMaxLength(128);
        builder.Entity<PlatformSession>().Property(x => x.SecurityStamp).HasMaxLength(128);
        builder.Entity<PlatformSession>().Property(x => x.AccessHash).HasMaxLength(64);
        builder.Entity<PlatformSession>().Property(x => x.RefreshHash).HasMaxLength(64);
        builder.Entity<PlatformSession>().Property(x => x.Purpose).HasMaxLength(16);
        builder.Entity<ConsumedRefresh>().Property(x => x.Hash).HasMaxLength(64);
        builder.Entity<ConsumedRefresh>().Property(x => x.SessionId).HasMaxLength(32);
        builder.Entity<ModuleRecord>().Property(x => x.Id).HasMaxLength(64);
        builder.Entity<AuditEntry>().Property(x => x.ActorId).HasMaxLength(128);
        builder.Entity<AuditEntry>().Property(x => x.Action).HasMaxLength(64);
        builder.Entity<AuditEntry>().Property(x => x.TargetId).HasMaxLength(128);
        builder.Entity<PendingRegistration>().Property(x => x.Id).HasMaxLength(32);
        builder.Entity<PendingRegistration>().Property(x => x.Email).HasMaxLength(254);
        builder.Entity<PendingRegistration>().Property(x => x.NormalizedEmail).HasMaxLength(254);
        builder.Entity<PendingRegistration>().HasIndex(x => new { x.NormalizedEmail, x.LastSentAt });
        builder.Entity<PendingRegistration>().HasIndex(x => x.ExpiresAt);
        builder.Entity<PlatformSession>().HasIndex(x => x.AccessHash).IsUnique();
        builder.Entity<PlatformSession>().HasIndex(x => x.RefreshHash).IsUnique();
        builder.Entity<PlatformSession>().HasOne<PlatformUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<ConsumedRefresh>().HasKey(x => x.Hash);
        builder.Entity<ConsumedRefresh>().HasOne<PlatformSession>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
    }
}
