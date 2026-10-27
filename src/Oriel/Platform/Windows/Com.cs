using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Oriel.Platform.Windows;

/// <summary>
/// Calls COM objects through their vtables, the way C does. WebView2 is
/// used through a handful of methods; declaring them by slot keeps the
/// backend small and free of runtime-generated marshalling, so it works
/// under NativeAOT. Slot numbers come from WebView2.h (see docs/architecture.md).
/// </summary>
internal static unsafe class Com
{
    public const int S_OK = 0;
    public const int E_NOINTERFACE = unchecked((int)0x80004002);
    public const int E_POINTER = unchecked((int)0x80004003);

    public static readonly Guid IID_IUnknown = new("00000000-0000-0000-C000-000000000046");

    /// <summary>The function in slot <paramref name="slot"/> of the vtable of <paramref name="obj"/>.</summary>
    public static void* Slot(nint obj, int slot) => (*(void***)obj)[slot];

    public static uint AddRef(nint obj) => ((delegate* unmanaged[Stdcall]<nint, uint>)Slot(obj, 1))(obj);

    public static uint Release(nint obj) => obj == 0 ? 0 : ((delegate* unmanaged[Stdcall]<nint, uint>)Slot(obj, 2))(obj);

    /// <summary>Asks <paramref name="obj"/> for the interface <paramref name="iid"/>; 0 when it has none.</summary>
    public static nint QueryInterface(nint obj, in Guid iid)
    {
        nint result = 0;
        fixed (Guid* p = &iid)
            ((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Slot(obj, 0))(obj, p, &result);
        return result;
    }

    /// <summary>Throws for a failed HRESULT, naming the call.</summary>
    public static void Check(int hr, string call)
    {
        if (hr < 0)
            throw new COMException($"{call} failed with 0x{hr:X8}", hr);
    }

    /// <summary>Takes a string COM allocated, freeing it.</summary>
    public static string? TakeString(nint p)
    {
        if (p == 0)
            return null;
        try
        {
            return new string((char*)p);
        }
        finally
        {
            Win32.CoTaskMemFree(p);
        }
    }
}

/// <summary>
/// A COM object made in C# for WebView2 to call back: the completed handlers
/// of its asynchronous calls and the handlers of its events. All of them are
/// an IUnknown with one Invoke method of two arguments.
/// </summary>
/// <remarks>
/// The object lives in native memory: its vtable, its reference count, the
/// interface it implements and a handle to the C# delegate it calls. When
/// WebView2 releases it for the last time, the memory and the handle are freed.
/// </remarks>
internal static unsafe class ComCallback
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Object
    {
        public void** Vtable;
        public int References;
        public Guid Iid;
        public nint Handle;
        public int Agile;
    }

    private static readonly Guid IID_IAgileObject = new("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90");

    // One vtable per signature of Invoke, shared by every object of it.
    private static readonly void** s_resultVtable = CreateVtable(
        (delegate* unmanaged[Stdcall]<Object*, int, nint, int>)&InvokeResult);

    private static readonly void** s_eventVtable = CreateVtable(
        (delegate* unmanaged[Stdcall]<Object*, nint, nint, int>)&InvokeEvent);

    private static int s_live;

    /// <summary>Raised when a callback throws: COM must not see exceptions.</summary>
    public static event Action<Exception>? Failed;

    /// <summary>
    /// How many callbacks exist: made, and not yet released for the last
    /// time. The stress tests check it comes back down as windows close.
    /// </summary>
    public static int Live => Volatile.Read(ref s_live);

    /// <summary>
    /// A completed handler, <c>Invoke(HRESULT, pointer)</c>: the pointer is the
    /// created object or a string, owned by WebView2 for the call only.
    /// </summary>
    public static nint ForResult(in Guid iid, Action<int, nint> invoke) => Create(s_resultVtable, iid, invoke);

    /// <summary>An event handler, <c>Invoke(sender, args)</c>, both borrowed for the call only.</summary>
    public static nint ForEvent(in Guid iid, Action<nint, nint> invoke) => Create(s_eventVtable, iid, invoke);

    /// <summary>
    /// An event handler that says it is agile (IAgileObject): WinRT then
    /// calls it on any thread, rather than marshal it, which a handler made
    /// here cannot be. <paramref name="invoke"/> must be safe on any thread.
    /// </summary>
    public static nint ForAgileEvent(in Guid iid, Action<nint, nint> invoke)
    {
        var obj = (Object*)Create(s_eventVtable, iid, invoke);
        obj->Agile = 1;
        return (nint)obj;
    }

    private static nint Create(void** vtable, in Guid iid, Delegate invoke)
    {
        var obj = (Object*)NativeMemory.Alloc((nuint)sizeof(Object));
        obj->Vtable = vtable;
        obj->References = 1;
        obj->Iid = iid;
        obj->Handle = GCHandle.ToIntPtr(GCHandle.Alloc(invoke));
        obj->Agile = 0;
        Interlocked.Increment(ref s_live);
        return (nint)obj;
    }

    private static void** CreateVtable(void* invoke)
    {
        var vtable = (void**)NativeMemory.Alloc(4, (nuint)sizeof(void*));
        vtable[0] = (delegate* unmanaged[Stdcall]<Object*, Guid*, void**, int>)&QueryInterface;
        vtable[1] = (delegate* unmanaged[Stdcall]<Object*, uint>)&AddRef;
        vtable[2] = (delegate* unmanaged[Stdcall]<Object*, uint>)&Release;
        vtable[3] = invoke;
        return vtable;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int QueryInterface(Object* self, Guid* iid, void** result)
    {
        if (result is null)
            return Com.E_POINTER;
        if (*iid == Com.IID_IUnknown || *iid == self->Iid || (self->Agile != 0 && *iid == IID_IAgileObject))
        {
            Interlocked.Increment(ref self->References);
            *result = self;
            return Com.S_OK;
        }
        *result = null;
        return Com.E_NOINTERFACE;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint AddRef(Object* self) => (uint)Interlocked.Increment(ref self->References);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint Release(Object* self)
    {
        var left = Interlocked.Decrement(ref self->References);
        if (left == 0)
        {
            GCHandle.FromIntPtr(self->Handle).Free();
            NativeMemory.Free(self);
            Interlocked.Decrement(ref s_live);
        }
        return (uint)left;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int InvokeResult(Object* self, int hr, nint value)
    {
        try
        {
            ((Action<int, nint>)GCHandle.FromIntPtr(self->Handle).Target!)(hr, value);
        }
        catch (Exception e)
        {
            Failed?.Invoke(e);
        }
        return Com.S_OK;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int InvokeEvent(Object* self, nint sender, nint args)
    {
        try
        {
            ((Action<nint, nint>)GCHandle.FromIntPtr(self->Handle).Target!)(sender, args);
        }
        catch (Exception e)
        {
            Failed?.Invoke(e);
        }
        return Com.S_OK;
    }
}
