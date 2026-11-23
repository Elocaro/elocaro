using Elocaro.Domain.Payments;

namespace Elocaro.Domain.Tests.Payments;

public class DirectDebitMandateTests
{
    private static DirectDebitMandate Mandate(DateOnly signedOn) =>
        new(Guid.NewGuid(), "MND-1", "Anna Schütz", "DE02120300000000202051", null, signedOn, MandateSequence.Recurring);

    [Fact]
    public void Without_a_collection_the_mandate_lapses_36_months_after_the_signature()
    {
        var mandate = Mandate(new DateOnly(2023, 1, 15));

        Assert.True(mandate.IsActiveOn(new DateOnly(2026, 1, 14)));
        Assert.False(mandate.IsActiveOn(new DateOnly(2026, 1, 15)));
    }

    [Fact]
    public void A_collection_extends_the_mandate_by_36_months_and_an_earlier_one_does_not_shorten_it()
    {
        var mandate = Mandate(new DateOnly(2023, 1, 15));

        mandate.RecordCollection(new DateOnly(2025, 6, 1));
        mandate.RecordCollection(new DateOnly(2024, 6, 1));

        Assert.Equal(new DateOnly(2025, 6, 1), mandate.LastCollectedOn);
        Assert.Equal(new DateOnly(2028, 6, 1), mandate.ExpiresOn);
        Assert.True(mandate.IsActiveOn(new DateOnly(2028, 5, 31)));
        Assert.False(mandate.IsActiveOn(new DateOnly(2028, 6, 1)));
    }
}
