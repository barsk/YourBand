using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace YourBand.Runtime.Settings
{
    /// <summary>
    /// The game was called GuitarTutor until 2026-11-12, and Unity keeps what the game saves under its company and
    /// product names: files in <c>%USERPROFILE%\AppData\LocalLow\&lt;company&gt;\&lt;product&gt;</c> (the VR settings) and the
    /// player preferences in the registry (<c>HKCU\Software\&lt;company&gt;\&lt;product&gt;</c>: which panels are shown, the song
    /// open last). At the first start under the new name this takes over what the old name had saved, as far as there
    /// is nothing of the new name's own: a file or a preference already there is left as it is, and the old ones stay
    /// (an older build keeps using them). In a built game only.
    /// </summary>
    public static class LegacyData
    {
        const string OldName = "GuitarTutor";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void TakeOver()
        {
            if (Application.isEditor)
                return;
            try
            {
                TakeOverFiles();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"YourBand: could not take over the files saved as {OldName}: {e.Message}");
            }
            try
            {
                TakeOverPreferences();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"YourBand: could not take over the preferences saved as {OldName}: {e.Message}");
            }
        }

        /// <summary>The settings files (JSON: the VR settings) in the old company and product folder that the new one does not have.</summary>
        static void TakeOverFiles()
        {
            var now = new DirectoryInfo(Application.persistentDataPath);  // <LocalLow>\<company>\<product>
            var localLow = now.Parent?.Parent;
            if (localLow == null)
                return;
            var old = new DirectoryInfo(Path.Combine(localLow.FullName, OldName, OldName));
            if (!old.Exists || string.Equals(old.FullName, now.FullName, StringComparison.OrdinalIgnoreCase))
                return;
            foreach (var file in old.GetFiles("*.json"))
            {
                var target = Path.Combine(now.FullName, file.Name);
                if (File.Exists(target))
                    continue;
                file.CopyTo(target);
                Debug.Log($"YourBand: took over {file.Name} from {old.FullName}");
            }
        }

        /// <summary>The player preferences the old name saved that the new one has no value for.</summary>
        static void TakeOverPreferences()
        {
#if UNITY_STANDALONE_WIN
            // (the registry through the Windows API: the profile the game is built for has no Microsoft.Win32.Registry)
            if (RegOpenKeyEx(CurrentUser, $@"Software\{OldName}\{OldName}", 0, KeyRead, out var old) != 0)
                return;
            try
            {
                if (RegCreateKeyEx(CurrentUser, $@"Software\{Application.companyName}\{Application.productName}", 0, null, 0,
                                   KeyRead | KeyWrite, IntPtr.Zero, out var now, out _) != 0)
                    return;
                try
                {
                    var taken = 0;
                    var data = new byte[64 * 1024];
                    for (var index = 0; ; ++index)
                    {
                        var name = new StringBuilder(16384);
                        var nameLength = name.Capacity;
                        var length = data.Length;
                        var result = RegEnumValue(old, index, name, ref nameLength, IntPtr.Zero, out var type, data, ref length);
                        if (result == NoMoreItems)
                            break;
                        if (result != 0)
                            continue;  // (a value too long for the buffer: left)
                        var have = 0;
                        if (RegQueryValueEx(now, name.ToString(), IntPtr.Zero, out _, null, ref have) == 0)
                            continue;  // the new name has its own
                        if (RegSetValueEx(now, name.ToString(), 0, type, data, length) == 0)
                            ++taken;
                    }
                    if (taken > 0)
                        Debug.Log($"YourBand: took over {taken} saved preferences from the registry key of {OldName}");
                }
                finally
                {
                    RegCloseKey(now);
                }
            }
            finally
            {
                RegCloseKey(old);
            }
#endif
        }

#if UNITY_STANDALONE_WIN
        static readonly IntPtr CurrentUser = new IntPtr(unchecked((int)0x80000001));  // HKEY_CURRENT_USER
        const int KeyRead = 0x20019, KeyWrite = 0x20006, NoMoreItems = 259;

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        static extern int RegOpenKeyEx(IntPtr key, string subKey, int options, int access, out IntPtr result);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        static extern int RegCreateKeyEx(IntPtr key, string subKey, int reserved, string keyClass, int options, int access,
                                         IntPtr security, out IntPtr result, out int disposition);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        static extern int RegEnumValue(IntPtr key, int index, StringBuilder name, ref int nameLength, IntPtr reserved,
                                       out int type, byte[] data, ref int dataLength);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        static extern int RegQueryValueEx(IntPtr key, string name, IntPtr reserved, out int type, byte[] data, ref int dataLength);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        static extern int RegSetValueEx(IntPtr key, string name, int reserved, int type, byte[] data, int dataLength);

        [DllImport("advapi32.dll")]
        static extern int RegCloseKey(IntPtr key);
#endif
    }
}
