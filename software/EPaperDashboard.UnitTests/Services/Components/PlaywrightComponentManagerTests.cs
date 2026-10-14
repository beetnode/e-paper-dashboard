using EPaperDashboard.RenderingComponent;
using EPaperDashboard.Services.Components.Playwright;
using EPaperDashboard.Utilities;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace EPaperDashboard.UnitTests.Services.Components;

public sealed class PlaywrightComponentManagerTests : IDisposable
{
    private readonly string _dataDirectory = Path.Combine(
        Path.GetTempPath(), $"izboard-component-tests-{Guid.NewGuid():N}");

    [Fact]
    public void GetStatus_WhenComponentIsAbsent_ReturnsNotInstalled()
    {
        var manager = CreateManager();

        manager.GetStatus().State.Should().Be(PlaywrightComponentState.NotInstalled);
    }

    [Fact]
    public void GetStatus_WhenComponentSpeaksAnotherProtocol_ReturnsIncompatible()
    {
        CreateComponent(999, RenderingComponentInfo.Version);
        var manager = CreateManager();

        var status = manager.GetStatus();

        status.State.Should().Be(PlaywrightComponentState.Incompatible);
        status.InstalledVersion.Should().Be(RenderingComponentInfo.Version);
        status.Error.Should().Contain("protocol");
        manager.ShouldAutomaticallyUpdate().Should().Be(manager.GetStatus().Supported);
    }

    [Theory]
    [InlineData(RenderingComponentInfo.Version)]
    [InlineData(RenderingComponentInfo.Version + ".900")]
    public void GetStatus_WhenComponentMatchesExpectedRelease_ReturnsInstalledWithoutUpdate(string componentVersion)
    {
        CreateComponent(RenderingComponentInfo.ProtocolVersion, componentVersion);
        var manager = CreateManager();

        var status = manager.GetStatus();

        status.State.Should().Be(PlaywrightComponentState.Installed);
        status.InstalledVersion.Should().Be(componentVersion);
        manager.GetActiveComponent().Should().NotBeNull();
        manager.ShouldAutomaticallyUpdate().Should().BeFalse();
    }

    [Fact]
    public void GetStatus_WhenCompatibleComponentIsAnotherRelease_StaysUsableAndUpdates()
    {
        CreateComponent(RenderingComponentInfo.ProtocolVersion, "0.0.1");
        var manager = CreateManager();

        manager.GetStatus().State.Should().Be(PlaywrightComponentState.Installed);
        manager.GetActiveComponent().Should().NotBeNull();
        manager.ShouldAutomaticallyUpdate().Should().Be(manager.GetStatus().Supported);
    }

    [Fact]
    public void GetStatus_WhenComponentUsesApplicationVersionedManifest_IsReplaced()
    {
        CreateComponent(RenderingComponentInfo.ProtocolVersion, "0.5.0.849", schemaVersion: 2);
        var manager = CreateManager();

        manager.GetStatus().State.Should().NotBe(PlaywrightComponentState.Installed);
        manager.ShouldAutomaticallyUpdate().Should().Be(manager.GetStatus().Supported);
    }

    [Fact]
    public async Task UninstallAsync_RemovesEntireComponentDirectory()
    {
        CreateComponent(RenderingComponentInfo.ProtocolVersion, RenderingComponentInfo.Version);
        var componentRoot = Path.Combine(_dataDirectory, "components", "playwright");
        File.WriteAllText(Path.Combine(componentRoot, "unused-download"), "test");
        var manager = CreateManager();

        await manager.UninstallAsync();

        Directory.Exists(componentRoot).Should().BeFalse();
        manager.GetStatus().State.Should().Be(PlaywrightComponentState.NotInstalled);
        manager.ShouldAutomaticallyUpdate().Should().BeFalse();
    }

    [Fact]
    public void Constructor_WhenComponentBaseUrlIsInvalid_Throws()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RENDERING_COMPONENT_BASE_URL"] = "not-a-url"
            })
            .Build();

        var action = () => CreateManager(configuration);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*rendering component base URL*");
    }

    [Theory]
    [InlineData("1.0.0", "rendering-v1.0.0")]
    [InlineData("1.0.0.42", "rendering-v1.0.0")]
    [InlineData("1.0.0+abcdef", "rendering-v1.0.0")]
    public void GetDefaultReleaseTag_AddressesComponentRelease(string version, string expected)
    {
        PlaywrightComponentManager.GetDefaultReleaseTag(version).Should().Be(expected);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
            Directory.Delete(_dataDirectory, recursive: true);
    }

    private PlaywrightComponentManager CreateManager(IConfiguration? configuration = null)
    {
        var environment = new Mock<IEnvironmentConfiguration>();
        environment.SetupGet(value => value.ConfigDir).Returns(_dataDirectory);

        return new PlaywrightComponentManager(
            Mock.Of<IHttpClientFactory>(),
            environment.Object,
            configuration ?? new ConfigurationBuilder().Build(),
            NullLogger<PlaywrightComponentManager>.Instance);
    }

    private void CreateComponent(int protocolVersion, string componentVersion, int schemaVersion = 3)
    {
        var root = Path.Combine(_dataDirectory, "components", "playwright", "current");
        Directory.CreateDirectory(Path.Combine(root, "worker"));
        Directory.CreateDirectory(Path.Combine(root, "browsers"));
        Directory.CreateDirectory(Path.Combine(root, "native", "lib"));
        File.WriteAllText(Path.Combine(root, "worker", "EPaperDashboard.PlaywrightComponent.dll"), "test");
        File.WriteAllText(Path.Combine(root, "component-manifest.json"), $$"""
            {
              "schemaVersion": {{schemaVersion}},
              "protocolVersion": {{protocolVersion}},
              "appVersion": "0.5.0",
              "componentVersion": "{{componentVersion}}",
              "runtimeIdentifier": "{{PlaywrightComponentManager.GetRuntimeIdentifier()}}",
              "workerAssembly": "worker/EPaperDashboard.PlaywrightComponent.dll",
              "browserPath": "browsers",
              "libraryPath": "native/lib"
            }
            """);
    }
}
