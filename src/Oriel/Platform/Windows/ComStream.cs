using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Oriel.Platform.Windows;

/// <summary>
/// A COM <c>IStream</c> that reads a .NET <see cref="Stream"/>, for the
/// responses WebView2 reads as the page takes them. Only reading is
/// supported: <c>Read</c>, <c>Stat</c>, and <c>Seek</c> to where it is. The
/// stream is disposed with the last reference.
/// </summary>
internal static unsafe class ComStream
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Object
    {
        public void** Vtable;
        public int References;
        public nint Handle; // the Reader
        public long Position;
        public long Length; // -1 when unknown
    }

    private static readonly Guid IID_ISequentialStream = new("0c733a30-2a1c-11ce-ade5-00aa0044773d");
    private static readonly Guid IID_IStream = new("0000000c-0000-0000-C000-000000000046");

    private const int S_FALSE = 1;
    private const int E_NOTIMPL = unchecked((int)0x80004001);
    private const int E_FAIL = unchecked((int)0x80004005);
    private const int STG_E_INVALIDFUNCTION = unchecked((int)0x80030001);
    private const int STREAM_SEEK_SET = 0;
    private const int STREAM_SEEK_CUR = 1;
    private const int STGTY_STREAM = 2;

    private static readonly void** s_vtable = CreateVtable();

    private static int s_live;

    /// <summary>How many streams exist, not yet released for the last time.</summary>
    public static int Live => Volatile.Read(ref s_live);

    /// <param name="stream">Read from where it is; disposed with the last reference.</param>
    /// <param name="length">How many bytes it has, or -1 when it does not say.</param>
    public static nint Create(Stream stream, long length)
    {
        var obj = (Object*)NativeMemory.Alloc((nuint)sizeof(Object));
        obj->Vtable = s_vtable;
        obj->References = 1;
        obj->Handle = GCHandle.ToIntPtr(GCHandle.Alloc(stream));
        obj->Position = 0;
        obj->Length = length;
        Interlocked.Increment(ref s_live);
        return (nint)obj;
    }

    private static void** CreateVtable()
    {
        var vtable = (void**)NativeMemory.Alloc(14, (nuint)sizeof(void*));
        vtable[0] = (delegate* unmanaged[Stdcall]<Object*, Guid*, void**, int>)&QueryInterface;
        vtable[1] = (delegate* unmanaged[Stdcall]<Object*, uint>)&AddRef;
        vtable[2] = (delegate* unmanaged[Stdcall]<Object*, uint>)&Release;
        vtable[3] = (delegate* unmanaged[Stdcall]<Object*, byte*, uint, uint*, int>)&Read;
        vtable[4] = (delegate* unmanaged[Stdcall]<Object*, byte*, uint, uint*, int>)&Write;
        vtable[5] = (delegate* unmanaged[Stdcall]<Object*, long, int, ulong*, int>)&Seek;
        vtable[6] = (delegate* unmanaged[Stdcall]<Object*, ulong, int>)&SetSize;
        vtable[7] = (delegate* unmanaged[Stdcall]<Object*, nint, ulong, ulong*, ulong*, int>)&CopyTo;
        vtable[8] = (delegate* unmanaged[Stdcall]<Object*, int, int>)&Commit;
        vtable[9] = (delegate* unmanaged[Stdcall]<Object*, int>)&Revert;
        vtable[10] = (delegate* unmanaged[Stdcall]<Object*, ulong, ulong, int, int>)&LockRegion;
        vtable[11] = (delegate* unmanaged[Stdcall]<Object*, ulong, ulong, int, int>)&LockRegion; // UnlockRegion
        vtable[12] = (delegate* unmanaged[Stdcall]<Object*, byte*, int, int>)&Stat;
        vtable[13] = (delegate* unmanaged[Stdcall]<Object*, nint*, int>)&Clone;
        return vtable;
    }

    private static Stream Of(Object* self) => (Stream)GCHandle.FromIntPtr(self->Handle).Target!;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int QueryInterface(Object* self, Guid* iid, void** result)
    {
        if (result is null)
            return Com.E_POINTER;
        if (*iid == Com.IID_IUnknown || *iid == IID_ISequentialStream || *iid == IID_IStream)
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
            var handle = GCHandle.FromIntPtr(self->Handle);
            try
            {
                ((Stream)handle.Target!).Dispose();
            }
            catch (Exception e)
            {
                Tracing.Write($"a shared stream failed to close: {e.Message}");
            }
            handle.Free();
            NativeMemory.Free(self);
            Interlocked.Decrement(ref s_live);
        }
        return (uint)left;
    }

    /// <summary>Fills the buffer, unless the stream ends first: then S_FALSE, as IStream says.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int Read(Object* self, byte* buffer, uint count, uint* read)
    {
        var total = 0;
        try
        {
            var stream = Of(self);
            lock (stream)
            {
                var span = new Span<byte>(buffer, (int)Math.Min(count, int.MaxValue));
                while (total < span.Length)
                {
                    var n = stream.Read(span[total..]);
                    if (n == 0)
                        break;
                    total += n;
                }
                self->Position += total;
            }
        }
        catch (Exception e)
        {
            Tracing.Write($"a shared stream failed to read: {e.Message}");
            if (read is not null)
                *read = (uint)total;
            return E_FAIL;
        }
        if (read is not null)
            *read = (uint)total;
        return total == count ? Com.S_OK : S_FALSE;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int Write(Object* self, byte* buffer, uint count, uint* written) => STG_E_INVALIDFUNCTION;

    /// <summary>Tells where the stream is; moves nowhere else, as it reads forward only.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int Seek(Object* self, long move, int origin, ulong* position)
    {
        var target = origin switch
        {
            STREAM_SEEK_SET => move,
            STREAM_SEEK_CUR => self->Position + move,
            _ => -1,
        };
        if (target != self->Position)
            return STG_E_INVALIDFUNCTION;
        if (position is not null)
            *position = (ulong)target;
        return Com.S_OK;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int SetSize(Object* self, ulong size) => STG_E_INVALIDFUNCTION;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int CopyTo(Object* self, nint target, ulong count, ulong* read, ulong* written) => E_NOTIMPL;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int Commit(Object* self, int flags) => Com.S_OK;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int Revert(Object* self) => STG_E_INVALIDFUNCTION;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int LockRegion(Object* self, ulong offset, ulong count, int type) => STG_E_INVALIDFUNCTION;

    /// <summary>
    /// STATSTG, 80 bytes on 64-bit Windows: no name, the type at 8, the size
    /// at 16 (0 when unknown); the rest is zero.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int Stat(Object* self, byte* stat, int flags)
    {
        if (stat is null)
            return Com.E_POINTER;
        new Span<byte>(stat, 8 + 8 + 8 + 24 + 4 + 4 + 16 + 4 + 4).Clear();
        *(int*)(stat + 8) = STGTY_STREAM;
        *(ulong*)(stat + 16) = self->Length < 0 ? 0 : (ulong)self->Length;
        return Com.S_OK;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int Clone(Object* self, nint* clone)
    {
        if (clone is not null)
            *clone = 0;
        return E_NOTIMPL;
    }
}
