using System.Globalization;
using System.Security;
using System.Text;

namespace Oriel;

/// <summary>A notification the system shows outside the app's windows: see <see cref="OrielApp.ShowNotification"/>.</summary>
public sealed class Notification
{
    /// <summary>The first line, in bold.</summary>
    public required string Title { get; init; }

    /// <summary>The text under the title. Defaults to none.</summary>
    public string? Body { get; init; }

    /// <summary>Buttons under the text, five at most. Defaults to none.</summary>
    public IReadOnlyList<NotificationAction> Actions { get; init; } = [];
}

/// <summary>A button of a <see cref="Notification"/>.</summary>
/// <param name="id">What <see cref="NotificationClickedEventArgs.Action"/> tells, such as <c>reply</c>.</param>
/// <param name="label">What the button shows, such as <c>Reply</c>.</param>
public sealed class NotificationAction(string id, string label)
{
    /// <summary>What <see cref="NotificationClickedEventArgs.Action"/> tells.</summary>
    public string Id { get; } = !string.IsNullOrEmpty(id) ? id : throw new ArgumentException("An action needs an id.", nameof(id));

    /// <summary>What the button shows.</summary>
    public string Label { get; } = !string.IsNullOrEmpty(label) ? label : throw new ArgumentException("An action needs a label.", nameof(label));
}

/// <summary>The person clicked a notification of the app, or one of its buttons: see <see cref="OrielApp.NotificationClicked"/>.</summary>
public sealed class NotificationClickedEventArgs(Notification notification, NotificationAction? action) : EventArgs
{
    /// <summary>The notification clicked.</summary>
    public Notification Notification { get; } = notification;

    /// <summary>The button clicked, or null for the notification itself.</summary>
    public NotificationAction? Action { get; } = action;
}

/// <summary>The XML of toasts, and the arguments their clicks come back with.</summary>
internal static class ToastXml
{
    /// <summary>
    /// The toast of notification <paramref name="id"/>: a click on it comes
    /// back as <c>id</c>, one on its button <c>i</c> as <c>id:i</c>.
    /// </summary>
    public static string Of(long id, Notification notification)
    {
        var xml = new StringBuilder();
        var launch = id.ToString(CultureInfo.InvariantCulture);
        xml.Append($"<toast launch=\"{launch}\" activationType=\"foreground\"><visual><binding template=\"ToastGeneric\">");
        xml.Append($"<text>{Escape(notification.Title)}</text>");
        if (notification.Body is { } body)
            xml.Append($"<text>{Escape(body)}</text>");
        xml.Append("</binding></visual>");
        if (notification.Actions.Count > 0)
        {
            xml.Append("<actions>");
            for (var i = 0; i < notification.Actions.Count; i++)
                xml.Append($"<action content=\"{Escape(notification.Actions[i].Label)}\" arguments=\"{launch}:{i}\" activationType=\"foreground\"/>");
            xml.Append("</actions>");
        }
        xml.Append("</toast>");
        return xml.ToString();
    }

    /// <summary>The notification and button index of a click's arguments; false for arguments of none.</summary>
    public static bool TryParse(string arguments, out long id, out int? action)
    {
        action = null;
        var colon = arguments.IndexOf(':');
        var head = colon < 0 ? arguments : arguments[..colon];
        if (!long.TryParse(head, NumberStyles.None, CultureInfo.InvariantCulture, out id))
            return false;
        if (colon < 0)
            return true;
        if (!int.TryParse(arguments.AsSpan(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var index))
            return false;
        action = index;
        return true;
    }

    private static string Escape(string text) => SecurityElement.Escape(text);
}
