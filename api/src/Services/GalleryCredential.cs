using Azure.Identity;
using Microsoft.Extensions.Configuration;

namespace WorkflowGallery.Api.Services;

internal static class GalleryCredential
{
    internal static DefaultAzureCredential Create(IConfiguration configuration) =>
        new(new DefaultAzureCredentialOptions
        {
            ManagedIdentityClientId = configuration["AZURE_CLIENT_ID"],
        });
}
