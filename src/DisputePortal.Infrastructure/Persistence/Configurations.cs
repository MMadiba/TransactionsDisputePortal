using DisputePortal.Domain.Disputes;
using DisputePortal.Domain.Transactions;
using DisputePortal.Infrastructure.Authentication;
using DisputePortal.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DisputePortal.Infrastructure.Persistence;

internal sealed class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> b)
    {
        b.ToTable("Transactions");
        b.HasKey(t => t.Id);
        b.Ignore(t => t.DomainEvents);

        b.Property(t => t.CustomerId).IsRequired();
        b.Property(t => t.Reference).HasMaxLength(32).IsRequired();
        b.Property(t => t.Merchant).HasMaxLength(120).IsRequired();
        b.Property(t => t.Description).HasMaxLength(250).IsRequired();
        b.Property(t => t.CardLast4).HasMaxLength(4).IsFixedLength().IsRequired();
        b.Property(t => t.Status).HasConversion<string>().HasMaxLength(16);

        // Money is a value object: mapped as an EF Core complex type, flattened into two columns.
        b.ComplexProperty(t => t.Amount, money =>
        {
            money.Property(m => m.AmountMinor).HasColumnName("AmountMinor");
            money.Property(m => m.Currency).HasColumnName("Currency").HasMaxLength(3).IsFixedLength();
        });

        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(t => t.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(t => t.Reference).IsUnique();
        b.HasIndex(t => new { t.CustomerId, t.PostedAtUtc });
    }
}

internal sealed class DisputeConfiguration : IEntityTypeConfiguration<Dispute>
{
    public void Configure(EntityTypeBuilder<Dispute> b)
    {
        b.ToTable("Disputes");
        b.HasKey(d => d.Id);
        b.Ignore(d => d.DomainEvents);
        b.Ignore(d => d.IsOpen);
        b.Ignore(d => d.BlocksNewDisputes);

        b.Property(d => d.Reference).HasMaxLength(DisputeReference.MaxLength).IsRequired();
        b.Property(d => d.CustomerId).IsRequired();
        b.Property(d => d.Details).HasMaxLength(DisputeLimits.DetailsMaxLength).IsRequired();
        b.Property(d => d.ResolutionNote).HasMaxLength(DisputeLimits.ResolutionNoteMaxLength);
        b.Property(d => d.ReviewerName).HasMaxLength(200);
        b.Property(d => d.Reason).HasConversion<string>().HasMaxLength(40);
        b.Property(d => d.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(d => d.Version).IsConcurrencyToken();

        // Aggregates reference each other by id only, never by navigation.
        b.HasOne<Transaction>().WithMany().HasForeignKey(d => d.TransactionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(d => d.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(d => d.ReviewerId).OnDelete(DeleteBehavior.Restrict);

        b.HasMany(d => d.History).WithOne().HasForeignKey(h => h.DisputeId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(d => d.History).UsePropertyAccessMode(PropertyAccessMode.Field);

        b.HasIndex(d => d.Reference).IsUnique();
        b.HasIndex(d => new { d.CustomerId, d.CreatedAtUtc });
        b.HasIndex(d => new { d.Status, d.CreatedAtUtc });

        // The database is the final word on "one non-withdrawn dispute per transaction" (Dispute.BlocksNewDisputes):
        // even if two requests race past the aggregate's check, only one insert can win.
        b.HasIndex(d => d.TransactionId)
            .IsUnique()
            .HasFilter($"\"Status\" <> '{nameof(DisputeStatus.Withdrawn)}'")
            .HasDatabaseName("IX_Disputes_TransactionId_NotWithdrawn");
    }
}

internal sealed class DisputeHistoryEntryConfiguration : IEntityTypeConfiguration<DisputeHistoryEntry>
{
    public void Configure(EntityTypeBuilder<DisputeHistoryEntry> b)
    {
        b.ToTable("DisputeHistory");
        b.HasKey(h => h.Id);
        b.Property(h => h.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(h => h.ActorRole).HasConversion<string>().HasMaxLength(16);
        b.Property(h => h.ActorUserId).IsRequired();
        b.Property(h => h.ActorName).HasMaxLength(120).IsRequired();
        b.Property(h => h.Note).HasMaxLength(DisputeLimits.ResolutionNoteMaxLength);
        b.HasIndex(h => new { h.DisputeId, h.OccurredAtUtc });
    }
}

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.ToTable("RefreshTokens");
        b.HasKey(r => r.Id);
        b.Property(r => r.TokenHash).HasMaxLength(64).IsFixedLength().IsRequired();
        b.Property(r => r.ReplacedByTokenHash).HasMaxLength(64).IsFixedLength();
        b.HasIndex(r => r.TokenHash).IsUnique();
        b.HasIndex(r => r.UserId);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
