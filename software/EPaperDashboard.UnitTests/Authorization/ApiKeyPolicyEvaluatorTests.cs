using EPaperDashboard.Authorization;
using EPaperDashboard.Data.Repositories;
using EPaperDashboard.Models;
using EPaperDashboard.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;
using Xunit;

namespace EPaperDashboard.UnitTests.Authorization;

public class ApiKeyPolicyEvaluatorTests
{
    private static ApiKeyPolicyEvaluator CreateSut(Mock<IDeviceRepository> deviceRepository) =>
        new(new DeviceService(deviceRepository.Object));

    [Fact]
    public void Evaluate_NullHttpContext_ReturnsFalse()
    {
        var sut = CreateSut(new Mock<IDeviceRepository>());

        sut.Evaluate(null).Should().BeFalse();
    }

    [Fact]
    public void Evaluate_MissingApiKeyHeader_ReturnsFalse()
    {
        var sut = CreateSut(new Mock<IDeviceRepository>());
        var context = new DefaultHttpContext();

        sut.Evaluate(context).Should().BeFalse();
    }

    [Fact]
    public void Evaluate_EmptyApiKeyHeader_ReturnsFalse()
    {
        var sut = CreateSut(new Mock<IDeviceRepository>());
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "";

        sut.Evaluate(context).Should().BeFalse();
    }

    [Fact]
    public void Evaluate_ApiKeyMatchesNoDevice_ReturnsFalse()
    {
        var deviceRepository = new Mock<IDeviceRepository>();
        deviceRepository.Setup(r => r.FindByApiKeyHash(DeviceApiKey.Hash("bad-key"))).Returns((Device?)null);
        var sut = CreateSut(deviceRepository);
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "bad-key";

        sut.Evaluate(context).Should().BeFalse();
    }

    [Fact]
    public void Evaluate_ApiKeyMatchesDevice_ReturnsTrue()
    {
        var deviceRepository = new Mock<IDeviceRepository>();
        deviceRepository.Setup(r => r.FindByApiKeyHash(DeviceApiKey.Hash("good-key"))).Returns(new Device { ApiKeyHash = DeviceApiKey.Hash("good-key") });
        var sut = CreateSut(deviceRepository);
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "good-key";

        sut.Evaluate(context).Should().BeTrue();
    }
}
