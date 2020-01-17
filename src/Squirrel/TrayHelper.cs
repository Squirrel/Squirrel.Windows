using Microsoft.Win32;
using Squirrel.SimpleSplat;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Squirrel
{
    public class TrayStateChanger : IEnableLogger
    {
        public List<NOTIFYITEM> GetTrayItems()
        {
            var instance = new TrayNotify();
            try {
                if (useLegacyInterface()) {
                    return getTrayItemsWin7(instance);
                } else {
                    return getTrayItems(instance);
                }
            } finally {
                Marshal.ReleaseComObject(instance);
            }
        }

        public void PromoteTrayItem(string exeToPromote)
        {
            var instance = new TrayNotify();

            try {
                var items = default(List<NOTIFYITEM>);
                var legacy = useLegacyInterface();

                if (legacy) {
                    items = getTrayItemsWin7(instance);
                } else {
                    items = getTrayItems(instance);
                }

                exeToPromote = exeToPromote.ToLowerInvariant();

                for (int i = 0; i < items.Count; i++) {
                    var item = items[i];
                    var exeName = item.exe_name.ToLowerInvariant();

                    if (!exeName.Contains(exeToPromote)) continue;

                    if (item.preference != NOTIFYITEM_PREFERENCE.PREFERENCE_SHOW_WHEN_ACTIVE) continue;
                    item.preference = NOTIFYITEM_PREFERENCE.PREFERENCE_SHOW_ALWAYS;

                    var writable = NOTIFYITEM_Writable.fromNotifyItem(item);
                    if (legacy) {
                        var notifier = (ITrayNotifyWin7)instance;
                        notifier.SetPreference(ref writable);
                    } else {
                        var notifier = (ITrayNotify)instance;
                        notifier.SetPreference(ref writable);
                    }
                }
            } catch (Exception ex) {
                Console.WriteLine("Failed to promote Tray icon: " + ex.ToString());
            } finally {
                Marshal.ReleaseComObject(instance);
            }
        }

        public unsafe void RemoveDeadEntries(List<string> executablesInPackage, string rootAppDirectory, string currentAppVersion)
        {
            var iconStreamData = default(byte[]);
            try {
                iconStreamData = (byte[])Registry.GetValue("HKEY_CURRENT_USER\\Software\\Classes\\Local Settings\\Software\\Microsoft\\Windows\\CurrentVersion\\TrayNotify", "IconStreams", new byte[] { 00 });
            } catch (Exception ex) {
                Console.WriteLine("Couldn't load IconStreams key, bailing: " + ex.ToString());
                return;
            }

            if (iconStreamData == null || iconStreamData.Length < 20) return;
            var toKeep = new List<byte[]>();
            var header = default(IconStreamsHeader);

            fixed (byte* b = iconStreamData) {
                header = (IconStreamsHeader)Marshal.PtrToStructure((IntPtr)b, typeof(IconStreamsHeader));
                byte* current;

                if (header.count <= 1) return;

                for (int i=0; i < header.count; i++) {
                    var offset = Marshal.SizeOf(typeof(IconStreamsHeader)) + (i * Marshal.SizeOf(typeof(IconStreamsItem)));
                    if (offset > iconStreamData.Length) {
                        this.Log().Error("Corrupted IconStreams regkey, bailing");
                        return;
                    }

                    current = b + offset;

                    var item = (IconStreamsItem)Marshal.PtrToStructure((IntPtr)current, typeof(IconStreamsItem));

                    try {
                        var path = item.ExePath.ToLowerInvariant();

                        // Someone completely unrelated? Keep it!
                        if (!executablesInPackage.Any(exe => path.Contains(exe))) {
                            goto keepItem;
                        }
                        
                        // Not an installed app? Keep it!
                        if (!path.StartsWith(rootAppDirectory, StringComparison.Ordinal)) {
                            goto keepItem;
                        }

                        // The current version? Keep it!
                        if (path.Contains("app-" + currentAppVersion)) {
                            goto keepItem;
                        }

                        // Don't keep this item, remove it from IconStreams
                        continue;

                    keepItem:

                        var newItem = new byte[Marshal.SizeOf(typeof(IconStreamsItem))];
                        Array.Copy(iconStreamData, offset, newItem, 0, newItem.Length);
                        toKeep.Add(newItem);
                    } catch (Exception ex) {
                        this.Log().ErrorException("Failed to parse IconStreams regkey", ex);
                        return;
                    }
                }

                if (header.count == toKeep.Count) {
                    return;
                }

                header.count = (uint)toKeep.Count;
                Marshal.StructureToPtr(header, (IntPtr)b, false);

                current = b + Marshal.SizeOf(typeof(IconStreamsHeader));
                for(int i = 0; i < toKeep.Count; i++) {
                    Marshal.Copy(toKeep[i], 0, (IntPtr)current, toKeep[i].Length);
                    current += toKeep[i].Length;
                }
            }

            try {
                var newSize = Marshal.SizeOf(typeof(IconStreamsHeader)) + (toKeep.Count * Marshal.SizeOf(typeof(IconStreamsItem)));
                var toSave = new byte[newSize];
                Array.Copy(iconStreamData, toSave, newSize);
                Registry.SetValue("HKEY_CURRENT_USER\\Software\\Classes\\Local Settings\\Software\\Microsoft\\Windows\\CurrentVersion\\TrayNotify", "IconStreams", toSave);
            } catch (Exception ex) {
                Console.WriteLine("Failed to write new IconStreams regkey: " + ex.ToString());
            }

            return;
        }

        static List<NOTIFYITEM> getTrayItems(TrayNotify instance)
        {
            var notifier = (ITrayNotify)instance;
            var callback = new NotificationCb();
            var handle = default(ulong);

            notifier.RegisterCallback(callback, out handle);
            notifier.UnregisterCallback(handle);
            return callback.items;
        }

        static List<NOTIFYITEM> getTrayItemsWin7(TrayNotify instance)
        {
            var notifier = (ITrayNotifyWin7)instance;
            var callback = new NotificationCb();

            notifier.RegisterCallback(callback);
            notifier.RegisterCallback(null);
            return callback.items;
        }

        class NotificationCb : INotificationCb
        {
            public readonly List<NOTIFYITEM> items = new List<NOTIFYITEM>();

            public void Notify([In] uint nEvent, [In] ref NOTIFYITEM notifyItem)
            {
                items.Add(notifyItem);
            }
        }

        static bool useLegacyInterface()
        {
            var ver = Environment.OSVersion.Version;
            if (ver.Major < 6) return true;
            if (ver.Major > 6) return false;

            // Windows 6.2 and higher use new interface
            return ver.Minor <= 1;
        }
    }

    // The known values for NOTIFYITEM's dwPreference member.
    public enum NOTIFYITEM_PREFERENCE
    {
        // In Windows UI: "Only show notifications."
        PREFERENCE_SHOW_WHEN_ACTIVE = 0,
        // In Windows UI: "Hide icon and notifications."
        PREFERENCE_SHOW_NEVER = 1,
        // In Windows UI: "Show icon and notifications."
        PREFERENCE_SHOW_ALWAYS = 2
    };

    // NOTIFYITEM describes an entry in Explorer's registry of status icons.
    // Explorer keeps entries around for a process even after it exits.
    public struct NOTIFYITEM
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public string exe_name;    // The file name of the creating executable.

        [MarshalAs(UnmanagedType.LPWStr)]
        public string tip;         // The last hover-text value associated with this status
                                   // item.

        public IntPtr icon;       // The icon associated with this status item.
        public IntPtr hwnd;       // The HWND associated with the status item.
        public NOTIFYITEM_PREFERENCE preference;  // Determines the behavior of the icon with respect to
                                                  // the taskbar
        public uint id;    // The ID specified by the application.  (hWnd, uID) is
                           // unique.
        public Guid guid;  // The GUID specified by the application, alternative to
                           // uID.
    };
    public struct NOTIFYITEM_Writable
    {
        public IntPtr exe_name;    // The file name of the creating executable.

        public IntPtr tip;         // The last hover-text value associated with this status
                                   // item.

        public IntPtr icon;       // The icon associated with this status item.
        public IntPtr hwnd;       // The HWND associated with the status item.
        public NOTIFYITEM_PREFERENCE preference;  // Determines the behavior of the icon with respect to
                                                  // the taskbar
        public uint id;    // The ID specified by the application.  (hWnd, uID) is
                           // unique.
        public Guid guid;  // The GUID specified by the application, alternative to
                           // uID.

        public static NOTIFYITEM_Writable fromNotifyItem(NOTIFYITEM item)
        {
            return new NOTIFYITEM_Writable {
                exe_name = Marshal.StringToCoTaskMemAuto(item.exe_name),
                tip = Marshal.StringToCoTaskMemAuto(item.tip),
                icon = item.icon,
                hwnd = item.hwnd,
                preference = item.preference,
                id = item.id,
                guid = item.guid
            };
        }
    };

    [ComImport]
    [Guid("D782CCBA-AFB0-43F1-94DB-FDA3779EACCB")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface INotificationCb
    {
        void Notify([In]uint nEvent, [In] ref NOTIFYITEM notifyItem);
    }

    [ComImport]
    [Guid("FB852B2C-6BAD-4605-9551-F15F87830935")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ITrayNotifyWin7
    {
        void RegisterCallback([MarshalAs(UnmanagedType.Interface)]INotificationCb callback);
        void SetPreference([In] ref NOTIFYITEM_Writable notifyItem);
        void EnableAutoTray([In] bool enabled);
    }

    [ComImport]
    [Guid("D133CE13-3537-48BA-93A7-AFCD5D2053B4")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ITrayNotify
    {
        void RegisterCallback([MarshalAs(UnmanagedType.Interface)]INotificationCb callback, [Out] out ulong handle);
        void UnregisterCallback([In] ulong handle);
        void SetPreference([In] ref NOTIFYITEM_Writable notifyItem);
        void EnableAutoTray([In] bool enabled);
        void DoAction([In] bool enabled);
    }

    [ComImport, Guid("25DEAD04-1EAC-4911-9E3A-AD0A4AB560FD")]
    class TrayNotify { }

    public struct IconStreamsHeader {
        public uint cbSize;
        public uint unknown1;
        public uint unknown2;
        public uint count;
        public uint unknown3;
    }

    public struct IconStreamsItem {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 528)]
        public byte[] exe_path;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1112)]
        public byte[] dontcare;

        public unsafe string ExePath {
            get {
                byte[] exeCopy = new byte[exe_path.Length];

                // https://raw.githubusercontent.com/lestert2005/SystemTrayModder/b1061f3758f8ff9c43d77157c7a62c7e5cc6885d/source/Program.cs
                for (int i=0; i < exe_path.Length; i++) {
                    var b = exe_path[i];
                    if (b > 64 && b < 91) {
                        exeCopy[i] = (byte)((b - 64 + 13) % 26 + 64);
                        continue;
                    }

                    if (b > 96 && b < 123) {
                        exeCopy[i] = (byte)((b - 96 + 13) % 26 + 96);
                        continue;
                    }

                    exeCopy[i] = b;
                }

                fixed (byte* b = exeCopy) {
                    return Marshal.PtrToStringUni((IntPtr)b);
                }
            }
        }
    }
}
