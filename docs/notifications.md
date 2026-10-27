# Notifications

`OrielApp.ShowNotification` shows a notification outside the app's
windows, in the system's notification center: a message that came, an
export done, a reminder. It can have buttons.

```csharp
app.NotificationClicked += (_, e) =>
{
    if (e.Action?.Id == "reply")
        OpenReply();
    else
        window.Show(); // the notification itself was clicked
};

app.ShowNotification(new Notification
{
    Title = "Ada",
    Body = "Lunch at noon?",
    Actions = [new NotificationAction("reply", "Reply"), new NotificationAction("later", "Later")],
});
```

- `Title` is required; `Body` and up to five `Actions` are not. Each action
  has an `Id` of its own, which the click tells, and the `Label` its button
  shows.
- `NotificationClicked` is raised on the UI thread when the person clicks
  the notification (`Action` is null) or one of its buttons, while the app
  runs. A click after the app quit opens nothing.
- Any thread may call `ShowNotification`. What fails on the system's side
  goes to `OrielApp.Error`.

## On Windows

Notifications are toasts, in the notification center. Windows shows the
toasts of an app that is not packaged under an id it registers for the
user, named after `AppOptions.Name`: the key
`HKCU\Software\Classes\AppUserModelId\Oriel.<letters and digits of the name>`,
written at the first notification, with the app's name as `DisplayName`.
The person can turn the app's notifications off in Settings, like any
app's; they then show nothing, and raise nothing.

## Elsewhere

Notifications are in the Windows backend; on Linux and macOS,
`ShowNotification` throws `NotSupportedException` for now.
