using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using EPaperDashboard.Utilities;

namespace EPaperDashboard.Services.Components.Playwright;

/// <summary>
/// Downloads the rendering component from a multi-architecture OCI image through the registry HTTP API.
/// The image holds the component as its single layer; every manifest and the layer are verified by digest.
/// </summary>
internal sealed class RegistryComponentSource(IHttpClientFactory httpClientFactory, ComponentImageReference image)
    : IRenderingComponentSource
{
    private const int MaximumManifestBytes = 4_000_000;
    private const string OciIndex = "application/vnd.oci.image.index.v1+json";
    private const string DockerManifestList = "application/vnd.docker.distribution.manifest.list.v2+json";
    private const string OciManifest = "application/vnd.oci.image.manifest.v1+json";
    private const string DockerManifest = "application/vnd.docker.distribution.manifest.v2+json";
    private static readonly string[] LayerMediaTypes =
    [
        "application/vnd.oci.image.layer.v1.tar+gzip",
        "application/vnd.docker.image.rootfs.diff.tar.gzip"
    ];

    private string? _token;

    public string Description => image.ToString();

    public async Task DownloadAsync(string runtimeIdentifier, string archivePath, Action<long, long?> progress, CancellationToken token)
    {
        var architecture = runtimeIdentifier switch
        {
            "linux-x64" => "amd64",
            "linux-arm64" => "arm64",
            _ => throw new PlatformNotSupportedException($"No component image exists for {runtimeIdentifier}.")
        };

        var (mediaType, manifest) = await GetManifestAsync(image.Digest ?? image.Tag, image.Digest, token);
        using (manifest)
        {
            if (mediaType is OciIndex or DockerManifestList)
            {
                var digest = SelectPlatformManifest(manifest.RootElement, architecture);
                var (platformMediaType, platformManifest) = await GetManifestAsync(digest, digest, token);
                using (platformManifest)
                {
                    if (platformMediaType is not (OciManifest or DockerManifest))
                        throw new InvalidOperationException($"The component image for {architecture} has an unsupported manifest.");
                    await DownloadLayerAsync(platformManifest.RootElement, archivePath, progress, token);
                }
            }
            else if (mediaType is OciManifest or DockerManifest)
            {
                // A single-platform image; the component manifest's runtime identifier is validated after extraction.
                await DownloadLayerAsync(manifest.RootElement, archivePath, progress, token);
            }
            else
            {
                throw new InvalidOperationException($"The component image has an unsupported manifest type '{mediaType}'.");
            }
        }
    }

    private static string SelectPlatformManifest(JsonElement index, string architecture)
    {
        foreach (var entry in index.GetProperty("manifests").EnumerateArray())
        {
            if (entry.TryGetProperty("platform", out var platform)
                && platform.TryGetProperty("os", out var os) && os.GetString() == "linux"
                && platform.TryGetProperty("architecture", out var arch) && arch.GetString() == architecture
                && entry.TryGetProperty("digest", out var digest))
                return digest.GetString()!;
        }

        throw new InvalidOperationException($"The component image has no linux/{architecture} variant.");
    }

    private async Task DownloadLayerAsync(JsonElement manifest, string archivePath, Action<long, long?> progress, CancellationToken token)
    {
        var layers = manifest.GetProperty("layers");
        if (layers.GetArrayLength() != 1)
            throw new InvalidOperationException("The component image must contain exactly one layer.");

        var layer = layers[0];
        var mediaType = layer.GetProperty("mediaType").GetString();
        if (!LayerMediaTypes.Contains(mediaType))
            throw new InvalidOperationException($"The component image layer has an unsupported type '{mediaType}'.");
        if (layer.GetProperty("size").GetInt64() > RenderingComponentDownload.MaximumComponentBytes)
            throw new InvalidOperationException("The component archive exceeds the allowed size.");

        var digest = layer.GetProperty("digest").GetString()!;
        var expected = ParseSha256Digest(digest);
        // Registries commonly redirect blob downloads to a CDN; HttpClient drops the Authorization header on redirects.
        using var response = await SendAsync($"blobs/{digest}", accept: null, HttpCompletionOption.ResponseHeadersRead, token);
        var actual = await RenderingComponentDownload.SaveAsync(response, archivePath, progress, token);
        RenderingComponentDownload.VerifySha256(actual, expected, "The downloaded component failed digest verification.");
    }

    private async Task<(string MediaType, JsonDocument Manifest)> GetManifestAsync(
        string reference, string? expectedDigest, CancellationToken token)
    {
        using var response = await SendAsync(
            $"manifests/{reference}",
            [OciIndex, DockerManifestList, OciManifest, DockerManifest],
            HttpCompletionOption.ResponseContentRead,
            token);
        if (response.Content.Headers.ContentLength is > MaximumManifestBytes)
            throw new InvalidOperationException("The component image manifest is too large.");

        var bytes = await response.Content.ReadAsByteArrayAsync(token);
        if (bytes.Length > MaximumManifestBytes)
            throw new InvalidOperationException("The component image manifest is too large.");
        if (expectedDigest is not null)
            RenderingComponentDownload.VerifySha256(
                SHA256.HashData(bytes), ParseSha256Digest(expectedDigest), "The component image manifest failed digest verification.");

        var document = JsonDocument.Parse(bytes);
        var mediaType = document.RootElement.TryGetProperty("mediaType", out var declared)
            ? declared.GetString()
            : response.Content.Headers.ContentType?.MediaType;
        return (mediaType ?? string.Empty, document);
    }

    private async Task<HttpResponseMessage> SendAsync(
        string path, string[]? accept, HttpCompletionOption completion, CancellationToken token)
    {
        var client = httpClientFactory.CreateClient(Constants.PlaywrightComponentHttpClientName);
        var uri = new Uri($"https://{image.Registry}/v2/{image.Repository}/{path}");

        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            foreach (var mediaType in accept ?? [])
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(mediaType));
            if (_token is not null)
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);

            var response = await client.SendAsync(request, completion, token);
            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                var challenge = response.Headers.WwwAuthenticate.FirstOrDefault(
                    value => string.Equals(value.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase));
                response.Dispose();
                if (challenge?.Parameter is null)
                    throw new InvalidOperationException($"The registry {image.Registry} requires unsupported authentication.");
                _token = await GetAnonymousTokenAsync(client, challenge.Parameter, token);
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                response.Dispose();
                throw new InvalidOperationException($"The component image {image} is unavailable ({status}).");
            }

            return response;
        }
    }

    private async Task<string> GetAnonymousTokenAsync(HttpClient client, string challenge, CancellationToken token)
    {
        var parameters = ParseChallenge(challenge);
        if (!parameters.TryGetValue("realm", out var realm)
            || !Uri.TryCreate(realm, UriKind.Absolute, out var realmUri)
            || realmUri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException($"The registry {image.Registry} sent an invalid authentication challenge.");

        var query = string.Join('&', parameters
            .Where(pair => pair.Key is "service" or "scope")
            .Select(pair => $"{pair.Key}={Uri.EscapeDataString(pair.Value)}"));
        var tokenUri = new UriBuilder(realmUri) { Query = query }.Uri;
        using var response = await client.GetAsync(tokenUri, token);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"The registry {image.Registry} refused anonymous access ({(int)response.StatusCode}).");

        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: token);
        var root = document.RootElement;
        var value = root.TryGetProperty("token", out var tokenValue) ? tokenValue.GetString()
            : root.TryGetProperty("access_token", out var accessToken) ? accessToken.GetString()
            : null;
        return string.IsNullOrEmpty(value)
            ? throw new InvalidOperationException($"The registry {image.Registry} returned no access token.")
            : value;
    }

    internal static Dictionary<string, string> ParseChallenge(string challenge)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var position = 0;
        while (position < challenge.Length)
        {
            var equals = challenge.IndexOf('=', position);
            if (equals < 0) break;
            var key = challenge[position..equals].Trim().TrimStart(',').Trim();
            position = equals + 1;
            string value;
            if (position < challenge.Length && challenge[position] == '"')
            {
                var end = challenge.IndexOf('"', position + 1);
                if (end < 0) break;
                value = challenge[(position + 1)..end];
                position = end + 1;
            }
            else
            {
                var end = challenge.IndexOf(',', position);
                if (end < 0) end = challenge.Length;
                value = challenge[position..end].Trim();
                position = end;
            }
            result[key] = value;
            while (position < challenge.Length && challenge[position] is ',' or ' ') position++;
        }
        return result;
    }

    private static string ParseSha256Digest(string digest) =>
        digest.StartsWith("sha256:", StringComparison.Ordinal) && digest.Length == 71
            ? digest["sha256:".Length..]
            : throw new InvalidOperationException($"Unsupported digest '{digest}'.");
}

