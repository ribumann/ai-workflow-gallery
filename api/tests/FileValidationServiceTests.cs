using WorkflowGallery.Api.Services;
using Xunit;

namespace WorkflowGallery.Api.Tests;

/// <summary>
/// Covers <see cref="FileValidationService"/>, including the path-traversal guard added after the
/// T057 security pass found that an unsanitized client-supplied file name could be used to build a
/// blob path that escapes the uploading task's prefix and overwrite another task's file.
/// </summary>
public class FileValidationServiceTests
{
    private readonly FileValidationService _service = new();

    [Theory]
    [InlineData("../other-task/file.md")]
    [InlineData("..\\other-task\\file.md")]
    [InlineData("sub/dir/file.md")]
    [InlineData("sub\\dir\\file.md")]
    [InlineData("..")]
    public void Validate_RejectsFileNamesWithPathSegmentsOrTraversal(string fileName)
    {
        var result = _service.Validate(fileName, sizeBytes: 100);

        Assert.False(result.IsValid);
        Assert.Contains("path", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("notes.md")]
    [InlineData("release-notes.v2.txt")]
    [InlineData("config.yaml")]
    [InlineData("bundle.zip")]
    [InlineData("Bundle.ZIP")]
    public void Validate_AcceptsPlainFileNames(string fileName)
    {
        var result = _service.Validate(fileName, sizeBytes: 100);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_RejectsDisallowedExtension()
    {
        var result = _service.Validate("script.exe", sizeBytes: 100);

        Assert.False(result.IsValid);
        Assert.Contains("not allowed", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetContentType_ReturnsZipContentTypeForZipFiles()
    {
        Assert.Equal("application/zip", _service.GetContentType("bundle.zip"));
    }
}
