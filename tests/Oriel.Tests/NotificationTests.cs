using Oriel.Tests.Fakes;

namespace Oriel.Tests;

public class NotificationTests
{
    private readonly FakePlatform _platform = new();

    private OrielApp App(string name = "Notes & Co 2") => new(new AppOptions { Name = name, AssetsDirectory = @"C:\app\wwwroot" }, _platform, null);

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 400 && !condition(); i++)
            await Task.Delay(5);
        Assert.True(condition());
    }

    [Fact]
    public void WritesToastsWithTheirButtons()
    {
        var xml = ToastXml.Of(7, new Notification
        {
            Title = "<New> & \"urgent\"",
            Body = "From Ada",
            Actions = [new NotificationAction("reply", "Reply"), new NotificationAction("mute", "Mute <1h>")],
        });
        Assert.Equal(
            "<toast launch=\"7\" activationType=\"foreground\"><visual><binding template=\"ToastGeneric\">"
            + "<text>&lt;New&gt; &amp; &quot;urgent&quot;</text><text>From Ada</text></binding></visual>"
            + "<actions><action content=\"Reply\" arguments=\"7:0\" activationType=\"foreground\"/>"
            + "<action content=\"Mute &lt;1h&gt;\" arguments=\"7:1\" activationType=\"foreground\"/></actions></toast>",
            xml);
        Assert.Equal("<toast launch=\"8\" activationType=\"foreground\"><visual><binding template=\"ToastGeneric\"><text>T</text></binding></visual></toast>",
            ToastXml.Of(8, new Notification { Title = "T" }));
    }

    [Theory]
    [InlineData("7", 7L, null)]
    [InlineData("7:1", 7L, 1)]
    public void ReadsTheArgumentsOfClicks(string arguments, long id, int? action)
    {
        Assert.True(ToastXml.TryParse(arguments, out var readId, out var readAction));
        Assert.Equal((id, action), (readId, readAction));
    }

    [Theory]
    [InlineData("")]
    [InlineData("x")]
    [InlineData("7:")]
    [InlineData("7:x")]
    [InlineData("-1")]
    public void IgnoresOtherArguments(string arguments) => Assert.False(ToastXml.TryParse(arguments, out _, out _));

    [Fact]
    public async Task TellsWhichNotificationAndButtonWereClicked()
    {
        var app = App();
        var run = Task.Run(() => app.Run());
        var clicked = new List<NotificationClickedEventArgs>();
        app.NotificationClicked += (_, e) => clicked.Add(e);
        var reply = new NotificationAction("reply", "Reply");
        var message = new Notification { Title = "Message", Actions = [reply] };
        app.ShowNotification(message);
        await WaitUntil(() => _platform.Notifications.Count == 1);
        var (appId, appName, _, activated) = Assert.Single(_platform.Notifications);
        Assert.Equal(("Oriel.NotesCo2", "Notes & Co 2"), (appId, appName));

        activated("1"); // from a thread of the system's
        activated("1:0");
        activated("1:5"); // no such button: the notification itself
        activated("99"); // not one of the app's
        await WaitUntil(() => clicked.Count == 3);
        await Task.Delay(50);
        Assert.Equal(3, clicked.Count);
        Assert.All(clicked, c => Assert.Same(message, c.Notification));
        Assert.Equal([null, reply, null], clicked.Select(c => c.Action));
        app.Quit();
        await run.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void RefusesNotificationsThatDoNotFit()
    {
        var app = App();
        Assert.Throws<ArgumentException>(() => app.ShowNotification(new Notification { Title = " " }));
        Assert.Throws<ArgumentException>(() => app.ShowNotification(new Notification
        {
            Title = "T",
            Actions = [.. Enumerable.Range(0, 6).Select(i => new NotificationAction($"a{i}", "A"))],
        }));
        Assert.Throws<ArgumentException>(() => app.ShowNotification(new Notification
        {
            Title = "T",
            Actions = [new NotificationAction("a", "A"), new NotificationAction("a", "B")],
        }));
        Assert.Throws<ArgumentException>(() => new NotificationAction("", "A"));
        _platform.SupportsNotifications = false;
        Assert.Throws<NotSupportedException>(() => app.ShowNotification(new Notification { Title = "T" }));
    }
}
