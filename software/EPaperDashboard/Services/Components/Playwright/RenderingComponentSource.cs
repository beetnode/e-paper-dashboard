using System.Security.Cryptography;

namespace EPaperDashboard.Services.Components.Playwright;

/// <summary>Downloads the rendering component archive (a gzip-compressed tar) for one runtime.</summary>
internal interface IRenderingComponentSource
{
    string Description { get; }

    /// <summary>Writes the verified archive to <paramref name="archivePath"/>.</summary>
    Task DownloadAsync(string runtimeIdentifier, string archivePath, Action<long, long?> progress, CancellationToken token);
}

internal static class RenderingComponentDownload
{
    internal const long MaximumComponentBytes = 1_500_000_000;

    /// <summary>Streams a response body to a new file and returns the SHA-256 of the written bytes.</summary>
    internal static async Task<byte[]> SaveAsync(
        HttpResponseMessage response,
        string destination,
        Action<long, long?> progress,
        CancellationToken token)
    {
        var totalBytes = response.Content.Headers.ContentLength;
        if (totalBytes is > MaximumComponentBytes)
            throw new InvalidOperationException("The component archive exceeds the allowed size.");

        progress(0, totalBytes);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var input = await response.Content.ReadAsStreamAsync(token);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81_920, true);
        var buffer = new byte[81_920];
        long downloaded = 0;

        while (true)
        {
            var read = await input.ReadAsync(buffer, token);
            if (read == 0)
                break;

            downloaded += read;
            if (downloaded > MaximumComponentBytes)
                throw new InvalidOperationException("The component archive exceeds the allowed size.");

            hash.AppendData(buffer, 0, read);
            await output.WriteAsync(buffer.AsMemory(0, read), token);
            progress(downloaded, totalBytes);
        }

        return hash.GetHashAndReset();
    }

    internal static void VerifySha256(byte[] actual, string expectedHex, string message)
    {
        if (expectedHex.Length != 64 || !expectedHex.All(Uri.IsHexDigit)
            || !CryptographicOperations.FixedTimeEquals(actual, Convert.FromHexString(expectedHex)))
            throw new InvalidOperationException(message);
    }
}
