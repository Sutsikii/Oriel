namespace Oriel.Tests;

public class NavigationPolicyTests
{
    private static readonly Frontend s_app = new(null, "wwwroot");
    private static readonly Frontend s_dev = new("http://localhost:5173", "wwwroot");

    [Theory]
    [InlineData("https://app.oriel/settings.html", NavigationAction.Allow)]
    [InlineData("https://example.com/", NavigationAction.OpenExternally)]
    [InlineData("http://app.oriel/", NavigationAction.OpenExternally)]
    [InlineData("mailto:someone@example.com", NavigationAction.OpenExternally)]
    [InlineData("about:blank", NavigationAction.Allow)]
    [InlineData("data:text/html,hi", NavigationAction.Allow)]
    [InlineData("file:///C:/Windows/notepad.exe", NavigationAction.Deny)]
    [InlineData("ms-msdt:/id", NavigationAction.Deny)]
    [InlineData("javascript:alert(1)", NavigationAction.Deny)]
    public void KeepsTheAppsPagesInTheWindow(string uri, NavigationAction expected)
    {
        Assert.Equal(expected, NavigationPolicy.Decide(s_app, new Uri(uri), isNewWindow: false));
    }

    [Theory]
    [InlineData("https://app.oriel/other.html", NavigationAction.Allow)]
    [InlineData("https://example.com/", NavigationAction.OpenExternally)]
    [InlineData("about:blank", NavigationAction.Deny)]
    [InlineData("blob:https://app.oriel/1234", NavigationAction.Deny)]
    public void OpensWindowsForTheAppsPagesOnly(string uri, NavigationAction expected)
    {
        Assert.Equal(expected, NavigationPolicy.Decide(s_app, new Uri(uri), isNewWindow: true));
    }

    [Theory]
    [InlineData("http://localhost:5173/settings", NavigationAction.Allow)]
    [InlineData("https://app.oriel/", NavigationAction.OpenExternally)]
    [InlineData("http://localhost:3000/", NavigationAction.OpenExternally)]
    public void KeepsTheDevServersPagesInTheWindowInDevelopment(string uri, NavigationAction expected)
    {
        Assert.Equal(expected, NavigationPolicy.Decide(s_dev, new Uri(uri), isNewWindow: false));
    }
}
