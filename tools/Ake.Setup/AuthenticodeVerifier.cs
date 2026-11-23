using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace Ake.Setup;

/// <summary>Harici kurulum dosyalarının Windows Authenticode dijital imzasını ve yayıncısını doğrular.</summary>
public static class AuthenticodeVerifier
{
    private const uint WtdUiNone = 2;
    private const uint WtdRevokeNone = 0;
    private const uint WtdChoiceFile = 1;
    private const uint WtdStateActionVerify = 1;
    private const uint WtdStateActionClose = 2;
    private const uint WtdSaferFlag = 0x00000100;

    private static readonly Guid ActionGenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    public static (bool Valid, string Reason) VerifySignedBy(string filePath, string expectedPublisher)
    {
        if (!OperatingSystem.IsWindows())
        {
            return (false, "Yalnızca Windows üzerinde imza doğrulanabilir.");
        }

        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return (false, "Doğrulanacak dosya bulunamadı.");
        }

        var fullPath = Path.GetFullPath(filePath);
        var fileInfo = new WINTRUST_FILE_INFO
        {
            cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(),
            pcwszFilePath = Marshal.StringToCoTaskMemUni(fullPath),
            hFile = IntPtr.Zero,
            pgKnownSubject = IntPtr.Zero,
        };

        var pFile = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
        var data = new WINTRUST_DATA
        {
            cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
            dwUIChoice = WtdUiNone,
            fdwRevocationChecks = WtdRevokeNone,
            dwUnionChoice = WtdChoiceFile,
            pInfoStruct = pFile,
            dwStateAction = WtdStateActionVerify,
            dwProvFlags = WtdSaferFlag,
        };

        var action = ActionGenericVerifyV2;
        try
        {
            Marshal.StructureToPtr(fileInfo, pFile, false);
            var status = WinVerifyTrust(new IntPtr(-1), ref action, ref data);
            if (status != 0)
            {
                return (false, $"Geçersiz veya güvenilmeyen Authenticode imzası (0x{status:X8}).");
            }

            if (string.IsNullOrWhiteSpace(expectedPublisher))
            {
                return (true, "Doğrulandı");
            }

#pragma warning disable SYSLIB0057 // Authenticode gömülü imzalı yürütülebilir sertifikasını okuma
            using var cert = X509Certificate.CreateFromSignedFile(fullPath);
#pragma warning restore SYSLIB0057
            return cert.Subject.Contains(expectedPublisher, StringComparison.OrdinalIgnoreCase)
                ? (true, cert.Subject)
                : (false, $"Beklenen yayıncı '{expectedPublisher}' yerine '{cert.Subject}' bulundu.");
        }
        catch (Exception ex)
        {
            return (false, $"İmza doğrulama hatası: {ex.Message}");
        }
        finally
        {
            if (data.hWVTStateData != IntPtr.Zero)
            {
                data.dwStateAction = WtdStateActionClose;
                _ = WinVerifyTrust(new IntPtr(-1), ref action, ref data);
            }

            Marshal.FreeHGlobal(pFile);
            if (fileInfo.pcwszFilePath != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(fileInfo.pcwszFilePath);
            }
        }
    }

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid pgActionId, ref WINTRUST_DATA pWvtData);

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_FILE_INFO
    {
        public uint cbStruct;
        public IntPtr pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_DATA
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pInfoStruct;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }
}
