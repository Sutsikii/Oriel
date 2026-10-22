using System.Runtime.InteropServices;

namespace Oriel.Platform.Windows;

/// <summary>
/// The system's file dialogs, IFileOpenDialog and IFileSaveDialog, called
/// through COM by slot as <see cref="WebView2"/> does, and message boxes.
/// Slots come from ShObjIdl_core.h: IUnknown's three methods first, then
/// IModalWindow's Show, then IFileDialog's, then IFileOpenDialog's.
/// </summary>
internal static unsafe class ShellDialogs
{
    private static readonly Guid CLSID_FileOpenDialog = new("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7");
    private static readonly Guid CLSID_FileSaveDialog = new("C0B4E2F3-BA21-4773-8DBA-335EC946EB8B");
    private static readonly Guid IID_IFileOpenDialog = new("D57C7288-D4AD-4768-BE02-9D969532D960");
    private static readonly Guid IID_IFileSaveDialog = new("84BCCD23-5FDE-4CDB-AEA4-AF64B83D78AB");
    private static readonly Guid IID_IShellItem = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");

    private const uint CLSCTX_INPROC_SERVER = 0x1;

    private const uint FOS_OVERWRITEPROMPT = 0x2;
    private const uint FOS_PICKFOLDERS = 0x20;
    private const uint FOS_FORCEFILESYSTEM = 0x40;
    private const uint FOS_ALLOWMULTISELECT = 0x200;
    private const uint FOS_PATHMUSTEXIST = 0x800;
    private const uint FOS_FILEMUSTEXIST = 0x1000;

    private const uint SIGDN_FILESYSPATH = 0x80058000;

    /// <summary>HRESULT_FROM_WIN32(ERROR_CANCELLED): the dialog was canceled.</summary>
    private const int E_CANCELLED = unchecked((int)0x800704C7);

    [StructLayout(LayoutKind.Sequential)]
    private struct COMDLG_FILTERSPEC
    {
        public nint pszName;
        public nint pszSpec;
    }

    /// <summary>Shows the dialog of <paramref name="request"/>, owned by <paramref name="owner"/>; null when canceled.</summary>
    public static IReadOnlyList<string>? Show(nint owner, FileDialogRequest request)
    {
        var save = request.Kind == FileDialogKind.Save;
        var dialog = Create(save ? CLSID_FileSaveDialog : CLSID_FileOpenDialog, save ? IID_IFileSaveDialog : IID_IFileOpenDialog);
        var strings = new List<nint>();
        try
        {
            // IFileDialog::GetOptions (10), then SetOptions (9).
            uint options;
            Com.Check(((delegate* unmanaged[Stdcall]<nint, uint*, int>)Com.Slot(dialog, 10))(dialog, &options), "IFileDialog::GetOptions");
            options |= FOS_FORCEFILESYSTEM | request.Kind switch
            {
                FileDialogKind.Open => FOS_FILEMUSTEXIST | FOS_PATHMUSTEXIST,
                FileDialogKind.OpenMany => FOS_FILEMUSTEXIST | FOS_PATHMUSTEXIST | FOS_ALLOWMULTISELECT,
                FileDialogKind.Save => FOS_OVERWRITEPROMPT | FOS_PATHMUSTEXIST,
                _ => FOS_PICKFOLDERS | FOS_PATHMUSTEXIST,
            };
            Com.Check(((delegate* unmanaged[Stdcall]<nint, uint, int>)Com.Slot(dialog, 9))(dialog, options), "IFileDialog::SetOptions");

            if (request.Filters.Count > 0)
            {
                var specs = stackalloc COMDLG_FILTERSPEC[request.Filters.Count];
                for (var i = 0; i < request.Filters.Count; i++)
                {
                    specs[i].pszName = Keep(strings, request.Filters[i].Name);
                    specs[i].pszSpec = Keep(strings, request.Filters[i].Spec);
                }
                // IFileDialog::SetFileTypes (4); the first type is selected.
                Com.Check(((delegate* unmanaged[Stdcall]<nint, uint, COMDLG_FILTERSPEC*, int>)Com.Slot(dialog, 4))(dialog, (uint)request.Filters.Count, specs),
                    "IFileDialog::SetFileTypes");
            }
            if (request.Title is not null)
                SetString(dialog, 17, "IFileDialog::SetTitle", request.Title);
            if (request.FileName is not null)
                SetString(dialog, 15, "IFileDialog::SetFileName", request.FileName);
            if (request.DefaultExtension is not null)
                SetString(dialog, 22, "IFileDialog::SetDefaultExtension", request.DefaultExtension);
            if (request.Directory is not null && ItemOf(request.Directory) is var folder and not 0)
            {
                try
                {
                    // IFileDialog::SetFolder (12): the folder asked for, over the last one used.
                    ((delegate* unmanaged[Stdcall]<nint, nint, int>)Com.Slot(dialog, 12))(dialog, folder);
                }
                finally
                {
                    Com.Release(folder);
                }
            }

            // IModalWindow::Show (3) runs a modal loop until the dialog closes.
            var hr = ((delegate* unmanaged[Stdcall]<nint, nint, int>)Com.Slot(dialog, 3))(dialog, owner);
            if (hr == E_CANCELLED)
                return null;
            Com.Check(hr, "IModalWindow::Show");
            return request.Kind == FileDialogKind.OpenMany ? Results(dialog) : [Result(dialog)];
        }
        finally
        {
            foreach (var s in strings)
                Marshal.FreeCoTaskMem(s);
            Com.Release(dialog);
        }
    }

