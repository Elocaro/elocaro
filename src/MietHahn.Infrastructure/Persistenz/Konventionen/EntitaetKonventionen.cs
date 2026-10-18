using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using MietHahn.Domain.Gemeinsam;

namespace MietHahn.Infrastructure.Persistenz.Konventionen;

/// <summary>Gemeinsame Abbildung aller <see cref="Entitaet"/>-Typen.</summary>
internal static class EntitaetKonventionen
{
    /// <summary>Name des globalen Filters, der logisch gelöschte Datensätze ausblendet.</summary>
    public const string SoftDeleteFilter = "SoftDelete";

    public static void Anwenden(ModelBuilder modelBuilder)
    {
        var entitaetTypen = modelBuilder.Model.GetEntityTypes()
            .Where(t => typeof(Entitaet).IsAssignableFrom(t.ClrType) && t.BaseType is null && !t.IsOwned())
            .ToList();

        foreach (var typ in entitaetTypen)
        {
            var builder = modelBuilder.Entity(typ.ClrType);

            builder.HasKey(nameof(Entitaet.Id));
            builder.Property(nameof(Entitaet.Id)).ValueGeneratedNever();
            builder.Property(nameof(Entitaet.Version)).IsRowVersion();
            builder.Property(nameof(Entitaet.ErstelltVon)).HasMaxLength(256);
            builder.Property(nameof(Entitaet.GeaendertVon)).HasMaxLength(256);

            builder.HasQueryFilter(SoftDeleteFilter, NichtGeloescht(typ.ClrType));
        }
    }

    private static LambdaExpression NichtGeloescht(Type typ)
    {
        var parameter = Expression.Parameter(typ, "e");
        var geloescht = Expression.Property(parameter, nameof(Entitaet.Geloescht));
        return Expression.Lambda(Expression.Not(geloescht), parameter);
    }
}
