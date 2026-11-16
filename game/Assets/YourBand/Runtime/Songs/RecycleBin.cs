using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace YourBand.Runtime.Songs
{
    /// <summary>
    /// Files to the Windows Recycle Bin instead of deleting them for good (a song you delete can be got back). The shell's own file
    /// operation, with its undo flag; no dialog.
    /// </summary>
    public static class RecycleBin
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct ShFileOperation
        {
            public IntPtr Window;
            public uint Function;
            [MarshalAs(UnmanagedType.LPWStr)] public string From;
            [MarshalAs(UnmanagedType.LPWStr)] public string To;
            public ushort Flags;
            [MarshalAs(UnmanagedType.Bool)] public bool Aborted;
            public IntPtr NameMappings;
            [MarshalAs(UnmanagedType.LPWStr)] public string Title;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHFileOperationW(ref ShFileOperation operation);

        const uint Delete = 3;
        const ushort Silent = 0x4, NoConfirmation = 0x10, AllowUndo = 0x40, NoErrorUi = 0x400;

        /// <summary>Moves the files that exist to the Recycle Bin, one at a time; false if any could not be moved.</summary>
        public static bool Move(params string[] files)
        {
            var all = true;
            foreach (var file in files.Where(f => !string.IsNullOrEmpty(f) && File.Exists(f)))
            {
                var operation = new ShFileOperation
                {
                    Function = Delete,
                    From = Path.GetFullPath(file) + "\0",  // (a list of names ends with a second null)
                    Flags = (ushort)(AllowUndo | NoConfirmation | Silent | NoErrorUi),
                };
                all &= SHFileOperationW(ref operation) == 0 && !operation.Aborted && !File.Exists(file);
            }
            return all;
        }
    }
}
