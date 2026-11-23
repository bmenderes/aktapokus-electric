using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;
using System.Text;

namespace Ake.Setup;

/// <summary>Windows Kabuk (.lnk) kısayolunu harici PowerShell veya betik çalıştırmadan doğrudan COM üzerinden oluşturur.</summary>
internal static class ShortcutCreator
{
    public static void Create(string shortcutPath, string targetExe, string workingDir, string description)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var fullShortcut = Path.GetFullPath(shortcutPath);
        var fullTarget = Path.GetFullPath(targetExe);
        var fullWorkingDir = Path.GetFullPath(workingDir);
        Directory.CreateDirectory(Path.GetDirectoryName(fullShortcut)!);

        Exception? error = null;
        void Save()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            try
            {
                CreateOnStaThread(fullShortcut, fullTarget, fullWorkingDir, description);
            }
            catch (Exception ex)
            {
                error = ex;
            }
        }

        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            Save();
        }
        else
        {
            var thread = new Thread(Save) { IsBackground = true };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
        }

        if (error is not null)
        {
            throw new IOException($"Kısayol oluşturulamadı: {fullShortcut}", error);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void CreateOnStaThread(string shortcutPath, string targetExe, string workingDir, string description)
    {
        var link = (IShellLinkW)new ShellLink();
        try
        {
            link.SetPath(targetExe);
            link.SetWorkingDirectory(workingDir);
            link.SetIconLocation(targetExe, 0);
            link.SetDescription(description);
            ((IPersistFile)link).Save(shortcutPath, false);
        }
        finally
        {
            if (Marshal.IsComObject(link))
            {
                Marshal.FinalReleaseComObject(link);
            }
        }
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink
    {
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out ushort pwHotkey);
        void SetHotkey(ushort wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }
}
