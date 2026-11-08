using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using WorkflowGallery.Api.Services.Auth;

namespace WorkflowGallery.Api.Services.Mcp;

/// <summary>
/// Builds the synthetic <see cref="HttpRequest"/> an MCP tool hands to the gallery's existing
/// Function classes.
///
/// <para>
/// <b>Why go through a request at all.</b> The tools deliberately call <c>Tasks.List</c>,
/// <c>Workflows.GetById</c> and friends rather than re-querying the database, so that ordering,
/// filtering, paging and visibility cannot drift from what the browser shows (FR-005, SC-002).
/// Those methods take an <see cref="HttpRequest"/>, so the adapter's job is to produce one that is
/// indistinguishable from the request the browser would have sent.
/// </para>
/// <para>
/// <b>The validated principal is forwarded, never synthesised.</b> The server-authenticated
/// ClaimsPrincipal is carried across; client-controlled platform headers are discarded. This makes
/// ownership-sensitive projections (for example an AI method's <c>canDelete</c>) compute against
/// the real person, and what guarantees the server has no privileged path around the gallery's
/// authorization rules (FR-003, FR-004, SC-003).
/// </para>
/// </summary>
public static class GalleryRequestFactory
{
    /// <summary>
    /// A content type that satisfies <see cref="HttpRequest.HasFormContentType"/>. The gallery's
    /// create/update endpoints accept <c>multipart/form-data</c> because the browser may attach a
    /// file; an MCP tool never does, but it still has to present itself as a form request for those
    /// endpoints to read their fields at all.
    /// </summary>
    private const string FormContentType = "multipart/form-data; boundary=----McpSyntheticBoundary";

    public static HttpRequest ForCollection(HttpRequest originating, string collectionId)
    {
        if (!CollectionScope.IsValid(collectionId))
            throw new McpArgumentException("A valid explicit collectionId is required.");
        var request = NewRequest(originating, requireCollection: false);
        request.Method = originating.Method;
        request.Path = originating.Path;
        request.QueryString = QueryString.Create("collectionId", collectionId);
        _ = CollectionRequestContext.TryResolve(request, PublicDemoPolicy.For(originating), out _);
        foreach (var header in new[] { "x-ms-original-url", "x-forwarded-host", "x-forwarded-proto" })
            if (originating.Headers.TryGetValue(header, out var value)) request.Headers[header] = value;
        return request;
    }

    public static HttpRequest Create(HttpRequest originating, IDictionary<string, string?>? query = null)
    {
        var request = NewRequest(originating);
        request.Method = HttpMethods.Get;
        query = new Dictionary<string, string?>(query ?? new Dictionary<string, string?>())
        {
            ["collectionId"] = originating.Query["collectionId"].ToString(),
        };

        if (query is { Count: > 0 })
        {
            request.QueryString = QueryString.Create(
                query.Where(kvp => !string.IsNullOrEmpty(kvp.Value))
                     .Select(kvp => new KeyValuePair<string, StringValues>(kvp.Key, kvp.Value)));
        }

        return request;
    }

    /// <summary>
    /// Builds a write request carrying <paramref name="fields"/> as form values.
    ///
    /// <para>
    /// The form is attached directly rather than encoded into a multipart body and parsed back out.
    /// <c>ReadFormAsync</c> returns an already-populated form feature as-is, so the endpoint sees
    /// exactly the fields it would have parsed, without this adapter having to reimplement
    /// multipart encoding — a format whose edge cases (quoting, encoding, boundaries) would be a
    /// silent correctness risk for values a user typed.
    /// </para>
    /// <para>
    /// Null-valued fields are omitted rather than sent as empty. The endpoints distinguish "field
    /// absent" from "field present but blank" when deciding whether an update clears a value, so
    /// collapsing the two here would make it impossible to leave a field untouched.
    /// </para>
    /// </summary>
    public static HttpRequest CreateForm(
        HttpRequest originating,
        string method,
        IDictionary<string, string?> fields)
    {
        var request = NewRequest(originating);
        request.Method = method;
        request.ContentType = FormContentType;

        var values = fields
            .Where(kvp => kvp.Value is not null)
            .ToDictionary(kvp => kvp.Key, kvp => new StringValues(kvp.Value));

        request.Form = new FormCollection(values);
        return request;
    }

    /// <summary>
    /// Builds a write request carrying <paramref name="body"/> as a JSON body.
    ///
    /// <para>
    /// Workflows and methods take JSON rather than the form the task endpoints take, and they
    /// deserialize <c>req.Body</c> directly, so this writes a real stream and rewinds it. The
    /// endpoints answer "the request body is not valid JSON" for anything malformed, which is
    /// reached through the caller's own arguments rather than through this adapter.
    /// </para>
    /// </summary>
    public static HttpRequest CreateJson(HttpRequest originating, string method, JsonObject body)
    {
        var request = NewRequest(originating);
        request.Method = method;
        request.ContentType = "application/json";

        var bytes = Encoding.UTF8.GetBytes(body.ToJsonString());
        request.Body = new MemoryStream(bytes);
        request.ContentLength = bytes.Length;

        return request;
    }

    /// <summary>Builds a bodyless write request, for verbs like DELETE.</summary>
    public static HttpRequest CreateBodyless(
        HttpRequest originating,
        string method,
        IDictionary<string, string?>? query = null)
    {
        var request = NewRequest(originating);
        request.Method = method;
        query = new Dictionary<string, string?>(query ?? new Dictionary<string, string?>())
        {
            ["collectionId"] = originating.Query["collectionId"].ToString(),
        };

        if (query is { Count: > 0 })
        {
            request.QueryString = QueryString.Create(
                query.Where(kvp => !string.IsNullOrEmpty(kvp.Value))
                     .Select(kvp => new KeyValuePair<string, StringValues>(kvp.Key, kvp.Value)));
        }

        return request;
    }

    private static HttpRequest NewRequest(HttpRequest originating, bool requireCollection = true)
    {
        if (requireCollection && !CollectionScope.IsValid(originating.Query["collectionId"].ToString()))
            throw new McpArgumentException("A valid explicit collectionId is required.");
        var context = new DefaultHttpContext
        {
            User = originating.HttpContext.User,
            // Some ASP.NET Core request features resolve services from the request scope; reuse the
            // originating request's provider when there is one so behaviour matches a real request.
            RequestServices = originating.HttpContext?.RequestServices
                ?? new ServiceCollection().BuildServiceProvider(),
        };

        var request = context.Request;
        PublicDemoPolicy.For(originating).Stamp(context);
        CollectionAuthorizationPolicy.For(originating).Stamp(context);
        request.QueryString = QueryString.Create("collectionId", originating.Query["collectionId"].ToString());
        request.Scheme = originating.Scheme;

        if (originating.Host.HasValue)
        {
            request.Host = originating.Host;
        }

        return request;
    }
}
