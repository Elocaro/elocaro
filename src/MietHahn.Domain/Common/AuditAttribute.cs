namespace MietHahn.Domain.Common;

/// <summary>Entitäten mit diesem Attribut werden nicht ins Audit-Log geschrieben (technische Daten, z. B. Outbox).</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class NoAuditAttribute : Attribute;

/// <summary>Der Wert dieser Eigenschaft erscheint im Audit-Log nur maskiert (z. B. IBAN, Zugangsdaten).</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ConfidentialAttribute : Attribute;

/// <summary>
/// Entität bzw. Eigenschaft wird nicht in den Vollexport geschrieben (Geheimnisse wie Schlüssel oder Tokens).
/// Fachliche Daten gehören immer in den Export, auch wenn sie <see cref="ConfidentialAttribute"/> tragen.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property, Inherited = false)]
public sealed class NoExportAttribute : Attribute;
