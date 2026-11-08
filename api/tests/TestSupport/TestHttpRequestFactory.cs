using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace WorkflowGallery.Api.Tests.TestSupport;

/// <summary>
/// Builds <see cref="HttpRequest"/> instances for Function unit tests without a running host.
/// </summary>
public static class TestHttpRequestFactory
{
    /// <summary>
    /// A GET-style request with query string parameters, no body.
    /// </summary>
    public static HttpRequest CreateWithQuery(IDictionary<string, StringValues> query)
    {
        query = new Dictionary<string, StringValues>(query);
        if (!query.ContainsKey("collectionId")) query["collectionId"] = "public-techniques";
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.QueryString = QueryString.Create(
            query.SelectMany(kvp => kvp.Value.Select(v => new KeyValuePair<string, string?>(kvp.Key, v))));
        return context.Request;
    }

    /// <summary>
    /// A JSON-body request (POST/PUT), as used by the Workflows and Feedback Functions.
    /// </summary>
    public static HttpRequest CreateWithJsonBody(string json)
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?collectionId=public-techniques");
        var bytes = Encoding.UTF8.GetBytes(json);
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentLength = bytes.Length;
        context.Request.ContentType = "application/json";
        return context.Request;
    }

    /// <summary>
    /// A multipart/form-data request pre-populated with an already-parsed <see cref="IFormCollection"/>,
    /// as consumed by the Tasks Function's Create/Update handlers (`req.ReadFormAsync`). Setting
    /// the <see cref="IFormFeature"/> directly avoids needing to hand-encode real multipart bytes.
    /// </summary>
    public static HttpRequest CreateWithForm(Dictionary<string, StringValues> fields, IFormFileCollection? files = null)
    {
        var context = new DefaultHttpContext
        {
            // FormFeature.HasFormContentType resolves an antiforgery-validation feature via
            // RequestServices; an empty provider is enough to satisfy that lookup in tests.
            RequestServices = new ServiceCollection().BuildServiceProvider(),
        };
        context.Request.ContentType = "multipart/form-data; boundary=----testboundary";
        context.Request.QueryString = new QueryString("?collectionId=public-techniques");

        // Construct FormFeature from the real HttpRequest (not the IFormCollection-only overload,
        // whose internal `_request` field stays null and NREs inside HasFormContentType's
        // antiforgery-validation check), then assign the pre-built form via its public setter so
        // ReadFormAsync returns it without parsing real multipart bytes.
        var formCollection = new FormCollection(fields, files);
        var formFeature = new FormFeature(context.Request, new FormOptions())
        {
            Form = formCollection,
        };
        context.Features.Set<IFormFeature>(formFeature);
        return context.Request;
    }

    public static FormFile CreateFormFile(string fieldName, string fileName, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, fieldName, fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = "text/plain",
        };
    }
}
