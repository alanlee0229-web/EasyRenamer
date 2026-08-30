using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using BatchRenamer.Core;

namespace BatchRenamer.FileSystem;

/// <summary>Read-only Windows file identity query used as a TOCTOU guard.</summary>
public sealed class WindowsFileIdentityProvider : IFileIdentityProvider
{
    public FileIdentity? TryGetIdentity(string path, bool isDirectory)
    {
        try
        {
            using var handle = NativeMethods.CreateFileW(
                path,
                0,
                NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE | NativeMethods.FILE_SHARE_DELETE,
                IntPtr.Zero,
                NativeMethods.OPEN_EXISTING,
                isDirectory ? NativeMethods.FILE_FLAG_BACKUP_SEMANTICS : 0,
                IntPtr.Zero);

            if (handle.IsInvalid) return null;
            if (!NativeMethods.GetFileInformationByHandle(handle, out var info)) return null;

            var index = ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow;
            return new FileIdentity(info.VolumeSerialNumber, index);
        }
        catch
        {
            return null;
        }
    }

    private static class NativeMethods
    {
        internal const uint FILE_SHARE_READ = 0x00000001;
        internal const uint FILE_SHARE_WRITE = 0x00000002;
        internal const uint FILE_SHARE_DELETE = 0x00000004;
        internal const uint OPEN_EXISTING = 3;
        internal const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern SafeFileHandle CreateFileW(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetFileInformationByHandle(
            SafeFileHandle hFile,
            out BY_HANDLE_FILE_INFORMATION lpFileInformation);

        [StructLayout(LayoutKind.Sequential)]
        internal struct BY_HANDLE_FILE_INFORMATION
        {
            public uint FileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
            public uint VolumeSerialNumber;
            public uint FileSizeHigh;
            public uint FileSizeLow;
            public uint NumberOfLinks;
            public uint FileIndexHigh;
            public uint FileIndexLow;
        }
    }
}
