using Elocaro.Application.Documents;
using Elocaro.Application.Files;
using Elocaro.Application.People;
using Elocaro.Application.Todos;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Infrastructure.Documents;

/// <summary>Registrations of the Documents module.</summary>
internal static class DocumentsRegistration
{
    internal static IServiceCollection AddDocumentsModule(this IServiceCollection services)
    {
        services.AddScoped<IDocumentLibrary, DocumentLibrary>();
        services.AddScoped<IDocumentTemplates, DocumentTemplates>();
        services.AddScoped<IFileShare, DocumentFileShare>();
        services.AddScoped<ITodoSource, DocumentExpiryTodos>();
        services.AddScoped<IPersonalDataSource, DocumentPersonalData>();

        return services;
    }
}