/// <summary>A parsed <c>[registry/]repository[:tag][@sha256:digest]</c> image reference.</summary>
internal sealed record ComponentImageReference(string Registry, string Repository, string Tag, string? Digest)
{
    private const string DockerHubRegistry = "registry-1.docker.io";

    public static ComponentImageReference Parse(string value)
    {
        var reference = value.Trim();
        string? digest = null;
        var at = reference.IndexOf('@');
        if (at >= 0)
        {
            digest = reference[(at + 1)..];
            reference = reference[..at];
            if (!digest.StartsWith("sha256:", StringComparison.Ordinal) || digest.Length != 71
                || !digest["sha256:".Length..].All(Uri.IsHexDigit))
                throw new InvalidOperationException($"The rendering component image '{value}' has an invalid digest.");
            digest = digest.ToLowerInvariant();
        }

        var tag = "latest";
        var lastSlash = reference.LastIndexOf('/');
        var colon = reference.LastIndexOf(':');
        if (colon > lastSlash)
        {
            tag = reference[(colon + 1)..];
            reference = reference[..colon];
        }

        var registry = DockerHubRegistry;
        var firstSlash = reference.IndexOf('/');
        if (firstSlash > 0)
        {
            var host = reference[..firstSlash];
            if (host.Contains('.') || host.Contains(':') || host == "localhost")
            {
                registry = host is "docker.io" or "index.docker.io" ? DockerHubRegistry : host;
                reference = reference[(firstSlash + 1)..];
            }
        }
        if (registry == DockerHubRegistry && !reference.Contains('/'))
            reference = $"library/{reference}";

        if (string.IsNullOrEmpty(reference) || string.IsNullOrEmpty(tag)
            || reference.Any(character => !(char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character is '/' or '.' or '_' or '-')))
            throw new InvalidOperationException($"The rendering component image '{value}' is not a valid image reference.");

        return new ComponentImageReference(registry, reference, tag, digest);
    }

    public override string ToString() =>
        $"{Registry}/{Repository}{(Digest is null ? $":{Tag}" : $"@{Digest}")}";
}
