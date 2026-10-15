using EPaperDashboard.Utilities;

namespace EPaperDashboard.Services.Components.Playwright;

/// <summary>
/// Downloads <c>rendering-component-{runtime}.tar.gz</c> and its <c>.sha256</c> from a plain HTTP directory,
/// used by the local deployment test stack and by mirrors without a container registry.
/// </summary>
internal sealed class UrlComponentSource(IHttpClientFactory httpClientFactory, Uri baseUri) : IRenderingComponentSource
{
    public string Description => baseUri.AbsoluteUri;

    public async Task DownloadAsync(string runtimeIdentifier, string archivePath, Action<long, long?> progress, CancellationToken token)
    {
        var client = httpClientFactory.CreateClient(Constants.PlaywrightComponentHttpClientName);
        var assetName = $"rendering-component-{runtimeIdentifier}.tar.gz";
        var archiveUrl = new Uri(baseUri, Uri.EscapeDataString(assetName));
        var checksumUrl = new Uri(baseUri, Uri.EscapeDataString($"{assetName}.sha256"));

        var checksumText = await client.GetStringAsync(checksumUrl, token);
        var expectedChecksum = checksumText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (expectedChecksum is null || expectedChecksum.Length != 64 || !expectedChecksum.All(Uri.IsHexDigit))
            throw new InvalidOperationException("The component checksum is invalid.");

        using var response = await client.GetAsync(archiveUrl, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        var actualChecksum = await RenderingComponentDownload.SaveAsync(response, archivePath, progress, token);
        RenderingComponentDownload.VerifySha256(
            actualChecksum, expectedChecksum, "The downloaded component failed checksum verification.");
    }

    internal static Uri? ParseBaseUri(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (!Uri.TryCreate($"{value.TrimEnd('/')}/", UriKind.Absolute, out var uri)
            || (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("The rendering component base URL must be an absolute HTTP or HTTPS URL.");

        return uri;
    }
}
