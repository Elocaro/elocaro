using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using MietHahn.Domain.Common;

namespace MietHahn.Infrastructure.Persistence.Conventions;

/// <summary>Shared mapping of all <see cref="Entity"/> types.</summary>
internal static class EntityConventions
{
    /// <summary>Name of the global filter that hides soft-deleted records.</summary>
    public const string SoftDeleteFilter = "SoftDelete";

    public static void Apply(ModelBuilder modelBuilder)
    {
        var entityTypes = modelBuilder.Model.GetEntityTypes()
            .Where(t => typeof(Entity).IsAssignableFrom(t.ClrType) && t.BaseType is null && !t.IsOwned())
            .ToList();

        foreach (var type in entityTypes)
        {
            var builder = modelBuilder.Entity(type.ClrType);

            builder.HasKey(nameof(Entity.Id));
            builder.Property(nameof(Entity.Id)).ValueGeneratedNever();
            builder.Property(nameof(Entity.Version)).IsRowVersion();
            builder.Property(nameof(Entity.CreatedBy)).HasMaxLength(256);
            builder.Property(nameof(Entity.ModifiedBy)).HasMaxLength(256);

            builder.HasQueryFilter(SoftDeleteFilter, NotDeleted(type.ClrType));
        }
    }

    private static LambdaExpression NotDeleted(Type type)
    {
        var parameter = Expression.Parameter(type, "e");
        var deleted = Expression.Property(parameter, nameof(Entity.Deleted));
        return Expression.Lambda(Expression.Not(deleted), parameter);
    }
}
