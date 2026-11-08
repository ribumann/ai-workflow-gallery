using Azure;
using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using WorkflowGallery.Api.Services;
using Xunit;

namespace WorkflowGallery.Api.Tests;

public sealed class PlaygroundUploadBudgetTests
{
    [Fact]
    public async Task ReservationsIncludeFailedAndDeletedUploadsWithoutRefund()
    {
        var counter = new CounterBlob();
        await PlaygroundUploadBudget.ReserveAsync(counter, PlaygroundWriteGuard.MaxStoredAttachmentBytes - 1, default);
        await PlaygroundUploadBudget.ReserveAsync(counter, 1, default);
        await Assert.ThrowsAsync<PlaygroundStorageLimitException>(() =>
            PlaygroundUploadBudget.ReserveAsync(counter, 1, default));
        Assert.Equal(PlaygroundWriteGuard.MaxStoredAttachmentBytes.ToString(), counter.Value);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("invalid")]
    [InlineData("536870913")]
    public async Task CorruptCountersFailClosed(string value)
    {
        var counter = new CounterBlob { Value = value };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PlaygroundUploadBudget.ReserveAsync(counter, 1, default));
        Assert.Equal(value, counter.Value);
    }

    [Fact]
    public async Task ContentionRereadsAndUncertainCommitRemainsCharged()
    {
        var counter = new CounterBlob { ConflictOnce = true };
        await PlaygroundUploadBudget.ReserveAsync(counter, 7, default);
        Assert.Equal("12", counter.Value);
        counter.ThrowAfterCommit = true;
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            PlaygroundUploadBudget.ReserveAsync(counter, 8, default));
        Assert.Equal("20", counter.Value);
    }

    [Fact]
    public async Task ConcurrentReservationsCannotExceedTheSharedBudget()
    {
        var counter = new CounterBlob();
        const long bytes = 32L * 1024 * 1024;
        var accepted = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(async () =>
        {
            try
            {
                await PlaygroundUploadBudget.ReserveAsync(counter, bytes, default);
                return true;
            }
            catch (PlaygroundStorageLimitException) { return false; }
        })));
        var total = accepted.Count(result => result) * bytes;
        Assert.InRange(total, bytes, PlaygroundWriteGuard.MaxStoredAttachmentBytes);
        Assert.Equal(total.ToString(), counter.Value);
    }

    private sealed class CounterBlob : BlobClient
    {
        public string? Value { get; set; }
        public bool ConflictOnce { get; set; }
        public bool ThrowAfterCommit { get; set; }
        private int _version;
        private readonly object _gate = new();

        public override Task<Response<BlobDownloadResult>> DownloadContentAsync(CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                if (Value is null) throw new RequestFailedException(404, "Missing");
                return Task.FromResult(Response.FromValue(BlobsModelFactory.BlobDownloadResult(
                    content: BinaryData.FromString(Value),
                    details: BlobsModelFactory.BlobDownloadDetails(eTag: new ETag(_version.ToString()))), new EmptyResponse()));
            }
        }

        public override Task<Response<BlobContentInfo>> UploadAsync(
            BinaryData content, BlobUploadOptions options, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                if (ConflictOnce)
                {
                    ConflictOnce = false;
                    Value = "5";
                    _version++;
                    throw new RequestFailedException(412, "Concurrent reservation");
                }
                if (Value is null ? options.Conditions.IfNoneMatch != ETag.All :
                    options.Conditions.IfMatch != new ETag(_version.ToString()))
                    throw new RequestFailedException(412, "Concurrent reservation");
                Value = content.ToString();
                _version++;
                if (ThrowAfterCommit) throw new OperationCanceledException("Uncertain response");
                return Task.FromResult(Response.FromValue(BlobsModelFactory.BlobContentInfo(
                    new ETag(_version.ToString()), DateTimeOffset.UtcNow, [], "", "", "", 0), new EmptyResponse()));
            }
        }
    }

    private sealed class EmptyResponse : Response
    {
        public override int Status => 200;
        public override string ReasonPhrase => "OK";
        public override Stream? ContentStream { get; set; }
        public override string ClientRequestId { get; set; } = "";
        public override void Dispose() { }
        protected override bool ContainsHeader(string name) => false;
        protected override IEnumerable<HttpHeader> EnumerateHeaders() => [];
        protected override bool TryGetHeader(string name, out string value) { value = ""; return false; }
        protected override bool TryGetHeaderValues(string name, out IEnumerable<string> values) { values = []; return false; }
    }
}
