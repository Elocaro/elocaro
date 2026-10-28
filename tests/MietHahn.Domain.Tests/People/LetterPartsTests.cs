using MietHahn.Domain.People;
using MietHahn.Domain.Properties;

namespace MietHahn.Domain.Tests.People;

public class LetterPartsTests
{
    private static readonly DateOnly Today = new(2026, 10, 28);

    [Fact]
    public void Address_block_uses_the_address_of_the_person()
    {
        var tenant = new Person("Schmidt", "Anna");
        tenant.SetAddress(new DateOnly(2020, 5, 1), "Altweg 1", "50667", "Köln");

        var rows = LetterParts.AddressRowsOf([tenant], "Wohnung 1", new Property("Haus", "Lindenweg 3", "50667", "Köln"), Today);

        Assert.Equal(["Anna Schmidt", "Altweg 1", "50667 Köln"], rows);
    }

    [Fact]
    public void Without_an_address_the_block_names_the_rented_property()
    {
        var tenant = new Person("Schmidt", "Anna");

        var rows = LetterParts.AddressRowsOf([tenant], "Wohnung 1", new Property("Haus", "Lindenweg 3", "50667", "Köln"), Today);

        Assert.Equal(["Anna Schmidt", "Wohnung 1", "Lindenweg 3", "50667 Köln"], rows);
    }

    [Fact]
    public void More_than_three_names_are_shortened()
    {
        var people = Enumerable.Range(1, 5).Select(i => new Person("Mieter" + i)).ToList();

        var rows = LetterParts.AddressRowsOf(people, null, null, Today);

        Assert.Equal(["Mieter1", "Mieter2", "und weitere"], rows);
    }

    [Theory]
    [InlineData(Salutation.Mrs, "Sehr geehrte Frau Schmidt,")]
    [InlineData(Salutation.Mr, "Sehr geehrter Herr Schmidt,")]
    [InlineData(Salutation.None, "Sehr geehrte Damen und Herren,")]
    [InlineData(Salutation.Diverse, "Sehr geehrte Damen und Herren,")]
    public void A_single_person_is_addressed_by_salutation(Salutation salutation, string expected)
    {
        Assert.Equal(expected, LetterParts.SalutationOf([new Person("Schmidt") { Salutation = salutation }]));
    }

    [Fact]
    public void Several_people_or_a_company_get_the_neutral_salutation()
    {
        var a = new Person("Schmidt") { Salutation = Salutation.Mrs };
        var b = new Person("Weber") { Salutation = Salutation.Mr };

        Assert.Equal("Sehr geehrte Damen und Herren,", LetterParts.SalutationOf([a, b]));
        Assert.Equal("Sehr geehrte Damen und Herren,", LetterParts.SalutationOf([new Person("Firma") { Salutation = Salutation.Mr, Company = "Firma GmbH" }]));
    }
}
