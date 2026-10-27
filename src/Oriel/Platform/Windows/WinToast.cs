using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
using Microsoft.Win32;

namespace Oriel.Platform.Windows;

/// <summary>
/// Toast notifications, through WinRT's ToastNotificationManager called by
/// vtable slot, as WebView2 is. An app that is not packaged shows them
/// under an AppUserModelID it registers for the user, with its name. Clicks
/// on the toast or its buttons come back through the toast's Activated
/// event, while the app runs.
/// </summary>
[SupportedOSPlatform("windows10.0.17763")]
internal static unsafe partial class WinToast
{
    private static readonly Guid IID_IXmlDocument = new("F7F3A506-1E87-42D6-BCFB-B8C809FA5494");
    private static readonly Guid IID_IXmlDocumentIO = new("6CD0E74E-EE65-4489-9EBF-CA43E87BA637");
    private static readonly Guid IID_IToastNotificationFactory = new("04124B20-82C6-4229-B109-FD9ED4662B53");
    private static readonly Guid IID_IToastNotificationManagerStatics = new("50AC103F-D235-4598-BBEF-98FE4D1A3AD4");
    private static readonly Guid IID_IToastActivatedEventArgs = new("E3BF92F3-C197-436F-8265-0625824F8DAC");

    // TypedEventHandler<ToastNotification, Object>: the type of Activated.
    private static readonly Guid IID_ActivatedHandler = new("AB54DE2D-97D9-5528-B6AD-105AFE156530");

    // The toasts shown, kept with their handlers: a toast dropped would raise nothing.
    private static readonly Queue<nint> s_shown = new();

    /// <summary>
    /// Shows <paramref name="xml"/>, a toast's XML, under <paramref name="appId"/>.
    /// <paramref name="activated"/> gets the arguments of what the person
    /// clicked, on a thread of WinRT's.
    /// </summary>
    public static void Show(string appId, string appName, string xml, Action<string> activated)
    {
        Register(appId, appName);
        nint document = 0, io = 0, factory = 0, toast = 0, statics = 0, notifier = 0, handler = 0;
        try
        {
            document = Activate("Windows.Data.Xml.Dom.XmlDocument");
            io = Query(document, IID_IXmlDocumentIO, "IXmlDocumentIO");
            using (var text = new HString(xml))
                Com.Check(((delegate* unmanaged[Stdcall]<nint, nint, int>)Com.Slot(io, 6))(io, text.Handle), "LoadXml");
            var xmlDocument = Query(document, IID_IXmlDocument, "IXmlDocument");
            try
            {
                factory = Factory("Windows.UI.Notifications.ToastNotification", IID_IToastNotificationFactory);
                Com.Check(((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)Com.Slot(factory, 6))(factory, xmlDocument, &toast), "CreateToastNotification");
            }
            finally
            {
                Com.Release(xmlDocument);
            }

            handler = ComCallback.ForAgileEvent(IID_ActivatedHandler, (_, args) => activated(Arguments(args)));
            long token;
            Com.Check(((delegate* unmanaged[Stdcall]<nint, nint, long*, int>)Com.Slot(toast, 11))(toast, handler, &token), "add_Activated");

            statics = Factory("Windows.UI.Notifications.ToastNotificationManager", IID_IToastNotificationManagerStatics);
            using (var id = new HString(appId))
                Com.Check(((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)Com.Slot(statics, 7))(statics, id.Handle, &notifier), "CreateToastNotifierWithId");
            Com.Check(((delegate* unmanaged[Stdcall]<nint, nint, int>)Com.Slot(notifier, 6))(notifier, toast), "Show");

            lock (s_shown)
            {
                s_shown.Enqueue(toast);
                toast = 0;
                while (s_shown.Count > 64)
                    Com.Release(s_shown.Dequeue());
            }
        }
        finally
        {
            Com.Release(handler);
            Com.Release(notifier);
            Com.Release(statics);
            Com.Release(toast);
            Com.Release(factory);
            Com.Release(io);
            Com.Release(document);
        }
    }

