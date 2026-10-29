using Elocaro.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elocaro.Infrastructure.Persistence.Configurations;

internal sealed class PushSubscriptionConfiguration : IEntityTypeConfiguration<PushSubscription>
{
    public void Configure(EntityTypeBuilder<PushSubscription> builder)
    {
        builder.ToTable("push_subscriptions");
        builder.Property(s => s.Endpoint).HasMaxLength(1000);
        builder.Property(s => s.P256dh).HasMaxLength(200);
        builder.Property(s => s.Auth).HasMaxLength(100);
        builder.Property(s => s.UserAgent).HasMaxLength(300);
        builder.HasIndex(s => s.Endpoint).IsUnique();
        builder.HasIndex(s => s.UserId);
    }
}

internal sealed class PushMessageConfiguration : IEntityTypeConfiguration<PushMessage>
{
    public void Configure(EntityTypeBuilder<PushMessage> builder)
    {
        builder.ToTable("push_messages");
        builder.Property(m => m.Title).HasMaxLength(100);
        builder.Property(m => m.Body).HasMaxLength(200);
        builder.Property(m => m.Url).HasMaxLength(300);
        builder.HasIndex(m => new { m.UserId, m.DeliveredAt });
    }
}

internal sealed class PushKeysConfiguration : IEntityTypeConfiguration<PushKeys>
{
    public void Configure(EntityTypeBuilder<PushKeys> builder)
    {
        builder.ToTable("push_keys");
        builder.Property(k => k.PublicKey).HasMaxLength(200);
        builder.Property(k => k.ProtectedPrivateKey).HasMaxLength(2000);
    }
}
