using EPaperDashboard.Services;
using FluentAssertions;
using Xunit;

namespace EPaperDashboard.UnitTests.Services;

public class DeviceApiKeyTests
{
    [Fact]
    public void Hash_IsLowercaseHexSha256()
    {
        DeviceApiKey.Hash("abc").Should().Be("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
    }

    [Fact]
    public void Hash_DifferentKeys_DifferentHashes()
    {
        DeviceApiKey.Hash("key-1").Should().NotBe(DeviceApiKey.Hash("key-2"));
    }

    [Fact]
    public void Generate_ProducesDistinctUrlSafeKeysOf32Bytes()
    {
        var first = DeviceApiKey.Generate();
        var second = DeviceApiKey.Generate();

        first.Should().MatchRegex("^[A-Za-z0-9_-]{43}$");
        second.Should().NotBe(first);
    }
}
