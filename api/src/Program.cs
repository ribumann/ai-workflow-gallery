using Azure.AI.OpenAI;
using System.ClientModel.Primitives;
using Azure.Storage.Blobs;
using Azure.Identity;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WorkflowGallery.Api.Data;
using WorkflowGallery.Api.Middleware;
using WorkflowGallery.Api.Services;
using WorkflowGallery.Api.Services.Mcp;
using WorkflowGallery.Api.Services.Auth;

if (args.Contains("--print-catalog-schema", StringComparer.Ordinal))
{
    using var schemaContext = new AppDbContextFactory().CreateDbContext(Array.Empty<string>());
    Console.WriteLine(CatalogSchema.GenerateCreateScript(schemaContext));
    return;
}

if (args.Contains("--initialize-catalog", StringComparer.Ordinal))
{
    var connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")
        ?? throw new InvalidOperationException("Set SqlConnectionString to a new empty local database before initialization.");
    var connection = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString);
    var server = connection.DataSource.Split(',')[0].Split('\\')[0];
    if (server is not ("." or "(local)" or "(localdb)" or "localhost" or "127.0.0.1" or "::1"))
        throw new InvalidOperationException("Catalog initialization is a local-only operation; remote SQL targets are refused.");
    _ = CatalogRegistry.Default;
    await using var schemaContext = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options);
    await CatalogSchema.InitializeAsync(schemaContext);
    return;
}

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication(worker =>
    {
        // Centralized exception-handling/logging middleware (T017) — logs every unhandled
        // exception once, with the invocation ID, before the host's default error response.
        worker.UseMiddleware<ExceptionHandlingMiddleware>();

        // Both proxied and direct API calls validate bearer identity before any handler executes.
        worker.UseMiddleware<TenantAuthorizationMiddleware>();
    })
    .ConfigureServices((context, services) =>
    {
        var publicDemoPolicy = PublicDemoPolicy.FromConfiguration(context.Configuration);
        services.AddSingleton(publicDemoPolicy);
        services.AddSingleton(CollectionAuthorizationPolicy.Read(context.Configuration));
        services.AddSingleton<PlaygroundWriteGuard>(PlaygroundWriteGuard.Worker);
        var publicComposerSettings = PublicComposerSettings.Read(context.Configuration);
        services.AddSingleton(publicComposerSettings);
        services.AddScoped<IPublicComposerBudget, PublicComposerBudget>();
        services.AddScoped<PublicComposer>();
        // Externalized configuration (T018): connection strings/container names/feature flags
        // come from app settings (local.settings.json locally, Static Web App app settings in
        // Azure — see infra/modules/static-web-app.bicep), never hardcoded in source.
        var sqlConnectionString = context.Configuration["SqlConnectionString"]
            ?? throw new InvalidOperationException("SqlConnectionString app setting is not configured.");

        services.AddDbContext<AppDbContext>(options => options.UseSqlServer(sqlConnectionString));
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddSingleton<IEntraMetadata, EntraMetadata>();
        services.AddSingleton<IEntraTokenValidator, EntraTokenValidator>();
        services.AddSingleton<GalleryRequestAuthorization>();

        // Application Insights (infra: app-insights.bicep), emitted via the OpenTelemetry pipeline
        // configured in host.json (telemetryMode: "OpenTelemetry"). Reads
        // APPLICATIONINSIGHTS_CONNECTION_STRING from app settings; a no-op locally when unset.
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

        // Reports retain their existing string enum wire contract; catalog IDs are plain strings.
        services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(options =>
        {
            options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        });

        // Blob storage for the single optional task file upload (T029) — container name is
        // externalized configuration (T018), same as SqlConnectionString above.
        //
        // Two auth modes, mirroring AzureWebJobsStorage's own identity-based setup below:
        //   - Azure (UAT/prod): `StorageBlobEndpoint` (a https://<account>.blob.core.windows.net/
        //     URL, see infra/modules/function-app.bicep) + the Function App's managed identity via
        //     DefaultAzureCredential. Required because this tenant's storage accounts have
        //     shared-key auth disabled by policy — a connection string with an account key throws
        //     `KeyBasedAuthenticationNotPermitted` on every request (found + fixed while triaging
        //     issue #67: every file upload was failing with a 500).
        //   - Local dev: `StorageConnectionString` (Azurite's `UseDevelopmentStorage=true`, see
        //     local.settings.json.template), which has no managed identity to authenticate with.
        var storageBlobEndpoint = context.Configuration["StorageBlobEndpoint"];
        var storageConnectionString = context.Configuration["StorageConnectionString"];
        var storageContainerName = context.Configuration["StorageContainerName"];
        var playgroundStorageContainerName = context.Configuration["PlaygroundStorageContainerName"];
        var playgroundStorageClientId = context.Configuration["PlaygroundStorageClientId"];
        var playgroundStorageBlobEndpoint = context.Configuration["PlaygroundStorageBlobEndpoint"];
        var curatedStorageBlobEndpoint = context.Configuration["CuratedStorageBlobEndpoint"];
        var curatedStorageClientId = context.Configuration["CuratedStorageClientId"];
        if (!string.IsNullOrWhiteSpace(curatedStorageBlobEndpoint) &&
            (string.IsNullOrWhiteSpace(curatedStorageClientId) ||
             string.Equals(curatedStorageBlobEndpoint.TrimEnd('/'), storageBlobEndpoint?.TrimEnd('/'), StringComparison.OrdinalIgnoreCase) ||
             string.Equals(curatedStorageBlobEndpoint.TrimEnd('/'), playgroundStorageBlobEndpoint?.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Curated files require a separate storage account and explicit identity.");
        if (publicDemoPolicy.PlaygroundWritesEnabled && string.IsNullOrWhiteSpace(playgroundStorageContainerName))
            throw new InvalidOperationException("PlaygroundStorageContainerName is required when PublicPlaygroundWritesEnabled is true.");
        if (publicDemoPolicy.PlaygroundWritesEnabled && string.IsNullOrWhiteSpace(playgroundStorageClientId))
            throw new InvalidOperationException("PlaygroundStorageClientId is required for public Playground writes.");
        if (publicDemoPolicy.PlaygroundWritesEnabled &&
            (string.IsNullOrWhiteSpace(playgroundStorageBlobEndpoint) ||
             string.Equals(playgroundStorageBlobEndpoint.TrimEnd('/'), storageBlobEndpoint?.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Public Playground writes require a separate PlaygroundStorageBlobEndpoint.");
        if (publicDemoPolicy.Enabled && string.IsNullOrWhiteSpace(storageBlobEndpoint) && string.IsNullOrWhiteSpace(storageConnectionString))
        {
            services.AddSingleton<IBlobStorageService, DisabledBlobStorageService>();
        }
        else
        {
            if (string.IsNullOrWhiteSpace(storageContainerName))
                throw new InvalidOperationException("StorageContainerName app setting is not configured.");
            var blobServiceClient = !string.IsNullOrWhiteSpace(storageBlobEndpoint)
                ? new BlobServiceClient(new Uri(storageBlobEndpoint), GalleryCredential.Create(context.Configuration))
                : !string.IsNullOrWhiteSpace(storageConnectionString)
                    ? new BlobServiceClient(storageConnectionString)
                    : throw new InvalidOperationException("Either StorageBlobEndpoint or StorageConnectionString app setting must be configured.");
            var playgroundBlobServiceClient = !string.IsNullOrWhiteSpace(playgroundStorageClientId) &&
                !string.IsNullOrWhiteSpace(playgroundStorageBlobEndpoint)
                ? new BlobServiceClient(new Uri(playgroundStorageBlobEndpoint), new ManagedIdentityCredential(playgroundStorageClientId))
                : null;
            services.AddSingleton(blobServiceClient);
            services.AddSingleton<IBlobStorageService>(sp =>
                new BlobStorageService(sp.GetRequiredService<BlobServiceClient>(), storageContainerName,
                    playgroundStorageContainerName, playgroundBlobServiceClient,
                    string.IsNullOrWhiteSpace(curatedStorageBlobEndpoint) ? null :
                        new BlobServiceClient(new Uri(curatedStorageBlobEndpoint), new ManagedIdentityCredential(curatedStorageClientId)),
                    publicDemoPolicy.Enabled));
        }
        services.AddSingleton<IFileValidationService, FileValidationService>();

        // Feedback notification integration: Azure Communication Services Email, authenticated with
        // the Function App managed identity so feedback no longer depends on an expiring GitHub PAT.
        services.AddSingleton<IFeedbackNotificationClient, AzureCommunicationFeedbackEmailClient>();

        // Composer remains opt-in. Missing inference configuration must not break manual
        // authoring or application startup; the handler distinguishes disabled from failed calls.
        var aiFoundryEndpoint = context.Configuration["AiFoundryEndpoint"];
        var aiFoundryDeploymentName = context.Configuration["AiFoundryDeploymentName"];

        if ((!publicDemoPolicy.Enabled || publicComposerSettings.Ready) &&
            !string.IsNullOrWhiteSpace(aiFoundryEndpoint) && !string.IsNullOrWhiteSpace(aiFoundryDeploymentName))
        {
            // Data-plane inference only (Cognitive Services OpenAI User role, see
            // infra/main.bicep's conditional aiFoundryRoleAssignment) via the Function App's
            // managed identity — disableLocalAuth: true on the AI Foundry account means there is
            // no key-based fallback, same posture as the storage/SQL managed-identity auth above.
            services.AddSingleton(new AzureOpenAIClient(new Uri(aiFoundryEndpoint), GalleryCredential.Create(context.Configuration),
                new AzureOpenAIClientOptions(AzureOpenAIClientOptions.ServiceVersion.V2024_10_21) { RetryPolicy = new ClientRetryPolicy(0),
                    NetworkTimeout = TimeSpan.FromSeconds(publicDemoPolicy.Enabled ? publicComposerSettings.ModelTimeoutSeconds : 45) }));
            services.AddScoped<IAiComposerService>(sp => new AzureAiComposerService(
                sp.GetRequiredService<AzureOpenAIClient>(),
                aiFoundryDeploymentName,
                sp.GetRequiredService<ILogger<AzureAiComposerService>>(),
                publicDemoPolicy.Enabled ? publicComposerSettings : null));
        }
        else
        {
            services.AddScoped<IAiComposerService, UnconfiguredAiComposerService>();
        }

        services.AddScoped<IAiComposerRateLimiter, AiComposerRateLimiter>();

        // MCP server (feature 007, issue #133): exposes the gallery's read operations to agents
        // over the Model Context Protocol at `/api/mcp`.
        //
        // Off unless `EnableMcpServer` is set. Private writes also require `EnableMcpWrites`
        // (FR-007); public Playground writes use their collection capability and data-plane policy.
        //
        // The Function classes below are registered so the MCP tools can *delegate* to the exact
        // same handlers the browser calls, rather than re-querying the database. That is what makes
        // "an agent's results match the browser's results" (SC-002) structurally true instead of
        // something to keep re-testing. Registering them here is additive: the Functions host still
        // activates its own instances for HTTP-triggered invocations.
        var mcpOptions = McpOptions.FromConfiguration(context.Configuration);
        services.AddSingleton(mcpOptions);
        services.AddSingleton(new McpToolCatalog(mcpOptions));
        services.AddSingleton(PublicMcpGuard.Worker);
        services.AddSingleton(new GalleryLinkBuilder(context.Configuration["GalleryBaseUrl"]));
        services.AddSingleton<IMcpAuditLogger, McpAuditLogger>();
        services.AddScoped<WorkflowGallery.Api.Functions.Tasks>();
        services.AddScoped<WorkflowGallery.Api.Functions.Workflows>();
        services.AddScoped<WorkflowGallery.Api.Functions.Methods>();
        services.AddScoped<WorkflowGallery.Api.Functions.Reports>();
        services.AddScoped<IGalleryMcpTools, GalleryMcpTools>();
    })
    .Build();

// Normal startup never changes a database schema.
using (var scope = host.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var composerSettings = scope.ServiceProvider.GetRequiredService<PublicComposerSettings>();
    if (scope.ServiceProvider.GetRequiredService<PublicDemoPolicy>().Enabled && composerSettings.OptedIn && !composerSettings.Ready)
        scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("PublicComposer")
            .LogError("Public Composer opt-in has incomplete or invalid settings. Anonymous inference is unavailable.");
    _ = CatalogRegistry.Default;
    await CatalogSchema.VerifyAsync(dbContext);
    if (scope.ServiceProvider.GetRequiredService<PublicDemoPolicy>().PlaygroundWritesEnabled)
    {
        var actor = await dbContext.Users.AsNoTracking().SingleOrDefaultAsync(
            user => user.Id == CurrentUserService.SandboxActorId && user.IsSandboxActor);
        if (actor is null || actor.EntraTenantId is not null || actor.EntraObjectId is not null)
            throw new InvalidOperationException("Playground writes require the provisioned non-Entra sandbox actor.");
    }
}

await host.RunAsync();