    /// <summary>Shows a message box owned by <paramref name="owner"/>, and returns the button pressed.</summary>
    public static MessageResult Message(nint owner, MessageRequest request)
    {
        var type = request.Buttons switch
        {
            MessageButtons.OkCancel => Win32.MB_OKCANCEL,
            MessageButtons.YesNo => Win32.MB_YESNO,
            MessageButtons.YesNoCancel => Win32.MB_YESNOCANCEL,
            _ => Win32.MB_OK,
        } | request.Kind switch
        {
            MessageKind.Warning => Win32.MB_ICONWARNING,
            MessageKind.Error => Win32.MB_ICONERROR,
            MessageKind.Question => Win32.MB_ICONQUESTION,
            _ => Win32.MB_ICONINFORMATION,
        };
        return Win32.MessageBoxW(owner, request.Text, request.Title, type) switch
        {
            Win32.IDOK => MessageResult.Ok,
            Win32.IDYES => MessageResult.Yes,
            Win32.IDNO => MessageResult.No,
            0 => throw new COMException("MessageBoxW failed", Marshal.GetHRForLastWin32Error()),
            _ => MessageResult.Cancel,
        };
    }

    private static nint Create(in Guid clsid, in Guid iid)
    {
        nint dialog;
        fixed (Guid* c = &clsid)
        fixed (Guid* i = &iid)
            Com.Check(Win32.CoCreateInstance(c, 0, CLSCTX_INPROC_SERVER, i, &dialog), "CoCreateInstance of a file dialog");
        return dialog;
    }

    private static nint Keep(List<nint> strings, string value)
    {
        var p = Marshal.StringToCoTaskMemUni(value);
        strings.Add(p);
        return p;
    }

    private static void SetString(nint dialog, int slot, string call, string value)
    {
        fixed (char* p = value)
            Com.Check(((delegate* unmanaged[Stdcall]<nint, char*, int>)Com.Slot(dialog, slot))(dialog, p), call);
    }

    /// <summary>The shell item of the folder <paramref name="path"/>, or 0 when there is none.</summary>
    private static nint ItemOf(string path)
    {
        nint item;
        fixed (char* p = path)
        fixed (Guid* iid = &IID_IShellItem)
            return Win32.SHCreateItemFromParsingName(p, 0, iid, &item) < 0 ? 0 : item;
    }

    /// <summary>The chosen item's path: IFileDialog::GetResult (20).</summary>
    private static string Result(nint dialog)
    {
        nint item;
        Com.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Com.Slot(dialog, 20))(dialog, &item), "IFileDialog::GetResult");
        try
        {
            return PathOf(item);
        }
        finally
        {
            Com.Release(item);
        }
    }

    /// <summary>The chosen items' paths: IFileOpenDialog::GetResults (27), an IShellItemArray.</summary>
    private static List<string> Results(nint dialog)
    {
        nint array;
        Com.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Com.Slot(dialog, 27))(dialog, &array), "IFileOpenDialog::GetResults");
        try
        {
            // IShellItemArray::GetCount (7) and GetItemAt (8).
            uint count;
            Com.Check(((delegate* unmanaged[Stdcall]<nint, uint*, int>)Com.Slot(array, 7))(array, &count), "IShellItemArray::GetCount");
            var paths = new List<string>((int)count);
            for (uint i = 0; i < count; i++)
            {
                nint item;
                Com.Check(((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)Com.Slot(array, 8))(array, i, &item), "IShellItemArray::GetItemAt");
                try
                {
                    paths.Add(PathOf(item));
                }
                finally
                {
                    Com.Release(item);
                }
            }
            return paths;
        }
        finally
        {
            Com.Release(array);
        }
    }

    /// <summary>IShellItem::GetDisplayName (5), as a file system path.</summary>
    private static string PathOf(nint item)
    {
        nint name;
        Com.Check(((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)Com.Slot(item, 5))(item, SIGDN_FILESYSPATH, &name), "IShellItem::GetDisplayName");
        return Com.TakeString(name)!;
    }
}
