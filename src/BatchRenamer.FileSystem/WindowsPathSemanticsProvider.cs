using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using BatchRenamer.Core;

namespace BatchRenamer.FileSystem;

/// <summary>
/// Queries directory-level case sensitivity when Windows exposes it. Failure is explicit: callers
/// receive IsReliable=false instead of silently assuming all Windows paths are case-insensitive.
/// </summary>
public sealed class WindowsPathSemanticsProvider : IPathSemanticsProvider
{
    public PathSemantics GetSemantics(string directoryPath)
    {
        var caseSensitive = false;
        var reliable = false;
        var source = "Windows fallback";
        int? maxComponentLength = null;

        try
        {
            if (Directory.Exists(directoryPath))
            {
                using var handle = NativeMethods.CreateFileW(
                    directoryPath,
                    NativeMethods.FILE_READ_ATTRIBUTES,
                    NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE | NativeMethods.FILE_SHARE_DELETE,
                    IntPtr.Zero,
                    NativeMethods.OPEN_EXISTING,
                    NativeMethods.FILE_FLAG_BACKUP_SEMANTICS,
                    IntPtr.Zero);

                if (!handle.IsInvalid)
                {
                    var info = new NativeMethods.FILE_CASE_SENSITIVE_INFO();
                    var size = Marshal.SizeOf<NativeMethods.FILE_CASE_SENSITIVE_INFO>();
                    var buffer = Marshal.AllocHGlobal(size);
                    try
                    {
                        Marshal.StructureToPtr(info, buffer, false);
                        if (NativeMethods.GetFileInformationByHandleEx(
                                handle,
                                NativeMethods.FILE_INFO_BY_HANDLE_CLASS.FileCaseSensitiveInfo,
                                buffer,
                                (uint)size))
                        {
                            info = Marshal.PtrToStructure<NativeMethods.FILE_CASE_SENSITIVE_INFO>(buffer);
                            caseSensitive = (info.Flags & NativeMethods.FILE_CS_FLAG_CASE_SENSITIVE_DIR) != 0;
                            reliable = true;
                            source = "FileCaseSensitiveInfo";
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(buffer);
                    }
                }
            }
        }
        catch
        {
            // Kept explicit through IsReliable=false.
        }

        try
        {
            var root = Path.GetPathRoot(directoryPath);
            if (!string.IsNullOrWhiteSpace(root)
                && NativeMethods.GetVolumeInformationW(root, null, 0, out _, out var maxComponent, out _, null, 0))
            {
                maxComponentLength = (int)maxComponent;
            }
        }
        catch
        {
            // Optional capability data.
        }

        // Modern .NET/WPF can use extended-length paths; 32767 is the Windows Unicode path ceiling,
        // not the legacy MAX_PATH=260 assumption. Component limits remain volume-specific.
        return new PathSemantics(caseSensitive, reliable, maxComponentLength ?? 255, 32767, source);
    }

    private static class NativeMethods
    {
        internal const uint FILE_READ_ATTRIBUTES = 0x00000080;
        internal const uint FILE_SHARE_READ = 0x00000001;
        internal const uint FILE_SHARE_WRITE = 0x00000002;
        internal const uint FILE_SHARE_DELETE = 0x00000004;
        internal const uint OPEN_EXISTING = 3;
        internal const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
        internal const uint FILE_CS_FLAG_CASE_SENSITIVE_DIR = 0x00000001;

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
        internal static extern bool GetFileInformationByHandleEx(
            SafeFileHandle hFile,
            FILE_INFO_BY_HANDLE_CLASS fileInformationClass,
            IntPtr lpFileInformation,
            uint dwBufferSize);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetVolumeInformationW(
            string lpRootPathName,
            System.Text.StringBuilder? lpVolumeNameBuffer,
            uint nVolumeNameSize,
            out uint lpVolumeSerialNumber,
            out uint lpMaximumComponentLength,
            out uint lpFileSystemFlags,
            System.Text.StringBuilder? lpFileSystemNameBuffer,
            uint nFileSystemNameSize);

        internal enum FILE_INFO_BY_HANDLE_CLASS
        {
            FileBasicInfo = 0,
            FileStandardInfo = 1,
            FileNameInfo = 2,
            FileRenameInfo = 3,
            FileDispositionInfo = 4,
            FileAllocationInfo = 5,
            FileEndOfFileInfo = 6,
            FileStreamInfo = 7,
            FileCompressionInfo = 8,
            FileAttributeTagInfo = 9,
            FileIdBothDirectoryInfo = 10,
            FileIdBothDirectoryRestartInfo = 11,
            FileIoPriorityHintInfo = 12,
            FileRemoteProtocolInfo = 13,
            FileFullDirectoryInfo = 14,
            FileFullDirectoryRestartInfo = 15,
            FileStorageInfo = 16,
            FileAlignmentInfo = 17,
            FileIdInfo = 18,
            FileIdExtdDirectoryInfo = 19,
            FileIdExtdDirectoryRestartInfo = 20,
            FileDispositionInfoEx = 21,
            FileRenameInfoEx = 22,
            FileCaseSensitiveInfo = 23,
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct FILE_CASE_SENSITIVE_INFO
        {
            public uint Flags;
        }
    }
}
