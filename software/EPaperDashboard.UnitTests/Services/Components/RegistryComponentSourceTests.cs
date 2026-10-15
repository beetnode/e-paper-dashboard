using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using EPaperDashboard.Services.Components.Playwright;
using FluentAssertions;
using Moq;
using Xunit;

namespace EPaperDashboard.UnitTests.Services.Components;

public sealed class RegistryComponentSourceTests : IDisposable
{
    private const string Registry = "https://registry.example.test/v2/izboard/rendering";
    private const string LayerType = "application/vnd.oci.image.layer.v1.tar+gzip";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"izboard-registry-tests-{Guid.NewGuid():N}");
    private readonly Handler _handler = new();
    private readonly byte[] _amd64Layer = Encoding.UTF8.GetBytes("amd64 component layer");
    private readonly byte[] _arm64Layer = Encoding.UTF8.GetBytes("arm64 component layer");

    public RegistryComponentSourceTests() => Directory.CreateDirectory(_directory);

    [Theory]
    [InlineData("linux-x64", "amd64 component layer")]
    [InlineData("linux-arm64", "arm64 component layer")]
    public async Task DownloadAsync_FromIndex_WritesLayerForRuntime(string runtime, string expected)
    {
        var index = PublishMultiArchImage();

        await Download($"registry.example.test/izboard/rendering@{Digest(index)}", runtime);

        File.ReadAllText(ArchivePath).Should().Be(expected);
    }

    [Fact]
    public async Task DownloadAsync_WhenRegistryChallenges_UsesAnonymousBearerToken()
    {
        PublishMultiArchImage();
        _handler.RequireToken = "anonymous-token";
        _handler.Routes["https://auth.example.test/token?service=registry.example.test&scope=repository%3Aizboard%2Frendering%3Apull"] =
            () => Json("""{"token":"anonymous-token"}""");

        await Download("registry.example.test/izboard/rendering:1.0.0", "linux-arm64");

        File.ReadAllText(ArchivePath).Should().Be("arm64 component layer");
        _handler.Requests.Where(request => request.Url.StartsWith(Registry))
            .Skip(1).Should().OnlyContain(request => request.Authorization == "Bearer anonymous-token");
    }

    [Fact]
    public async Task DownloadAsync_WhenIndexDoesNotMatchPinnedDigest_Throws()
    {
        PublishMultiArchImage();
        var other = new string('a', 64);
        _handler.Routes[$"{Registry}/manifests/sha256:{other}"] = _handler.Routes[$"{Registry}/manifests/1.0.0"];

        var action = () => Download($"registry.example.test/izboard/rendering@sha256:{other}", "linux-x64");

        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*manifest failed digest verification*");
    }

    [Fact]
    public async Task DownloadAsync_WhenLayerDoesNotMatchDigest_Throws()
    {
        PublishMultiArchImage();
        var layerUrl = $"{Registry}/blobs/{Digest(_amd64Layer)}";
        _handler.Routes[layerUrl] = () => Bytes(Encoding.UTF8.GetBytes("tampered layer"));

        var action = () => Download("registry.example.test/izboard/rendering:1.0.0", "linux-x64");

        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*failed digest verification*");
    }

    [Fact]
    public async Task DownloadAsync_WhenIndexHasNoVariantForArchitecture_Throws()
    {
        var amd64 = Manifest(Layer(_amd64Layer));
        PublishIndex(Index(("amd64", amd64)));

        var action = () => Download("registry.example.test/izboard/rendering:1.0.0", "linux-arm64");

        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*no linux/arm64 variant*");
    }

    [Theory]
    [InlineData(2, LayerType, 10, "*exactly one layer*")]
    [InlineData(1, "application/vnd.oci.image.layer.v1.tar+zstd", 10, "*unsupported type*")]
    [InlineData(1, LayerType, 2_000_000_000, "*exceeds the allowed size*")]
    public async Task DownloadAsync_WhenLayerIsNotAcceptable_Throws(int layers, string mediaType, long size, string message)
    {
        var layer = $$"""{"mediaType":"{{mediaType}}","digest":"{{Digest(_amd64Layer)}}","size":{{size}}}""";
        var manifest = Manifest(string.Join(',', Enumerable.Repeat(layer, layers)));
        PublishIndex(Index(("amd64", manifest)));

        var action = () => Download("registry.example.test/izboard/rendering:1.0.0", "linux-x64");

        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage(message);
    }

    [Theory]
    [InlineData("izdevdigital/izboard-rendering", "registry-1.docker.io", "izdevdigital/izboard-rendering", "latest", null)]
    [InlineData("izdevdigital/izboard-rendering:0.6.0", "registry-1.docker.io", "izdevdigital/izboard-rendering", "0.6.0", null)]
    [InlineData("docker.io/library/alpine:3", "registry-1.docker.io", "library/alpine", "3", null)]
    [InlineData("alpine", "registry-1.docker.io", "library/alpine", "latest", null)]
    [InlineData("ghcr.io/beetnode/izboard-rendering:latest", "ghcr.io", "beetnode/izboard-rendering", "latest", null)]
    [InlineData("localhost:5000/rendering", "localhost:5000", "rendering", "latest", null)]
    [InlineData(
        "izdevdigital/izboard-rendering@sha256:ABCDEF0123456789abcdef0123456789abcdef0123456789abcdef0123456789",
        "registry-1.docker.io", "izdevdigital/izboard-rendering", "latest",
        "sha256:abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789")]
    public void Parse_ReturnsReferenceParts(string value, string registry, string repository, string tag, string? digest)
    {
        ComponentImageReference.Parse(value).Should().Be(new ComponentImageReference(registry, repository, tag, digest));
    }

    [Theory]
    [InlineData("izdevdigital/Izboard")]
    [InlineData("izdevdigital/izboard@sha256:short")]
    [InlineData("izdevdigital/izboard@md5:0123456789abcdef0123456789abcdef")]
    [InlineData("izdevdigital/izboard:")]
    public void Parse_WhenReferenceIsInvalid_Throws(string value)
    {
        var action = () => ComponentImageReference.Parse(value);

        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ParseChallenge_ReadsQuotedParameters()
    {
        var parameters = RegistryComponentSource.ParseChallenge(
            "realm=\"https://auth.docker.io/token\",service=\"registry.docker.io\",scope=\"repository:a/b:pull\"");

        parameters.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["realm"] = "https://auth.docker.io/token",
            ["service"] = "registry.docker.io",
            ["scope"] = "repository:a/b:pull"
        });
    }

    public void Dispose()
    {
        _handler.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    private string ArchivePath => Path.Combine(_directory, "component.tar.gz");

    private async Task Download(string reference, string runtime)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(value => value.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(_handler, disposeHandler: false));
        var source = new RegistryComponentSource(factory.Object, ComponentImageReference.Parse(reference));
        await source.DownloadAsync(runtime, ArchivePath, (_, _) => { }, CancellationToken.None);
    }

    private byte[] PublishMultiArchImage() =>
        PublishIndex(Index(("amd64", Manifest(Layer(_amd64Layer))), ("arm64", Manifest(Layer(_arm64Layer)))));

    private byte[] PublishIndex(string index)
    {
        var bytes = Encoding.UTF8.GetBytes(index);
        _handler.Routes[$"{Registry}/manifests/1.0.0"] = () => Bytes(bytes);
        _handler.Routes[$"{Registry}/manifests/{Digest(bytes)}"] = () => Bytes(bytes);
        _handler.Routes[$"{Registry}/blobs/{Digest(_amd64Layer)}"] = () => Bytes(_amd64Layer);
        _handler.Routes[$"{Registry}/blobs/{Digest(_arm64Layer)}"] = () => Bytes(_arm64Layer);
        return bytes;
    }

    private string Index(params (string Architecture, string Manifest)[] manifests)
    {
        var entries = manifests.Select(entry =>
        {
            var bytes = Encoding.UTF8.GetBytes(entry.Manifest);
            _handler.Routes[$"{Registry}/manifests/{Digest(bytes)}"] = () => Bytes(bytes);
            return $$$"""{"mediaType":"application/vnd.oci.image.manifest.v1+json","digest":"{{{Digest(bytes)}}}","size":{{{bytes.Length}}},"platform":{"architecture":"{{{entry.Architecture}}}","os":"linux"}}""";
        })
        // Attestation manifests published by buildx carry an unknown platform and must be ignored.
        .Append($$$"""{"mediaType":"application/vnd.oci.image.manifest.v1+json","digest":"{{{Digest([1])}}}","size":1,"platform":{"architecture":"unknown","os":"unknown"}}""");
        return $$"""{"schemaVersion":2,"mediaType":"application/vnd.oci.image.index.v1+json","manifests":[{{string.Join(',', entries)}}]}""";
    }

    private static string Manifest(string layers) =>
        $$"""{"schemaVersion":2,"mediaType":"application/vnd.oci.image.manifest.v1+json","config":{"mediaType":"application/vnd.oci.image.config.v1+json","digest":"{{Digest([2])}}","size":2},"layers":[{{layers}}]}""";

    private static string Layer(byte[] content) =>
        $$"""{"mediaType":"{{LayerType}}","digest":"{{Digest(content)}}","size":{{content.Length}}}""";

    private static string Digest(byte[] content) => $"sha256:{Convert.ToHexStringLower(SHA256.HashData(content))}";

    private static HttpResponseMessage Bytes(byte[] content) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(content) };

    private static HttpResponseMessage Json(string content) =>
        new(HttpStatusCode.OK) { Content = new StringContent(content, Encoding.UTF8, "application/json") };

    private sealed class Handler : HttpMessageHandler
    {
        public string? RequireToken { get; set; }
        public Dictionary<string, Func<HttpResponseMessage>> Routes { get; } = [];
        public List<(string Url, string? Authorization)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.AbsoluteUri;
            Requests.Add((url, request.Headers.Authorization?.ToString()));
            if (RequireToken is not null && url.StartsWith(Registry)
                && request.Headers.Authorization?.Parameter != RequireToken)
            {
                var challenge = new HttpResponseMessage(HttpStatusCode.Unauthorized);
                challenge.Headers.WwwAuthenticate.Add(new AuthenticationHeaderValue("Bearer",
                    "realm=\"https://auth.example.test/token\",service=\"registry.example.test\",scope=\"repository:izboard/rendering:pull\""));
                return Task.FromResult(challenge);
            }

            return Task.FromResult(Routes.TryGetValue(url, out var route) ? route() : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
