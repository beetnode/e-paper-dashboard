using EPaperDashboard.Services;
using FluentAssertions;
using Xunit;

namespace EPaperDashboard.UnitTests.Services;

public class PasswordHashingTests
{
    [Fact]
    public void Hash_IsSaltedAndNotTheLegacyFormat()
    {
        var first = PasswordHashing.Hash("secret");
        var second = PasswordHashing.Hash("secret");

        first.Should().NotBe(second);
        first.Should().NotBe(PasswordHashing.LegacySha256("secret"));
        first.Should().NotMatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void Verify_CurrentHash_AcceptsOnlyTheMatchingPassword()
    {
        var hash = PasswordHashing.Hash("secret");

        PasswordHashing.Verify(hash, "secret").Should().Be(PasswordCheck.Valid);
        PasswordHashing.Verify(hash, "wrong").Should().Be(PasswordCheck.Failed);
    }

    [Fact]
    public void Verify_LegacyHash_AcceptsTheMatchingPasswordAndAsksForRehash()
    {
        var legacy = PasswordHashing.LegacySha256("secret");

        PasswordHashing.Verify(legacy, "secret").Should().Be(PasswordCheck.ValidNeedsRehash);
        PasswordHashing.Verify(legacy.ToUpperInvariant(), "secret").Should().Be(PasswordCheck.ValidNeedsRehash);
        PasswordHashing.Verify(legacy, "wrong").Should().Be(PasswordCheck.Failed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    public void Verify_UnreadableHash_Fails(string stored) =>
        PasswordHashing.Verify(stored, "secret").Should().Be(PasswordCheck.Failed);
}
