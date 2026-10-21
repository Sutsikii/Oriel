using Oriel.Tests.Fakes;

namespace Oriel.Tests;

public class RecoveryPolicyTests
{
    private readonly ManualTime _time = new();

    [Theory]
    [InlineData(WebViewFailure.RendererExited, WebViewRecovery.Reload)]
    [InlineData(WebViewFailure.RendererUnresponsive, WebViewRecovery.Reload)]
    [InlineData(WebViewFailure.BrowserExited, WebViewRecovery.Recreate)]
    [InlineData(WebViewFailure.FrameRendererExited, WebViewRecovery.None)]
    [InlineData(WebViewFailure.GpuExited, WebViewRecovery.None)]
    [InlineData(WebViewFailure.Other, WebViewRecovery.None)]
    public void RecoversWhatTheFailureBroke(WebViewFailure failure, WebViewRecovery expected)
    {
        Assert.Equal(expected, new RecoveryPolicy(_time).Decide(failure));
    }

    [Fact]
    public void StopsRecoveringAPageThatKeepsFailing()
    {
        var policy = new RecoveryPolicy(_time);
        for (var i = 0; i < RecoveryPolicy.MaxRecoveries; i++)
        {
            Assert.Equal(WebViewRecovery.Reload, policy.Decide(WebViewFailure.RendererExited));
            _time.Advance(TimeSpan.FromSeconds(5));
        }
        Assert.Equal(WebViewRecovery.ErrorPage, policy.Decide(WebViewFailure.RendererExited));
        Assert.Equal(WebViewRecovery.ErrorPage, policy.Decide(WebViewFailure.BrowserExited));
    }

    [Fact]
    public void ForgetsFailuresOlderThanAMinute()
    {
        var policy = new RecoveryPolicy(_time);
        for (var i = 0; i < RecoveryPolicy.MaxRecoveries; i++)
            policy.Decide(WebViewFailure.RendererExited);
        _time.Advance(RecoveryPolicy.Window + TimeSpan.FromSeconds(1));
        Assert.Equal(WebViewRecovery.Reload, policy.Decide(WebViewFailure.RendererExited));
    }

    [Fact]
    public void FailuresWithoutRecoveryDoNotCount()
    {
        var policy = new RecoveryPolicy(_time);
        for (var i = 0; i < 10; i++)
            policy.Decide(WebViewFailure.GpuExited);
        Assert.Equal(WebViewRecovery.Reload, policy.Decide(WebViewFailure.RendererExited));
    }
}
