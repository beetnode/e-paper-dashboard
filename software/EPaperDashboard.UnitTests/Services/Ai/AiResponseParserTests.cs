using EPaperDashboard.Utilities;
using EPaperDashboard.Services.Ai;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace EPaperDashboard.UnitTests.Services.Ai;

public class AiResponseParserTests
{
    private static AiResponseParser CreateSut() => new(NullLogger<AiResponseParser>.Instance);

    [Fact]
    public void Parse_WellFormedJsonWithWidgets_ReturnsSuccessWithWidgets()
    {
        var sut = CreateSut();
        const string response = """
            {"widgets": [{"type": "header", "config": {"title": "Hi"}}, {"type": "markdown", "config": {"content": "text"}}]}
            """;

        var result = sut.Parse(response);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value[0].Id.Should().Be("header");
        result.Value[1].Id.Should().Be("markdown");
    }

    [Fact]
    public void Parse_ResponseWrappedInCodeFences_StripsFencesAndParses()
    {
        var sut = CreateSut();
        const string response = "```json\n{\"widgets\": [{\"type\": \"markdown\", \"config\": {}}]}\n```";

        var result = sut.Parse(response);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle();
    }

    [Fact]
    public void Parse_DuplicateWidgetTypes_AssignsIncrementingIds()
    {
        var sut = CreateSut();
        const string response = """
            {"widgets": [{"type": "markdown"}, {"type": "markdown"}]}
            """;

        var result = sut.Parse(response);

        result.IsSuccess.Should().BeTrue();
        result.Value.Select(w => w.Id).Should().Equal("markdown", "markdown-2");
    }

    [Fact]
    public void Parse_UnknownWidgetType_IsSkipped()
    {
        var sut = CreateSut();
        const string response = """
            {"widgets": [{"type": "not-a-real-widget"}, {"type": "markdown"}]}
            """;

        var result = sut.Parse(response);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle().Which.Type.Should().Be("markdown");
    }

    [Fact]
    public void Parse_MalformedJson_ReturnsFailureNotException()
    {
        var sut = CreateSut();

        var result = sut.Parse("{ not valid json");

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("not valid JSON");
    }

    [Fact]
    public void Parse_MissingWidgetsProperty_ReturnsFailure()
    {
        var sut = CreateSut();

        var result = sut.Parse("""{"foo": "bar"}""");

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("widgets");
    }

    [Fact]
    public void Parse_WidgetsArrayResultsInNoValidWidgets_ReturnsFailure()
    {
        var sut = CreateSut();

        var result = sut.Parse("""{"widgets": [{"type": "unknown-type"}]}""");

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("no valid widgets");
    }

    [Fact]
    public async Task RepairAndParseAsync_RepairLlmCallFails_ReturnsFailure()
    {
        var sut = CreateSut();
        var aiService = new Mock<IAiService>();
        aiService
            .Setup(s => s.GenerateCompletionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(Result.Failure<string>("llm down"));

        var result = await sut.RepairAndParseAsync(aiService.Object, "{broken", "parse error", CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("llm down");
    }

    [Fact]
    public async Task RepairAndParseAsync_RepairedResponseIsValid_ReturnsParsedWidgets()
    {
        var sut = CreateSut();
        var aiService = new Mock<IAiService>();
        aiService
            .Setup(s => s.GenerateCompletionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(Result.Success<string>("""{"widgets": [{"type": "markdown"}]}"""));

        var result = await sut.RepairAndParseAsync(aiService.Object, "{broken", "parse error", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle();
    }

    [Theory]
    [InlineData("header", true)]
    [InlineData("ai-content", true)]
    [InlineData("not-a-widget", false)]
    public void IsKnownWidgetType_ChecksAgainstAllowedList(string type, bool expected)
    {
        AiResponseParser.IsKnownWidgetType(type).Should().Be(expected);
    }
}
