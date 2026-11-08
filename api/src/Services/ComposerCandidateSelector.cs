namespace WorkflowGallery.Api.Services.Catalog;

public static class ComposerCandidateSelector
{
    public static IReadOnlyList<CatalogTaskSnapshot> Select(string goal, IReadOnlyList<CatalogTaskSnapshot> catalog) =>
        CatalogCandidateSelector.Select(goal, catalog);
}