    /// <summary>How many toasts of <paramref name="appId"/> the notification center holds: for tests.</summary>
    internal static int HistoryCount(string appId)
    {
        var IID_Statics2 = new Guid("7AB93C52-0E48-4750-BA9D-1A4113981847");
        var IID_History2 = new Guid("3BC3D253-2F31-4092-9129-8AD5ABF067DA");
        nint statics = 0, history = 0, history2 = 0, list = 0;
        try
        {
            statics = Factory("Windows.UI.Notifications.ToastNotificationManager", IID_Statics2);
            Com.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Com.Slot(statics, 6))(statics, &history), "get_History");
            history2 = Query(history, IID_History2, "IToastNotificationHistory2");
            using (var id = new HString(appId))
                Com.Check(((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)Com.Slot(history2, 7))(history2, id.Handle, &list), "GetHistoryWithId");
            uint size;
            Com.Check(((delegate* unmanaged[Stdcall]<nint, uint*, int>)Com.Slot(list, 7))(list, &size), "get_Size");
            return (int)size;
        }
        finally
        {
            Com.Release(list);
            Com.Release(history2);
            Com.Release(history);
            Com.Release(statics);
        }
    }

    /// <summary>The arguments of an IToastActivatedEventArgs, or "" for other args.</summary>
    private static string Arguments(nint args)
    {
        var activated = Com.QueryInterface(args, IID_IToastActivatedEventArgs);
        if (activated == 0)
            return "";
        try
        {
            nint text;
            Com.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Com.Slot(activated, 6))(activated, &text), "get_Arguments");
            return HString.Take(text);
        }
        finally
        {
            Com.Release(activated);
        }
    }

    /// <summary>
    /// Names the app for the toasts of <paramref name="appId"/>, as Windows
    /// asks of apps that are not packaged: per user, no administrator.
    /// </summary>
    private static void Register(string appId, string appName)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\AppUserModelId\{appId}");
            if (key.GetValue("DisplayName") as string != appName)
                key.SetValue("DisplayName", appName);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or SecurityException or IOException)
        {
            Tracing.Write($"could not register {appId} for notifications: {e.Message}");
        }
    }

    private static nint Activate(string runtimeClass)
    {
        using var name = new HString(runtimeClass);
        nint instance;
        Com.Check(RoActivateInstance(name.Handle, &instance), $"RoActivateInstance({runtimeClass})");
        return instance;
    }

    private static nint Factory(string runtimeClass, in Guid iid)
    {
        using var name = new HString(runtimeClass);
        nint factory;
        fixed (Guid* p = &iid)
            Com.Check(RoGetActivationFactory(name.Handle, p, &factory), $"RoGetActivationFactory({runtimeClass})");
        return factory;
    }

    private static nint Query(nint obj, in Guid iid, string name)
    {
        var result = Com.QueryInterface(obj, iid);
        return result != 0 ? result : throw new COMException($"{name} is missing", Com.E_NOINTERFACE);
    }

    /// <summary>A WinRT string, deleted with it.</summary>
    private readonly struct HString : IDisposable
    {
        public HString(string text)
        {
            nint handle;
            fixed (char* p = text)
                Com.Check(WindowsCreateString(p, (uint)text.Length, &handle), "WindowsCreateString");
            Handle = handle;
        }

        public nint Handle { get; }

        public void Dispose() => WindowsDeleteString(Handle);

        /// <summary>The text of a string WinRT gave, which it deletes.</summary>
        public static string Take(nint handle)
        {
            if (handle == 0)
                return "";
            uint length;
            var buffer = WindowsGetStringRawBuffer(handle, &length);
            var text = new string(buffer, 0, (int)length);
            WindowsDeleteString(handle);
            return text;
        }
    }

    [LibraryImport("combase.dll")]
    private static partial int RoActivateInstance(nint activatableClassId, nint* instance);

    [LibraryImport("combase.dll")]
    private static partial int RoGetActivationFactory(nint activatableClassId, Guid* iid, nint* factory);

    [LibraryImport("combase.dll")]
    private static partial int WindowsCreateString(char* source, uint length, nint* text);

    [LibraryImport("combase.dll")]
    private static partial int WindowsDeleteString(nint text);

    [LibraryImport("combase.dll")]
    private static partial char* WindowsGetStringRawBuffer(nint text, uint* length);
}
