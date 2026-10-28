using MietHahn.Domain.Properties;

namespace MietHahn.Domain.People;

/// <summary>Address block and salutation of letters and mails to the tenants of a tenancy.</summary>
public static class LetterParts
{
    /// <summary>The lines of the address window: names (at most three), then the address of the person on the day, else the rented property.</summary>
    public static IReadOnlyList<string> AddressRowsOf(IReadOnlyList<Person> people, string? unitName, Property? property, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(people);
        var names = people.Select(p => p.DisplayName).ToList();
        if (names.Count > 3)
        {
            names = [names[0], names[1], "und weitere"];
        }

        var address = people.Select(p => p.AddressAt(today)).FirstOrDefault(a => a is not null);
        var rows = new List<string>(names);
        if (address is not null)
        {
            if (!string.IsNullOrWhiteSpace(address.AddressLine2))
            {
                rows.Add(address.AddressLine2);
            }

            rows.Add(address.Street);
            rows.Add($"{address.PostalCode} {address.City}");
            if (address.Country != "Deutschland")
            {
                rows.Add(address.Country);
            }
        }
        else if (property is not null)
        {
            // Without an address of the person: the rented property itself.
            if (!string.IsNullOrWhiteSpace(unitName))
            {
                rows.Add(unitName);
            }

            rows.Add(property.Street);
            rows.Add($"{property.PostalCode} {property.City}");
        }

        return rows.Take(6).ToList();
    }

    /// <summary>"Sehr geehrte Frau …," for a single person with a known salutation, otherwise the neutral form.</summary>
    public static string SalutationOf(IReadOnlyList<Person> people)
    {
        ArgumentNullException.ThrowIfNull(people);
        return people is [{ Company: null, Salutation: var salutation } person] && salutation is Salutation.Mrs or Salutation.Mr
            ? (salutation == Salutation.Mrs ? "Sehr geehrte Frau " : "Sehr geehrter Herr ") + person.LastName + ","
            : "Sehr geehrte Damen und Herren,";
    }
}
