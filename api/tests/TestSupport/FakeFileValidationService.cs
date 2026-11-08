using WorkflowGallery.Api.Services;

namespace WorkflowGallery.Api.Tests.TestSupport;

/// <summary>Fake <see cref="IFileValidationService"/>: always approves uploads by default, so
/// Task write-path tests can focus on the behavior under test rather than file-validation rules
/// (which have their own dedicated coverage where needed).</summary>
public class FakeFileValidationService : IFileValidationService
{
    private readonly FileValidationResult _result;

    public FakeFileValidationService(FileValidationResult? result = null)
    {
        _result = result ?? FileValidationResult.Success();
    }

    public FileValidationResult Validate(string fileName, long sizeBytes) => _result;

    public string GetContentType(string fileName) => "application/octet-stream";
}
