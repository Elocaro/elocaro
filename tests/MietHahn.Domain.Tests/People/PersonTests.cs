using MietHahn.Domain.Common;
using MietHahn.Domain.People;

namespace MietHahn.Domain.Tests.People;

public class PersonTests
{
    [Fact]
    public void Display_name_with_title_and_company()
    {
        Assert.Equal("Dr. Anna Schmidt", new Person("Schmidt", "Anna") { Title = "Dr." }.DisplayName);
        Assert.Equal("Schmidt, Anna", new Person("Schmidt", "Anna").SortName);
        Assert.Equal("Stadtwerke Köln GmbH (Kundenservice)", new Person("Kundenservice") { Company = "Stadtwerke Köln GmbH" }.DisplayName);
    }

    [Fact]
    public void Relocation_keeps_previous_address_and_returns_new_address_after_move_out()
    {
        var tenant = new Person("Schmidt", "Anna");
        tenant.SetAddress(new DateOnly(2020, 5, 1), "Altweg 1", "50667", "Köln");
        tenant.SetAddress(new DateOnly(2026, 3, 1), "Lindenweg 3", "50667", "Köln", "Wohnung 1");
        tenant.SetAddress(new DateOnly(2027, 8, 1), "Neue Straße 5", "10115", "Berlin");

        Assert.Equal("Altweg 1, 50667 Köln", tenant.AddressAt(new DateOnly(2026, 2, 28))!.SingleLine);
        Assert.Equal("Wohnung 1, Lindenweg 3, 50667 Köln", tenant.AddressAt(new DateOnly(2026, 3, 1))!.SingleLine);
        // Statement for 2027, sent in 2028: goes to the new address
        Assert.Equal("Neue Straße 5, 10115 Berlin", tenant.AddressAt(new DateOnly(2028, 5, 1))!.SingleLine);
        Assert.Equal(3, tenant.Addresses.Count);
    }

    [Fact]
    public void Multiple_IBANs_valid_and_without_duplicates()
    {
        var tenant = new Person("Schmidt", "Anna");

        tenant.AddBankDetails("de02120300000000202051");
        tenant.AddBankDetails("DE89 3704 0044 0532 0130 00", "Hans Schmidt (Vater)", "cobadeffxxx");

        Assert.Equal(["DE02 1203 0000 0000 2020 51", "DE89 3704 0044 0532 0130 00"], tenant.BankDetails.Select(b => b.Iban));
        Assert.Equal("COBADEFFXXX", tenant.BankDetails[1].Bic);
        Assert.Throws<RuleViolationException>(() => tenant.AddBankDetails("DE02120300000000202051"));
        var errors = Assert.Throws<RuleViolationException>(() => tenant.AddBankDetails("DE02120300000000202052"));
        Assert.Contains("keine gültige IBAN", errors.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Anonymize_removes_all_personal_data()
    {
        var tenant = new Person("Schmidt", "Anna")
        {
            Salutation = Salutation.Mrs,
            Title = "Dr.",
            Email = "anna@example.de",
            Phone = "0221 123",
            Mobile = "0170 123",
            DateOfBirth = new DateOnly(1990, 1, 1),
            Notes = "Hund",
            Company = "Schmidt GmbH",
            Occupation = "IT",
            UserId = Guid.CreateVersion7(),
        };
        tenant.SetAddress(new DateOnly(2026, 1, 1), "Lindenweg 3", "50667", "Köln");
        tenant.AddBankDetails("DE02120300000000202051");
        var timestamp = new DateTimeOffset(2037, 1, 1, 0, 0, 0, TimeSpan.Zero);

        tenant.Anonymize(timestamp);

        Assert.True(tenant.IsAnonymized);
        Assert.Equal(timestamp, tenant.AnonymizedAt);
        Assert.StartsWith("Anonymisiert ", tenant.LastName, StringComparison.Ordinal);
        Assert.DoesNotContain("Schmidt", tenant.DisplayName, StringComparison.Ordinal);
        Assert.All(new object?[] { tenant.FirstName, tenant.Title, tenant.Email, tenant.Phone, tenant.Mobile, tenant.DateOfBirth, tenant.Notes, tenant.Company, tenant.Occupation, tenant.UserId }, Assert.Null);
        Assert.Empty(tenant.Addresses);
        Assert.Empty(tenant.BankDetails);
        Assert.Throws<RuleViolationException>(() => tenant.SetAddress(new DateOnly(2037, 2, 1), "X", "1", "Y"));
    }
}
