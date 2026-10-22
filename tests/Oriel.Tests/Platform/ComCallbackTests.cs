using Oriel.Platform.Windows;

namespace Oriel.Tests.Platform;

public class ComCallbackTests
{
    private static readonly Guid s_iid = new("4e8a3389-c9d8-4bd2-b6b5-124fee6cc14d");

    [Fact]
    public void CountsCallbacksUntilTheirLastRelease()
    {
        var before = ComCallback.Live;
        var handler = ComCallback.ForEvent(s_iid, (_, _) => { });
        var result = ComCallback.ForResult(s_iid, (_, _) => { });
        Assert.Equal(before + 2, ComCallback.Live);

        // A reference WebView2 takes, as when it keeps an event handler.
        Com.AddRef(handler);
        Com.Release(handler);
        Assert.Equal(before + 2, ComCallback.Live);

        Com.Release(handler);
        Com.Release(result);
        Assert.Equal(before, ComCallback.Live);
    }
}
