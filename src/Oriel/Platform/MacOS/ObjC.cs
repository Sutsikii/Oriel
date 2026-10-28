using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Oriel.Platform.MacOS;

/// <summary>
/// The Objective-C runtime, which the macOS backend reaches AppKit and
/// WebKit through: <c>objc_msgSend</c> declared once per signature it is
/// called with, classes made at run time for the delegates, and the few C
/// functions of libSystem it needs. The frameworks are the system's.
/// </summary>
/// <remarks>
/// Messages are sent with <see cref="Send(nint, nint)"/> and its overloads,
/// whose return value is ignored for methods that return nothing. Strings
/// made with <see cref="Str"/> are autoreleased: every call into the
/// backend runs in an autorelease pool (<see cref="Pool"/>).
/// </remarks>
[SupportedOSPlatform("macos")]
internal static unsafe partial class ObjC
{
    private const string Lib = "/usr/lib/libobjc.A.dylib";
    private const string LibSystem = "/usr/lib/libSystem.B.dylib";

    /// <summary>Loads AppKit and WebKit, whose classes are found by name afterwards.</summary>
    public static void LoadFrameworks()
    {
        NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit");
        NativeLibrary.Load("/System/Library/Frameworks/WebKit.framework/WebKit");
    }

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint objc_getClass(string name);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint sel_registerName(string name);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint objc_allocateClassPair(nint superclass, string name, nint extraBytes);

    [LibraryImport(Lib)]
    private static partial void objc_registerClassPair(nint cls);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    private static partial byte class_addMethod(nint cls, nint sel, nint imp, string types);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint objc_getProtocol(string name);

    [LibraryImport(Lib)]
    private static partial byte class_addProtocol(nint cls, nint protocol);

    [LibraryImport(Lib)]
    private static partial nint objc_autoreleasePoolPush();

    [LibraryImport(Lib)]
    private static partial void objc_autoreleasePoolPop(nint pool);

    /// <summary>A class, by name; 0 when there is none.</summary>
    public static nint Class(string name) => objc_getClass(name);

    public static nint Sel(string name) => sel_registerName(name);

    // ---- objc_msgSend, one declaration per signature ----

    [LibraryImport(Lib, EntryPoint = "objc_msgSend")]
    public static partial nint Send(nint self, nint sel);

    [LibraryImport(Lib, EntryPoint = "objc_msgSend")]
    public static partial nint Send(nint self, nint sel, nint a);

    [LibraryImport(Lib, EntryPoint = "objc_msgSend")]
    public static partial nint Send(nint self, nint sel, nint a, nint b);

    [LibraryImport(Lib, EntryPoint = "objc_msgSend")]
    public static partial nint Send(nint self, nint sel, nint a, nint b, nint c);

    [LibraryImport(Lib, EntryPoint = "objc_msgSend")]
    public static partial nint Send(nint self, nint sel, nint a, nint b, nint c, nint d);

    [LibraryImport(Lib, EntryPoint = "objc_msgSend")]
    public static partial nint Send(nint self, nint sel, byte a);

    [LibraryImport(Lib, EntryPoint = "objc_msgSend")]
    public static partial nint Send(nint self, nint sel, nint a, byte b);

    [LibraryImport(Lib, EntryPoint = "objc_msgSend")]
    public static partial nint Send(nint self, nint sel, nint a, nint b, byte c);

    [LibraryImport(Lib, EntryPoint = "objc_msgSend")]
    public static partial nint Send(nint self, nint sel, NSRect a, nint b);

    [LibraryImport(Lib, EntryPoint = "objc_msgSend")]
    public static partial nint Send(nint self, nint sel, NSRect a, byte b);

    [LibraryImport(Lib, EntryPoint = "objc_msgSend")]
    public static partial nint Send(nint self, nint sel, NSRect a, nint b, nint c, byte d);

    [LibraryImport(Lib, EntryPoint = "objc_msgSend")]
    public static partial nint Send(nint self, nint sel, NSSize a);

    [LibraryImport(Lib, EntryPoint = "objc_msgSend")]
    public static partial nint Send(nint self, nint sel, double a);

    [LibraryImport(Lib, EntryPoint = "objc_msgSend")]
    public static partial nint Send(nint self, nint sel, NSRect a);

    /// <summary>-[NSMenu popUpMenuPositioningItem:atLocation:inView:].</summary>
    [LibraryImport(Lib, EntryPoint = "objc_msgSend")]
    public static partial byte Send(nint self, nint sel, nint item, NSPoint location, nint view);

    /// <summary>A method that returns a point: two doubles, in registers on both arm64 and x64.</summary>
    [LibraryImport(Lib, EntryPoint = "objc_msgSend")]
    public static partial NSPoint SendPoint(nint self, nint sel);

    [LibraryImport(Lib, EntryPoint = "objc_msgSend")]
    public static partial NSPoint SendPoint(nint self, nint sel, NSPoint point, nint view);

