namespace MietHahn.Domain.Gemeinsam;

/// <summary>Entitäten mit diesem Attribut werden nicht ins Audit-Log geschrieben (technische Daten, z. B. Outbox).</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class OhneAuditAttribute : Attribute;

/// <summary>Der Wert dieser Eigenschaft erscheint im Audit-Log nur maskiert (z. B. IBAN, Zugangsdaten).</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class VertraulichAttribute : Attribute;
