using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace WorkflowGallery.Api.Services;

internal static class BoundedJsonBody
{
    internal static async Task<T?> ReadAsync<T>(HttpRequest request, int maxBytes, JsonSerializerOptions options, CancellationToken cancellationToken)
    {
        if (request.ContentLength > maxBytes)
            throw new BadHttpRequestException("Request body is too large.", StatusCodes.Status413PayloadTooLarge);

        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > maxBytes)
                throw new BadHttpRequestException("Request body is too large.", StatusCodes.Status413PayloadTooLarge);
            buffer.Write(chunk, 0, read);
        }
        buffer.Position = 0;
        return await JsonSerializer.DeserializeAsync<T>(buffer, options, cancellationToken);
    }
}
