using Elocaro.Domain.Common;

namespace Elocaro.Domain.Statements;

/// <summary>
/// The landlord releases a receipt for the tenants of a released statement (portal: inspection of receipts). By default nothing
/// is released. Who released it and when is the audit data of the entity (<see cref="Entity.CreatedBy"/>, <see cref="Entity.CreatedAt"/>);
/// withdrawing the release removes the entity (logical deletion by the interceptor), a later release creates a new one.
/// </summary>
public sealed class StatementReceiptShare : Entity
{
    private StatementReceiptShare()
    {
    }

    public StatementReceiptShare(Guid statementId, Guid receiptId)
    {
        StatementId = statementId;
        ReceiptId = receiptId;
    }

    public Guid StatementId { get; private set; }

    public Guid ReceiptId { get; private set; }
}
