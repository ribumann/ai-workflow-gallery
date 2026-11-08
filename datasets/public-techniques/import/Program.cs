using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WorkflowGallery.Api.Data;
using WorkflowGallery.Api.Services.Catalog;

const string Usage = """
Usage: dotnet run --project datasets\public-techniques\import -- \
  --catalog <catalog.json> --collection public-techniques \
  --connection-env <environment-variable-name> --target-database <exact-database-name> \
  --actor-id <existing-maintainer-user-guid> (--dry-run | --apply) [--publish-reviewed]

Only explicitly selected isolated fresh catalog databases are supported.
This tool never creates a schema, user, role, connection or scheduled process.
The connection string is read from the named environment variable and never printed.
Dry run uses the actual application importer/validation; apply is transactional.
Content stays unpublished unless --publish-reviewed is explicitly supplied.
""";

if (args is ["--help"])
{
    Console.WriteLine(Usage);
    return 0;
}

try
{
    var allowed = new HashSet<string>(StringComparer.Ordinal)
    {
        "--catalog", "--collection", "--connection-env", "--target-database", "--actor-id",
        "--dry-run", "--apply", "--publish-reviewed",
    };
    var options = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var index = 0; index < args.Length; index++)
    {
        var key = args[index];
        if (!allowed.Contains(key)) throw new ArgumentException($"Unknown argument: {key}");
        if (key is "--dry-run" or "--apply" or "--publish-reviewed")
        {
            if (!options.TryAdd(key, "true")) throw new ArgumentException($"Duplicate argument: {key}");
            continue;
        }
        if (++index >= args.Length || args[index].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException($"Missing value for {key}.");
        if (!options.TryAdd(key, args[index])) throw new ArgumentException($"Duplicate argument: {key}");
    }

    foreach (var required in new[] { "--catalog", "--collection", "--connection-env", "--target-database", "--actor-id" })
    {
        if (!options.TryGetValue(required, out var value) || string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"Required argument: {required}");
    }
    if (options.ContainsKey("--dry-run") == options.ContainsKey("--apply"))
        throw new ArgumentException("Choose exactly one of --dry-run and --apply.");
    if (options["--collection"] != "public-techniques")
        throw new ArgumentException("This tool imports only the public-techniques collection.");
    if (!Guid.TryParse(options["--actor-id"], out var actorId) || actorId == Guid.Empty)
        throw new ArgumentException("--actor-id must identify an existing maintainer user.");
    var environmentName = options["--connection-env"];
    if (environmentName.Any(character => !(char.IsAsciiLetterOrDigit(character) || character == '_')))
        throw new ArgumentException("--connection-env must name an environment variable, not contain a connection string.");
    var connectionString = Environment.GetEnvironmentVariable(environmentName);
    if (string.IsNullOrWhiteSpace(connectionString))
        throw new ArgumentException("The selected connection environment variable is empty.");

    var catalogFile = new FileInfo(options["--catalog"]);
    if (!catalogFile.Exists || catalogFile.Length is <= 0 or > 5 * 1024 * 1024)
        throw new ArgumentException("The catalog must be an existing nonempty JSON file no larger than 5 MiB.");
    await using var catalog = catalogFile.OpenRead();
    using (var document = await JsonDocument.ParseAsync(catalog))
    {
        if (!document.RootElement.TryGetProperty("collectionId", out var collection)
            || collection.GetString() != options["--collection"]
            || !document.RootElement.TryGetProperty("datasetId", out var dataset)
            || dataset.GetString() != "public-techniques-v1")
            throw new ArgumentException("Catalog dataset and collection must match the explicit public-techniques import boundary.");
    }
    catalog.Position = 0;

    var dbOptions = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options;
    await using var db = new AppDbContext(dbOptions);
    if (!string.Equals(db.Database.GetDbConnection().Database, options["--target-database"], StringComparison.Ordinal))
        throw new ArgumentException("The selected connection does not identify the exact --target-database.");
    var actor = await db.Users.SingleOrDefaultAsync(user => user.Id == actorId);
    if (actor is null) throw new ArgumentException("The selected actor does not exist. This tool never creates users.");
    var dryRun = options.ContainsKey("--dry-run");
    var publishReviewed = options.ContainsKey("--publish-reviewed");
    var result = await CatalogImportService.ImportAsync(db, catalog, actor, dryRun, publishReviewed);
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        datasetId = "public-techniques-v1", collectionId = options["--collection"],
        dryRun, publishReviewed, result.AlreadyImported, result.Tasks, result.Workflows, result.Methods,
    }));
    return 0;
}
catch (Exception error) when (error is ArgumentException or JsonException or InvalidOperationException)
{
    Console.Error.WriteLine($"Import rejected: {error.Message}");
    Console.Error.WriteLine("Use --help for the explicit isolated-target invocation.");
    return 1;
}
catch (Exception error)
{
    // Provider exceptions can contain connection details. Keep diagnostics local and nonsecret.
    Console.Error.WriteLine($"Import failed ({error.GetType().Name}); no success is claimed. Check local database diagnostics before retrying.");
    return 1;
}
