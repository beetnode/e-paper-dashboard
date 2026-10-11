using EPaperDashboard.Services;
using EPaperDashboard.Services.Plugins;
using EPaperDashboard.Services.Plugins.HomeAssistant;
using FluentAssertions;
using Xunit;

namespace EPaperDashboard.UnitTests.Services.Plugins.HomeAssistant;

public class HomeAssistantPluginCapabilitiesTests
{
    private static string[] For(string domain, string? state = null) =>
        HomeAssistantPlugin.Capabilities(new HassEntity { EntityId = domain + ".x", Domain = domain, State = state });

    [Theory]
    [InlineData("sensor", "unknown")]
    [InlineData("light", "on")]
    [InlineData("input_text", "21.5")]
    public void GraphableDomainsOrNumericStates_HaveHistory(string domain, string state)
    {
        For(domain, state).Should().Contain(SourceCapabilities.History);
    }

    [Fact]
    public void NonNumericStatesOfOtherDomains_HaveNoHistory()
    {
        For("input_text", "hello").Should().NotContain(SourceCapabilities.History).And.Contain(SourceCapabilities.State);
    }

    [Theory]
    [InlineData("calendar", SourceCapabilities.Calendar)]
    [InlineData("todo", SourceCapabilities.Todo)]
    [InlineData("weather", SourceCapabilities.Forecast)]
    [InlineData("event", SourceCapabilities.Feed)]
    public void DomainsMapToNormalizedCapabilities(string domain, string capability)
    {
        For(domain).Should().Contain(capability);
    }
}
