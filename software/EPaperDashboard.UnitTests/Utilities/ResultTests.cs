using EPaperDashboard.Utilities;
using FluentAssertions;
using Xunit;

namespace EPaperDashboard.UnitTests.Utilities;

public class ResultTests
{
    [Fact]
    public void Success_IsSuccessful()
    {
        var result = Result.Success();

        result.IsSuccess.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
    }

    [Fact]
    public void Failure_CarriesError()
    {
        var result = Result.Failure("broken");

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be("broken");
    }

    [Fact]
    public void Error_OnSuccess_Throws()
    {
        var act = () => Result.Success().Error;

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void String_ConvertsToFailure()
    {
        Result result = "broken";

        result.Error.Should().Be("broken");
    }

    [Fact]
    public void Default_IsFailure()
    {
        default(Result).IsFailure.Should().BeTrue();
        default(Result<int>).IsFailure.Should().BeTrue();
    }

    [Fact]
    public void GenericSuccess_CarriesValue()
    {
        var result = Result.Success(42);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    [Fact]
    public void GenericFailure_CarriesError()
    {
        var result = Result.Failure<int>("broken");

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be("broken");
    }

    [Fact]
    public void Value_OnFailure_Throws()
    {
        var act = () => Result.Failure<int>("broken").Value;

        act.Should().Throw<InvalidOperationException>().WithMessage("*broken*");
    }

    [Fact]
    public void Error_OnGenericSuccess_Throws()
    {
        var act = () => Result.Success(1).Error;

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ImplicitConversions_BuildSuccessAndFailure()
    {
        Result<int> success = 5;
        Result<int> failure = "broken";

        success.Value.Should().Be(5);
        failure.Error.Should().Be("broken");
    }

    [Fact]
    public void Map_TransformsValue_AndKeepsFailure()
    {
        Result.Success(2).Map(x => x * 10).Value.Should().Be(20);
        Result.Failure<int>("broken").Map(x => x * 10).Error.Should().Be("broken");
    }

    [Fact]
    public void Bind_ChainsResults_AndKeepsFirstFailure()
    {
        Result.Success(2).Bind(x => Result.Success($"#{x}")).Value.Should().Be("#2");
        Result.Success(2).Bind(_ => Result.Failure<string>("second")).Error.Should().Be("second");
        Result.Failure<int>("first").Bind(x => Result.Success($"#{x}")).Error.Should().Be("first");
    }

    [Fact]
    public void Match_PicksBranch()
    {
        Result.Success(2).Match(x => $"ok {x}", e => $"fail {e}").Should().Be("ok 2");
        Result.Failure<int>("broken").Match(x => $"ok {x}", e => $"fail {e}").Should().Be("fail broken");
    }

    [Fact]
    public async Task MapAsync_TransformsAwaitedResult()
    {
        var result = await Task.FromResult(Result.Success(2)).Map(x => x + 1);

        result.Value.Should().Be(3);
    }

    [Fact]
    public async Task Try_CapturesValue()
    {
        var result = await Result.Try(() => Task.FromResult(7), e => e.Message);

        result.Value.Should().Be(7);
    }

    [Fact]
    public async Task Try_TurnsExceptionIntoFailure()
    {
        var result = await Result.Try<int>(() => throw new InvalidOperationException("boom"), e => $"handled {e.Message}");

        result.Error.Should().Be("handled boom");
    }
}
