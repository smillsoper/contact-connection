using System.Text;
using ContactConnection.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Storage;

public class LocalFileBlobStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cc-blob-tests-" + Guid.NewGuid().ToString("N"));
    private readonly LocalFileBlobStorage _store;

    public LocalFileBlobStorageTests()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:LocalRoot"] = _root })
            .Build();
        _store = new LocalFileBlobStorage(config, NullLogger<LocalFileBlobStorage>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static MemoryStream Bytes(string s) => new(Encoding.UTF8.GetBytes(s));

    [Fact]
    public async Task Put_Then_Exists_And_OpenRead_RoundTrips()
    {
        await _store.PutAsync("screen/call-1/rec-1/000000.webm", Bytes("hello chunk"), "video/webm");

        Assert.True(await _store.ExistsAsync("screen/call-1/rec-1/000000.webm"));

        await using var read = await _store.OpenReadAsync("screen/call-1/rec-1/000000.webm");
        Assert.NotNull(read);
        using var sr = new StreamReader(read!);
        Assert.Equal("hello chunk", await sr.ReadToEndAsync());
    }

    [Fact]
    public async Task Put_Overwrites_ExistingKey()
    {
        await _store.PutAsync("k/a.bin", Bytes("v1"), "application/octet-stream");
        await _store.PutAsync("k/a.bin", Bytes("v2-longer"), "application/octet-stream");

        await using var read = await _store.OpenReadAsync("k/a.bin");
        using var sr = new StreamReader(read!);
        Assert.Equal("v2-longer", await sr.ReadToEndAsync());
    }

    [Fact]
    public async Task OpenRead_MissingKey_ReturnsNull()
        => Assert.Null(await _store.OpenReadAsync("nope/missing.webm"));

    [Fact]
    public async Task Delete_RemovesBlob()
    {
        await _store.PutAsync("k/x.bin", Bytes("x"), "application/octet-stream");
        await _store.DeleteAsync("k/x.bin");
        Assert.False(await _store.ExistsAsync("k/x.bin"));
    }

    [Fact]
    public async Task DeletePrefix_RemovesWholeSubtree()
    {
        await _store.PutAsync("screen/call-9/rec-9/000000.webm", Bytes("a"), "video/webm");
        await _store.PutAsync("screen/call-9/rec-9/000001.webm", Bytes("b"), "video/webm");
        await _store.PutAsync("screen/call-9/other.txt", Bytes("c"), "text/plain");

        await _store.DeletePrefixAsync("screen/call-9/rec-9");

        Assert.False(await _store.ExistsAsync("screen/call-9/rec-9/000000.webm"));
        Assert.False(await _store.ExistsAsync("screen/call-9/rec-9/000001.webm"));
        Assert.True(await _store.ExistsAsync("screen/call-9/other.txt"));
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("screen/../../etc/passwd")]
    [InlineData("screen/./x/../../../y")]
    [InlineData("")]
    public async Task InvalidKeys_AreRejected(string key)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _store.PutAsync(key, Bytes("x"), "text/plain"));
    }
}
