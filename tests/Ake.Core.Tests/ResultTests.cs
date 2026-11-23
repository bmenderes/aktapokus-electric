namespace Ake.Core.Tests;

public class ResultTests
{
    private static readonly Error SampleError = new("test.hata", "Örnek hata");

    [Fact]
    public void Success_has_no_error()
    {
        var result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Failure_carries_the_error()
    {
        var result = Result.Failure(SampleError);

        Assert.True(result.IsFailure);
        Assert.Equal(SampleError, result.Error);
    }

    [Fact]
    public void Failure_rejects_null_error()
    {
        Assert.Throws<ArgumentNullException>(() => Result.Failure(null!));
        Assert.Throws<ArgumentNullException>(() => Result.Failure<int>(null!));
    }

    [Fact]
    public void Generic_success_exposes_the_value()
    {
        var result = Result.Success(42);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void Reading_value_of_a_failure_throws()
    {
        var result = Result.Failure<int>(SampleError);

        var ex = Assert.Throws<InvalidOperationException>(() => result.Value);
        Assert.Contains(SampleError.Code, ex.Message);
    }
}