    /// <summary>Calls the superclass's implementation of a method taking one object and returning a BOOL.</summary>
    public static byte SendSuperBool(nint self, nint superclass, nint sel, nint a)
    {
        var super = new ObjCSuper { Receiver = self, SuperClass = superclass };
        return objc_msgSendSuper(&super, sel, a);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ObjCSuper
    {
        public nint Receiver;
        public nint SuperClass;
    }

    [LibraryImport(Lib)]
    private static partial byte objc_msgSendSuper(ObjCSuper* super, nint sel, nint a);

    /// <summary>-[NSObject performSelectorOnMainThread:withObject:waitUntilDone:modes:].</summary>
    [LibraryImport(Lib, EntryPoint = "objc_msgSend")]
    public static partial nint Send(nint self, nint sel, nint selector, nint argument, byte wait, nint modes);

    /// <summary>+[NSEvent otherEventWithType:location:modifierFlags:timestamp:windowNumber:context:subtype:data1:data2:].</summary>
    [LibraryImport(Lib, EntryPoint = "objc_msgSend")]
    public static partial nint Send(nint self, nint sel, nint type, NSPoint location, nint flags, double time, nint window, nint context, short subtype, nint data1, nint data2);

    [LibraryImport(Lib, EntryPoint = "objc_msgSend")]
    private static partial byte SendByte(nint self, nint sel);

    [LibraryImport(Lib, EntryPoint = "objc_msgSend")]
    private static partial byte SendByte(nint self, nint sel, nint a);

    public static bool SendBool(nint self, nint sel) => SendByte(self, sel) != 0;

    public static bool SendBool(nint self, nint sel, nint a) => SendByte(self, sel, a) != 0;

    // A struct of 32 bytes comes back in memory on x64, through objc_msgSend_stret;
    // in registers on arm64, which has no _stret.
    [LibraryImport(Lib, EntryPoint = "objc_msgSend")]
    private static partial NSRect SendRectArm64(nint self, nint sel);

    [LibraryImport(Lib, EntryPoint = "objc_msgSend_stret")]
    private static partial NSRect SendRectX64(nint self, nint sel);

    public static NSRect SendRect(nint self, nint sel) =>
        RuntimeInformation.ProcessArchitecture == Architecture.X64 ? SendRectX64(self, sel) : SendRectArm64(self, sel);

    // ---- Objects ----

    /// <summary>A new object of <paramref name="cls"/>, from alloc and init: the caller owns it.</summary>
    public static nint New(string cls) => Send(Send(Class(cls), Sel("alloc")), Sel("init"));

    public static void Release(nint obj)
    {
        if (obj != 0)
            Send(obj, Sel("release"));
    }

    public static bool RespondsTo(nint obj, string selector) => SendBool(obj, Sel("respondsToSelector:"), Sel(selector));

    /// <summary>An autoreleased NSString of <paramref name="text"/>.</summary>
    public static nint Str(string text)
    {
        nint s;
        fixed (char* p = text)
            s = Send(Send(Class("NSString"), Sel("alloc")), Sel("initWithCharacters:length:"), (nint)p, text.Length);
        return Send(s, Sel("autorelease"));
    }

    /// <summary>The text of an NSString, or null for nil.</summary>
    public static string? Text(nint str) =>
        str == 0 ? null : Marshal.PtrToStringUTF8(Send(str, Sel("UTF8String")));

    public static bool IsString(nint obj) => obj != 0 && SendBool(obj, Sel("isKindOfClass:"), Class("NSString"));

    /// <summary>The absolute string of an NSURL, or null for nil.</summary>
    public static string? UrlText(nint url) => url == 0 ? null : Text(Send(url, Sel("absoluteString")));

    /// <summary>An autoreleased NSURL of <paramref name="url"/>.</summary>
    public static nint Url(string url) => Send(Class("NSURL"), Sel("URLWithString:"), Str(url));

    /// <summary>Calls a block that takes one integer or object, such as a decision handler WebKit passes.</summary>
    public static void CallBlock(nint block, nint argument)
    {
        // A block starts with its isa, its flags and a reserved int, then its function.
        var invoke = *(delegate* unmanaged<nint, nint, void>*)(block + 16);
        invoke(block, argument);
    }

    /// <summary>
    /// Makes a subclass of NSObject named <paramref name="name"/>, which
    /// adopts <paramref name="protocols"/> and has <paramref name="methods"/>;
    /// or finds it, made already by this process.
    /// </summary>
    public static nint DefineClass(string name, string[] protocols, params (string Selector, nint Function, string Types)[] methods) =>
        DefineSubclass("NSObject", name, protocols, methods);

    /// <summary>As <see cref="DefineClass"/>, of a subclass of <paramref name="superclass"/>.</summary>
    public static nint DefineSubclass(string superclass, string name, string[] protocols, params (string Selector, nint Function, string Types)[] methods)
    {
        var cls = Class(name);
        if (cls != 0)
            return cls;
        cls = objc_allocateClassPair(Class(superclass), name, 0);
        foreach (var protocol in protocols)
        {
            var p = objc_getProtocol(protocol);
            if (p != 0)
                class_addProtocol(cls, p);
        }
        foreach (var (selector, function, types) in methods)
            class_addMethod(cls, Sel(selector), function, types);
        objc_registerClassPair(cls);
        return cls;
    }

    /// <summary>An autorelease pool, drained when disposed.</summary>
    public static Pool AutoreleasePool() => new(objc_autoreleasePoolPush());

    public readonly struct Pool(nint handle) : IDisposable
    {
        public void Dispose() => objc_autoreleasePoolPop(handle);
    }

    // ---- libSystem ----

    [LibraryImport(LibSystem)]
    public static partial int pthread_main_np();

    [LibraryImport(LibSystem)]
    public static partial void dispatch_async_f(nint queue, nint context, delegate* unmanaged<nint, void> work);

    /// <summary>The main dispatch queue, which the main thread's run loop drains: <c>dispatch_get_main_queue()</c> is this symbol's address.</summary>
    public static readonly nint MainQueue = NativeLibrary.GetExport(NativeLibrary.Load(LibSystem), "_dispatch_main_q");
}

[StructLayout(LayoutKind.Sequential)]
internal record struct NSPoint(double X, double Y);

[StructLayout(LayoutKind.Sequential)]
internal record struct NSSize(double Width, double Height);

[StructLayout(LayoutKind.Sequential)]
internal record struct NSRect(double X, double Y, double Width, double Height);
