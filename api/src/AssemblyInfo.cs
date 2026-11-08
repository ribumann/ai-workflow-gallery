using System.Runtime.CompilerServices;

// Exposes `internal` members (e.g. GetRoles.DetermineRoles) to the xUnit test project (T052) without
// making them public API.
[assembly: InternalsVisibleTo("WorkflowGallery.Api.Tests")]
