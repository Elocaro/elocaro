namespace MietHahn.Application.Setup;

/// <summary>
/// Example data for trying MietHahn out: one house with four apartments, three tenants with different payment behaviour and a landlord
/// profile. Only on an empty installation; there is no "remove" because the data is meant for a throw-away instance
/// (reset the database to start over).
/// </summary>
public interface IDemoData
{
    /// <summary>Whether there is nothing yet (no property, person or lease), so the example data may be loaded.</summary>
    Task<bool> CanLoadAsync(CancellationToken cancellationToken = default);

    Task<DataImportResult> LoadAsync(CancellationToken cancellationToken = default);
}
