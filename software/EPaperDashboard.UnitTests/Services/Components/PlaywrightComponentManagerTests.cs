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
    public void GetStatus_WhenComponentTargetsAnotherAppVersion_ReturnsIncompatible()
    {
        CreateComponent("999.0.0", "999.0.0");
        var manager = CreateManager();

        var status = manager.GetStatus();

        status.State.Should().Be(PlaywrightComponentState.Incompatible);
        status.InstalledVersion.Should().Be("999.0.0");
        status.Error.Should().Contain(PlaywrightComponentManager.GetCompatibilityVersion(Constants.AppVersion));
        manager.ShouldAutomaticallyUpdate().Should().Be(manager.GetStatus().Supported);
    }

    [Fact]
    public void GetStatus_WhenComponentMatchesAppVersion_ReturnsInstalled()
    {
        CreateComponent(PlaywrightComponentManager.GetCompatibilityVersion(Constants.AppVersion), Constants.AppVersion);
        var manager = CreateManager();

        var status = manager.GetStatus();

        status.State.Should().Be(PlaywrightComponentState.Installed);
        status.InstalledVersion.Should().Be(Constants.AppVersion);
        manager.GetActiveComponent().Should().NotBeNull();
        manager.ShouldAutomaticallyUpdate().Should().BeFalse();
    }

    [Fact]
    public void GetStatus_WhenComponentBuildDoesNotMatch_ReturnsIncompatible()
    {
        CreateComponent(PlaywrightComponentManager.GetCompatibilityVersion(Constants.AppVersion), "0.0.0.1");
        var manager = CreateManager();

        manager.GetStatus().State.Should().Be(PlaywrightComponentState.Incompatible);
        manager.ShouldAutomaticallyUpdate().Should().Be(manager.GetStatus().Supported);
    }

    [Fact]
    public async Task UninstallAsync_RemovesEntireComponentDirectory()
    {
        CreateComponent(PlaywrightComponentManager.GetCompatibilityVersion(Constants.AppVersion), Constants.AppVersion);
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
    [InlineData("0.6.0", "v0.6.0")]
    [InlineData("0.6.0.900", "v0.6.0")]
    [InlineData("0.6.0+abcdef", "v0.6.0")]
    [InlineData("not-a-version", "vnot-a-version")]
    public void GetDefaultReleaseTag_ReturnsApplicationRelease(string version, string expected)
    {
        PlaywrightComponentManager.GetDefaultReleaseTag(version).Should().Be(expected);
    }

    [Theory]
    [InlineData("0.4.1", "0.4.1")]
    [InlineData("0.4.1.42", "0.4.1")]
    [InlineData("0.4.1+abcdef", "0.4.1")]
    public void GetCompatibilityVersion_DropsBuildMetadata(string version, string expected)
    {
        PlaywrightComponentManager.GetCompatibilityVersion(version).Should().Be(expected);
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

    private void CreateComponent(string appVersion, string componentVersion)
    {
        var root = Path.Combine(_dataDirectory, "components", "playwright", "current");
        Directory.CreateDirectory(Path.Combine(root, "worker"));
        Directory.CreateDirectory(Path.Combine(root, "browsers"));
        Directory.CreateDirectory(Path.Combine(root, "native", "lib"));
        File.WriteAllText(Path.Combine(root, "worker", "EPaperDashboard.PlaywrightComponent.dll"), "test");
        File.WriteAllText(Path.Combine(root, "component-manifest.json"), $$"""
            {
              "schemaVersion": 2,
              "appVersion": "{{appVersion}}",
              "componentVersion": "{{componentVersion}}",
              "runtimeIdentifier": "{{PlaywrightComponentManager.GetRuntimeIdentifier()}}",
              "workerAssembly": "worker/EPaperDashboard.PlaywrightComponent.dll",
              "browserPath": "browsers",
              "libraryPath": "native/lib"
            }
            """);
    }
}
