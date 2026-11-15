namespace Elocaro.Domain.Properties;

/// <summary>How the rooms of a shared flat take part in the operating cost statement.</summary>
public enum SharedFlatBilling
{
    /// <summary>Flat rate: the rooms get no statement, actual costs are compared with the flat-rate income internally.</summary>
    FlatRate = 0,

    /// <summary>The costs of the flat are distributed over the rooms by area; each room tenant gets a statement.</summary>
    ByRoom = 1,
}
