namespace MietHahn.Domain.Common;

/// <summary>Entities with this attribute are not written to the audit log (technical data, e.g. outbox).</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class NoAuditAttribute : Attribute;

/// <summary>The value of this property appears masked only in the audit log (e.g. IBAN, credentials).</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ConfidentialAttribute : Attribute;

/// <summary>
/// Entity or property is not written to the full export (secrets such as keys or tokens).
/// Business data always belongs in the export, even if it carries <see cref="ConfidentialAttribute"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property, Inherited = false)]
public sealed class NoExportAttribute : Attribute;
