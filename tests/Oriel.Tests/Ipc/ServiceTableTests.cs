using System.Text.Json;
using Oriel.Generated;
using Oriel.Ipc;

namespace Oriel.Tests.Ipc;

public class ServiceTableTests
{
    private sealed class Named(string name) : ServiceDispatcher(name)
    {
        public override ValueTask<ValueWriter?> InvokeAsync(string method, JsonElement args, CallContext context) =>
            ValueTask.FromResult<ValueWriter?>(null);
    }

    [Fact]
    public void FindsServicesByName()
    {
        var table = new ServiceTable();
        var a = new Named("A");
        table.Add(a);
        Assert.True(table.TryGet("A", out var found));
        Assert.Same(a, found);
        Assert.False(table.TryGet("a", out _));
    }

    [Fact]
    public void RefusesTwoServicesOfOneName()
    {
        var table = new ServiceTable();
        table.Add(new Named("A"));
        var e = Assert.Throws<InvalidOperationException>(() => table.Add(new Named("A")));
        Assert.Equal("A service named A is already bound.", e.Message);
    }
}
