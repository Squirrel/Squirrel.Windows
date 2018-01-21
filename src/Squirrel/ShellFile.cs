using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

// All of this code is from http://vbaccelerator.com/home/NET/Code/Libraries/Shell_Projects/Creating_and_Modifying_Shortcuts/article.asp

namespace Squirrel.Shell
{
    /// <summary>
    /// Summary description for ShellLink.
    /// </summary>
    public class ShellLink : IDisposable
    {
        [ComImport()]
        [Guid("0000010C-0000-0000-C000-000000000046")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IPersist
        {
            [PreserveSig]
            //[helpstring("Returns the class identifier for the component object")]
            void GetClassID(out Guid pClassID);
        }

        [ComImport()]
        [Guid("0000010B-0000-0000-C000-000000000046")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IPersistFile
        {
            // can't get this to go if I extend IPersist, so put it here:
            [PreserveSig]
            void GetClassID(out Guid pClassID);

            //[helpstring("Checks for changes since last file write")]      
            void IsDirty();

            //[helpstring("Opens the specified file and initializes the object from its contents")]      
            void Load(
                [MarshalAs(UnmanagedType.LPWStr)] string pszFileName,
                uint dwMode);

            //[helpstring("Saves the object into the specified file")]      
            void Save(
                [MarshalAs(UnmanagedType.LPWStr)] string pszFileName,
                [MarshalAs(UnmanagedType.Bool)] bool fRemember);

            //[helpstring("Notifies the object that save is completed")]      
            void SaveCompleted(
                [MarshalAs(UnmanagedType.LPWStr)] string pszFileName);

            //[helpstring("Gets the current name of the file associated with the object")]      
            void GetCurFile(
                [MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PropVariant
        {
            public short variantType;
            public short Reserved1, Reserved2, Reserved3;
            public IntPtr pointerValue;

            public static PropVariant FromString(string str)
            {
                var pv = new PropVariant() {
                    variantType = 31,  // VT_LPWSTR
                    pointerValue = Marshal.StringToCoTaskMemUni(str),
                };

                return pv;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PROPERTYKEY
        {
            public Guid fmtid;
            public UIntPtr pid;

            public static PROPERTYKEY PKEY_AppUserModel_ID {
                get {
                    return new PROPERTYKEY() {
                        fmtid = Guid.ParseExact("{9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3}", "B"),
                        pid = new UIntPtr(5),
                    };
                }
            }

            public static PROPERTYKEY PKEY_AppUserModel_ToastActivatorCLSID {
                get {
                    return new PROPERTYKEY() {
                        fmtid = Guid.ParseExact("{9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3}", "B"),
                        pid = new UIntPtr(26),
                    };
                }
            }
        }

        [ComImport]
        [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IPropertyStore
        {
            [PreserveSig]
            int GetCount([Out] out uint cProps);
            [PreserveSig]
            int GetAt([In] uint iProp, out PROPERTYKEY pkey);
            [PreserveSig]
            int GetValue([In] ref PROPERTYKEY key, out PropVariant pv);
            [PreserveSig]
            int SetValue([In] ref PROPERTYKEY key, [In] ref PropVariant pv);
            [PreserveSig]
            int Commit();
        }

        [ComImport()]
        [Guid("000214EE-0000-0000-C000-000000000046")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellLinkA
        {
            //[helpstring("Retrieves the path and filename of a shell link object")]
            void GetPath(
                [Out(), MarshalAs(UnmanagedType.LPStr)] StringBuilder pszFile,
                int cchMaxPath,
                ref _WIN32_FIND_DATAA pfd,
                uint fFlags);

            //[helpstring("Retrieves the list of shell link item identifiers")]
            void GetIDList(out IntPtr ppidl);

            //[helpstring("Sets the list of shell link item identifiers")]
            void SetIDList(IntPtr pidl);

            //[helpstring("Retrieves the shell link description string")]
            void GetDescription(
                [Out(), MarshalAs(UnmanagedType.LPStr)] StringBuilder pszFile,
                int cchMaxName);

            //[helpstring("Sets the shell link description string")]
            void SetDescription(
                [MarshalAs(UnmanagedType.LPStr)] string pszName);

            //[helpstring("Retrieves the name of the shell link working directory")]
            void GetWorkingDirectory(
                [Out(), MarshalAs(UnmanagedType.LPStr)] StringBuilder pszDir,
                int cchMaxPath);

            //[helpstring("Sets the name of the shell link working directory")]
            void SetWorkingDirectory(
                [MarshalAs(UnmanagedType.LPStr)] string pszDir);

            //[helpstring("Retrieves the shell link command-line arguments")]
            void GetArguments(
                [Out(), MarshalAs(UnmanagedType.LPStr)] StringBuilder pszArgs,
                int cchMaxPath);

            //[helpstring("Sets the shell link command-line arguments")]
            void SetArguments(
                [MarshalAs(UnmanagedType.LPStr)] string pszArgs);

            //[propget, helpstring("Retrieves or sets the shell link hot key")]
            void GetHotkey(out short pwHotkey);
            //[propput, helpstring("Retrieves or sets the shell link hot key")]
            void SetHotkey(short pwHotkey);

            //[propget, helpstring("Retrieves or sets the shell link show command")]
            void GetShowCmd(out uint piShowCmd);
            //[propput, helpstring("Retrieves or sets the shell link show command")]
            void SetShowCmd(uint piShowCmd);

            //[helpstring("Retrieves the location (path and index) of the shell link icon")]
            void GetIconLocation(
                [Out(), MarshalAs(UnmanagedType.LPStr)] StringBuilder pszIconPath,
                int cchIconPath,
                out int piIcon);

            //[helpstring("Sets the location (path and index) of the shell link icon")]
            void SetIconLocation(
                [MarshalAs(UnmanagedType.LPStr)] string pszIconPath,
                int iIcon);

            //[helpstring("Sets the shell link relative path")]
            void SetRelativePath(
                [MarshalAs(UnmanagedType.LPStr)] string pszPathRel,
                uint dwReserved);

            //[helpstring("Resolves a shell link. The system searches for the shell link object and updates the shell link path and its list of identifiers (if necessary)")]
            void Resolve(
                IntPtr hWnd,
                uint fFlags);

            //[helpstring("Sets the shell link path and filename")]
            void SetPath(
                [MarshalAs(UnmanagedType.LPStr)] string pszFile);
        }

        [ComImport()]
        [Guid("000214F9-0000-0000-C000-000000000046")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellLinkW
        {
            //[helpstring("Retrieves the path and filename of a shell link object")]
            void GetPath(
                [Out(), MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile,
                int cchMaxPath,
                ref _WIN32_FIND_DATAW pfd,
                uint fFlags);

            //[helpstring("Retrieves the list of shell link item identifiers")]
            void GetIDList(out IntPtr ppidl);

            //[helpstring("Sets the list of shell link item identifiers")]
            void SetIDList(IntPtr pidl);

            //[helpstring("Retrieves the shell link description string")]
            void GetDescription(
                [Out(), MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile,
                int cchMaxName);

            //[helpstring("Sets the shell link description string")]
            void SetDescription(
                [MarshalAs(UnmanagedType.LPWStr)] string pszName);

            //[helpstring("Retrieves the name of the shell link working directory")]
            void GetWorkingDirectory(
                [Out(), MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir,
                int cchMaxPath);

            //[helpstring("Sets the name of the shell link working directory")]
            void SetWorkingDirectory(
                [MarshalAs(UnmanagedType.LPWStr)] string pszDir);

            //[helpstring("Retrieves the shell link command-line arguments")]
            void GetArguments(
                [Out(), MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs,
                int cchMaxPath);

            //[helpstring("Sets the shell link command-line arguments")]
            void SetArguments(
                [MarshalAs(UnmanagedType.LPWStr)] string pszArgs);

            //[propget, helpstring("Retrieves or sets the shell link hot key")]
            void GetHotkey(out short pwHotkey);
            //[propput, helpstring("Retrieves or sets the shell link hot key")]
            void SetHotkey(short pwHotkey);

            //[propget, helpstring("Retrieves or sets the shell link show command")]
            void GetShowCmd(out uint piShowCmd);
            //[propput, helpstring("Retrieves or sets the shell link show command")]
            void SetShowCmd(uint piShowCmd);

            //[helpstring("Retrieves the location (path and index) of the shell link icon")]
            void GetIconLocation(
                [Out(), MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath,
                int cchIconPath,
                out int piIcon);

            //[helpstring("Sets the location (path and index) of the shell link icon")]
            void SetIconLocation(
                [MarshalAs(UnmanagedType.LPWStr)] string pszIconPath,
                int iIcon);

            //[helpstring("Sets the shell link relative path")]
            void SetRelativePath(
                [MarshalAs(UnmanagedType.LPWStr)] string pszPathRel,
                uint dwReserved);

            //[helpstring("Resolves a shell link. The system searches for the shell link object and updates the shell link path and its list of identifiers (if necessary)")]
            void Resolve(
                IntPtr hWnd,
                uint fFlags);

            //[helpstring("Sets the shell link path and filename")]
            void SetPath(
                [MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }

        [Guid("00021401-0000-0000-C000-000000000046")]
        [ClassInterface(ClassInterfaceType.None)]
        [ComImport()]
        class CShellLink
        {
        }

        enum EShellLinkGP : uint
        {
            SLGP_SHORTPATH = 1,
            SLGP_UNCPRIORITY = 2
        }

        [Flags]
        enum EShowWindowFlags : uint
        {
            SW_HIDE = 0,
            SW_SHOWNORMAL = 1,
            SW_NORMAL = 1,
            SW_SHOWMINIMIZED = 2,
            SW_SHOWMAXIMIZED = 3,
            SW_MAXIMIZE = 3,
            SW_SHOWNOACTIVATE = 4,
            SW_SHOW = 5,
            SW_MINIMIZE = 6,
            SW_SHOWMINNOACTIVE = 7,
            SW_SHOWNA = 8,
            SW_RESTORE = 9,
            SW_SHOWDEFAULT = 10,
            SW_MAX = 10
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4, Size = 0,
            CharSet = CharSet.Unicode)]
        struct _WIN32_FIND_DATAW
        {
            public uint dwFileAttributes;
            public _FILETIME ftCreationTime;
            public _FILETIME ftLastAccessTime;
            public _FILETIME ftLastWriteTime;
            public uint nFileSizeHigh;
            public uint nFileSizeLow;
            public uint dwReserved0;
            public uint dwReserved1;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] // MAX_PATH
                public string cFileName;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
            public string cAlternateFileName;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4, Size = 0,
            CharSet = CharSet.Ansi)]
        struct _WIN32_FIND_DATAA
        {
            public uint dwFileAttributes;
            public _FILETIME ftCreationTime;
            public _FILETIME ftLastAccessTime;
            public _FILETIME ftLastWriteTime;
            public uint nFileSizeHigh;
            public uint nFileSizeLow;
            public uint dwReserved0;
            public uint dwReserved1;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] // MAX_PATH
                public string cFileName;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
            public string cAlternateFileName;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4, Size = 0)]
        struct _FILETIME
        {
            public uint dwLowDateTime;
            public uint dwHighDateTime;
        }

        class UnManagedMethods
        {
            [DllImport("Shell32", CharSet = CharSet.Auto)]
            internal static extern int ExtractIconEx(
                [MarshalAs(UnmanagedType.LPTStr)] string lpszFile,
                int nIconIndex,
                IntPtr[] phIconLarge,
                IntPtr[] phIconSmall,
                int nIcons);

            [DllImport("user32")]
            internal static extern int DestroyIcon(IntPtr hIcon);
        }

        /// <summary>
        /// Flags determining how the links with missing
        /// targets are resolved.
        /// </summary>
        [Flags]
        public enum EShellLinkResolveFlags : uint
        {
            /// <summary>
            /// Allow any match during resolution.  Has no effect
            /// on ME/2000 or above, use the other flags instead.
            /// </summary>
            SLR_ANY_MATCH = 0x2,

            /// <summary>
            /// Call the Microsoft Windows Installer. 
            /// </summary>
            SLR_INVOKE_MSI = 0x80,

            /// <summary>
            /// Disable distributed link tracking. By default, 
            /// distributed link tracking tracks removable media 
            /// across multiple devices based on the volume name. 
            /// It also uses the UNC path to track remote file 
            /// systems whose drive letter has changed. Setting 
            /// SLR_NOLINKINFO disables both types of tracking.
            /// </summary>
            SLR_NOLINKINFO = 0x40,

            /// <summary>
            /// Do not display a dialog box if the link cannot be resolved. 
            /// When SLR_NO_UI is set, a time-out value that specifies the 
            /// maximum amount of time to be spent resolving the link can 
            /// be specified in milliseconds. The function returns if the 
            /// link cannot be resolved within the time-out duration. 
            /// If the timeout is not set, the time-out duration will be 
            /// set to the default value of 3,000 milliseconds (3 seconds). 
            /// </summary>                                  
            SLR_NO_UI = 0x1,

            /// <summary>
            /// Not documented in SDK.  Assume same as SLR_NO_UI but 
            /// intended for applications without a hWnd.
            /// </summary>
            SLR_NO_UI_WITH_MSG_PUMP = 0x101,

            /// <summary>
            /// Do not update the link information. 
            /// </summary>
            SLR_NOUPDATE = 0x8,

            /// <summary>
            /// Do not execute the search heuristics. 
            /// </summary>                                                        
            SLR_NOSEARCH = 0x10,

            /// <summary>
            /// Do not use distributed link tracking. 
            /// </summary>
            SLR_NOTRACK = 0x20,

            /// <summary>
            /// If the link object has changed, update its path and list 
            /// of identifiers. If SLR_UPDATE is set, you do not need to 
            /// call IPersistFile::IsDirty to determine whether or not 
            /// the link object has changed. 
            /// </summary>
            SLR_UPDATE = 0x4
        }

        public enum LinkDisplayMode : uint
        {
            edmNormal = EShowWindowFlags.SW_NORMAL,
            edmMinimized = EShowWindowFlags.SW_SHOWMINNOACTIVE,
            edmMaximized = EShowWindowFlags.SW_MAXIMIZE
        }

        // Use Unicode (W) under NT, otherwise use ANSI      
        IShellLinkW linkW;
        IShellLinkA linkA;
        string shortcutFile = "";

        /// <summary>
        /// Creates an instance of the Shell Link object.
        /// </summary>
        public ShellLink()
        {
            if (System.Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                linkW = (IShellLinkW)new CShellLink();
            }
            else
            {
                linkA = (IShellLinkA)new CShellLink();
            }
        }

        /// <summary>
        /// Creates an instance of a Shell Link object
        /// from the specified link file
        /// </summary>
        /// <param name="linkFile">The Shortcut file to open</param>
        public ShellLink(string linkFile) : this()
        {
            Open(linkFile);
        }

        /// <summary>
        /// Call dispose just in case it hasn't happened yet
        /// </summary>
        ~ShellLink()
        {
            Dispose();
        }

        /// <summary>
        /// Dispose the object, releasing the COM ShellLink object
        /// </summary>
        public void Dispose()
        {
            if (linkW != null)
            {
                Marshal.ReleaseComObject(linkW);
                linkW = null;
            }
            if (linkA != null)
            {
                Marshal.ReleaseComObject(linkA);
                linkA = null;
            }
        }

        public string ShortCutFile
        {
            get { return this.shortcutFile; }
            set { this.shortcutFile = value; }
        }

        /// <summary>
        /// Gets a System.Drawing.Icon containing the icon for this
        /// ShellLink object.
        /// </summary>
        public Icon LargeIcon
        {
            get { return getIcon(true); }
        }

        public Icon SmallIcon
        {
            get { return getIcon(false); }
        }

        Icon getIcon(bool large)
        {
            // Get icon index and path:
            int iconIndex = 0;
            StringBuilder iconPath = new StringBuilder(260, 260);
            if (linkA == null)
            {
                linkW.GetIconLocation(iconPath, iconPath.Capacity, out iconIndex);
            }
            else
            {
                linkA.GetIconLocation(iconPath, iconPath.Capacity, out iconIndex);
            }
            string iconFile = iconPath.ToString();

            // If there are no details set for the icon, then we must use
            // the shell to get the icon for the target:
            if (iconFile.Length == 0)
            {
                // Use the FileIcon object to get the icon:
                FileIcon.SHGetFileInfoConstants flags =
                    FileIcon.SHGetFileInfoConstants.SHGFI_ICON |
                        FileIcon.SHGetFileInfoConstants.SHGFI_ATTRIBUTES;
                if (large)
                {
                    flags = flags | FileIcon.SHGetFileInfoConstants.SHGFI_LARGEICON;
                }
                else
                {
                    flags = flags | FileIcon.SHGetFileInfoConstants.SHGFI_SMALLICON;
                }
                FileIcon fileIcon = new FileIcon(Target, flags);
                return fileIcon.ShellIcon;
            }
            else
            {
                // Use ExtractIconEx to get the icon:
                IntPtr[] hIconEx = new IntPtr[1] { IntPtr.Zero };
                int iconCount = 0;
                if (large)
                {
                    iconCount = UnManagedMethods.ExtractIconEx(
                        iconFile,
                        iconIndex,
                        hIconEx,
                        null,
                        1);
                }
                else
                {
                    iconCount = UnManagedMethods.ExtractIconEx(
                        iconFile,
                        iconIndex,
                        null,
                        hIconEx,
                        1);
                }
                // If success then return as a GDI+ object
                Icon icon = null;
                if (hIconEx[0] != IntPtr.Zero)
                {
                    icon = Icon.FromHandle(hIconEx[0]);
                    //UnManagedMethods.DestroyIcon(hIconEx[0]);
                }
                return icon;
            }
        }

        /// <summary>
        /// Gets the path to the file containing the icon for this shortcut.
        /// </summary>
        public string IconPath
        {
            get
            {
                StringBuilder iconPath = new StringBuilder(260, 260);
                int iconIndex = 0;
                if (linkA == null)
                {
                    linkW.GetIconLocation(iconPath, iconPath.Capacity, out
                        iconIndex);
                }
                else
                {
                    linkA.GetIconLocation(iconPath, iconPath.Capacity, out
                        iconIndex);
                }
                return iconPath.ToString();
            }
            set
            {
                StringBuilder iconPath = new StringBuilder(260, 260);
                int iconIndex = 0;
                if (linkA == null)
                {
                    linkW.GetIconLocation(iconPath, iconPath.Capacity, out
                        iconIndex);
                }
                else
                {
                    linkA.GetIconLocation(iconPath, iconPath.Capacity, out
                        iconIndex);
                }
                if (linkA == null)
                {
                    linkW.SetIconLocation(value, iconIndex);
                }
                else
                {
                    linkA.SetIconLocation(value, iconIndex);
                }
            }
        }

        /// <summary>
        /// Gets the index of this icon within the icon path's resources
        /// </summary>
        public int IconIndex
        {
            get
            {
                StringBuilder iconPath = new StringBuilder(260, 260);
                int iconIndex = 0;
                if (linkA == null)
                {
                    linkW.GetIconLocation(iconPath, iconPath.Capacity, out
                        iconIndex);
                }
                else
                {
                    linkA.GetIconLocation(iconPath, iconPath.Capacity, out
                        iconIndex);
                }
                return iconIndex;
            }
            set
            {
                StringBuilder iconPath = new StringBuilder(260, 260);
                int iconIndex = 0;
                if (linkA == null)
                {
                    linkW.GetIconLocation(iconPath, iconPath.Capacity, out
                        iconIndex);
                }
                else
                {
                    linkA.GetIconLocation(iconPath, iconPath.Capacity, out
                        iconIndex);
                }
                if (linkA == null)
                {
                    linkW.SetIconLocation(iconPath.ToString(), value);
                }
                else
                {
                    linkA.SetIconLocation(iconPath.ToString(), value);
                }
            }
        }

        /// <summary>
        /// Gets/sets the fully qualified path to the link's target
        /// </summary>
        public string Target
        {
            get
            {
                StringBuilder target = new StringBuilder(260, 260);
                if (linkA == null)
                {
                    _WIN32_FIND_DATAW fd = new _WIN32_FIND_DATAW();
                    linkW.GetPath(target, target.Capacity, ref fd,
                        (uint)EShellLinkGP.SLGP_UNCPRIORITY);
                }
                else
                {
                    _WIN32_FIND_DATAA fd = new _WIN32_FIND_DATAA();
                    linkA.GetPath(target, target.Capacity, ref fd,
                        (uint)EShellLinkGP.SLGP_UNCPRIORITY);
                }
                return target.ToString();
            }
            set
            {
                if (linkA == null)
                {
                    linkW.SetPath(value);
                }
                else
                {
                    linkA.SetPath(value);
                }
            }
        }

        /// <summary>
        /// Gets/sets the Working Directory for the Link
        /// </summary>
        public string WorkingDirectory
        {
            get
            {
                StringBuilder path = new StringBuilder(260, 260);
                if (linkA == null)
                {
                    linkW.GetWorkingDirectory(path, path.Capacity);
                }
                else
                {
                    linkA.GetWorkingDirectory(path, path.Capacity);
                }
                return path.ToString();
            }
            set
            {
                if (linkA == null)
                {
                    linkW.SetWorkingDirectory(value);
                }
                else
                {
                    linkA.SetWorkingDirectory(value);
                }
            }
        }

        /// <summary>
        /// Gets/sets the description of the link
        /// </summary>
        public string Description
        {
            get
            {
                StringBuilder description = new StringBuilder(1024, 1024);
                if (linkA == null)
                {
                    linkW.GetDescription(description, description.Capacity);
                }
                else
                {
                    linkA.GetDescription(description, description.Capacity);
                }
                return description.ToString();
            }
            set
            {
                if (linkA == null)
                {
                    linkW.SetDescription(value);
                }
                else
                {
                    linkA.SetDescription(value);
                }
            }
        }

        /// <summary>
        /// Gets/sets any command line arguments associated with the link
        /// </summary>
        public string Arguments
        {
            get
            {
                StringBuilder arguments = new StringBuilder(260, 260);
                if (linkA == null)
                {
                    linkW.GetArguments(arguments, arguments.Capacity);
                }
                else
                {
                    linkA.GetArguments(arguments, arguments.Capacity);
                }
                return arguments.ToString();
            }
            set
            {
                if (linkA == null)
                {
                    linkW.SetArguments(value);
                }
                else
                {
                    linkA.SetArguments(value);
                }
            }
        }

        /// <summary>
        /// Gets/sets the initial display mode when the shortcut is
        /// run
        /// </summary>
        public LinkDisplayMode DisplayMode
        {
            get
            {
                uint cmd = 0;
                if (linkA == null)
                {
                    linkW.GetShowCmd(out cmd);
                }
                else
                {
                    linkA.GetShowCmd(out cmd);
                }
                return (LinkDisplayMode)cmd;
            }
            set
            {
                if (linkA == null)
                {
                    linkW.SetShowCmd((uint)value);
                }
                else
                {
                    linkA.SetShowCmd((uint)value);
                }
            }
        }

        /// <summary>
        /// Gets/sets the HotKey to start the shortcut (if any)
        /// </summary>
        public short HotKey
        {
            get
            {
                short key = 0;
                if (linkA == null)
                {
                    linkW.GetHotkey(out key);
                }
                else
                {
                    linkA.GetHotkey(out key);
                }
                return key;
            }
            set
            {
                if (linkA == null)
                {
                    linkW.SetHotkey(value);
                }
                else
                {
                    linkA.SetHotkey(value);
                }
            }
        }

        /// <summary>
        /// Sets the appUserModelId
        /// </summary>
        public void SetAppUserModelId(string appId)
        {
            var propStore = (IPropertyStore)linkW;
            var pkey = PROPERTYKEY.PKEY_AppUserModel_ID;
            var str = PropVariant.FromString (appId);
            propStore.SetValue(ref pkey, ref str);
        }

        /// <summary>
        /// Sets the ToastActivatorCLSID
        /// </summary>
        public void SetToastActivatorCLSID(string clsid)
        {
            var propStore = (IPropertyStore)linkW;
            var pkey = PROPERTYKEY.PKEY_AppUserModel_ToastActivatorCLSID;
            var str = PropVariant.FromString (clsid);
            propStore.SetValue(ref pkey, ref str);
        }

        /// <summary>
        /// Saves the shortcut to ShortCutFile.
        /// </summary>
        public void Save()
        {
            Save(shortcutFile);
        }

        /// <summary>
        /// Saves the shortcut to the specified file
        /// </summary>
        /// <param name="linkFile">The shortcut file (.lnk)</param>
        public void Save(
            string linkFile
            )
        {
            // Save the object to disk
            if (linkA == null)
            {
                ((IPersistFile)linkW).Save(linkFile, true);
                shortcutFile = linkFile;
            }
            else
            {
                ((IPersistFile)linkA).Save(linkFile, true);
                shortcutFile = linkFile;
            }
        }

        /// <summary>
        /// Loads a shortcut from the specified file
        /// </summary>
        /// <param name="linkFile">The shortcut file (.lnk) to load</param>
        public void Open(
            string linkFile
            )
        {
            Open(linkFile,
                IntPtr.Zero,
                (EShellLinkResolveFlags.SLR_ANY_MATCH |
                    EShellLinkResolveFlags.SLR_NO_UI),
                1);
        }

        /// <summary>
        /// Loads a shortcut from the specified file, and allows flags controlling
        /// the UI behaviour if the shortcut's target isn't found to be set.
        /// </summary>
        /// <param name="linkFile">The shortcut file (.lnk) to load</param>
        /// <param name="hWnd">The window handle of the application's UI, if any</param>
        /// <param name="resolveFlags">Flags controlling resolution behaviour</param>
        public void Open(
            string linkFile,
            IntPtr hWnd,
            EShellLinkResolveFlags resolveFlags
            )
        {
            Open(linkFile,
                hWnd,
                resolveFlags,
                1);
        }

        /// <summary>
        /// Loads a shortcut from the specified file, and allows flags controlling
        /// the UI behaviour if the shortcut's target isn't found to be set.  If
        /// no SLR_NO_UI is specified, you can also specify a timeout.
        /// </summary>
        /// <param name="linkFile">The shortcut file (.lnk) to load</param>
        /// <param name="hWnd">The window handle of the application's UI, if any</param>
        /// <param name="resolveFlags">Flags controlling resolution behaviour</param>
        /// <param name="timeOut">Timeout if SLR_NO_UI is specified, in ms.</param>
        public void Open(
            string linkFile,
            IntPtr hWnd,
            EShellLinkResolveFlags resolveFlags,
            ushort timeOut
            )
        {
            uint flags;

            if ((resolveFlags & EShellLinkResolveFlags.SLR_NO_UI)
                == EShellLinkResolveFlags.SLR_NO_UI)
            {
                flags = (uint)((int)resolveFlags | (timeOut << 16));
            }
            else
            {
                flags = (uint)resolveFlags;
            }

            if (linkA == null)
            {
                ((IPersistFile)linkW).Load(linkFile, 0); //STGM_DIRECT)
                linkW.Resolve(hWnd, flags);
                this.shortcutFile = linkFile;
            }
            else
            {
                ((IPersistFile)linkA).Load(linkFile, 0); //STGM_DIRECT)
                linkA.Resolve(hWnd, flags);
                this.shortcutFile = linkFile;
            }
        }
    }

    /// <summary>
    /// Enables extraction of icons for any file type from
    /// the Shell.
    /// </summary>
    public class FileIcon
    {
        const int MAX_PATH = 260;

        [StructLayout(LayoutKind.Sequential)]
        struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public int dwAttributes;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MAX_PATH)]
            public string szDisplayName;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        [DllImport("shell32")]
        static extern IntPtr SHGetFileInfo(
            string pszPath,
            int dwFileAttributes,
            ref SHFILEINFO psfi,
            uint cbFileInfo,
            uint uFlags);

        [DllImport("user32.dll")]
        static extern int DestroyIcon(IntPtr hIcon);

        const int FORMAT_MESSAGE_ALLOCATE_BUFFER = 0x100;
        const int FORMAT_MESSAGE_ARGUMENT_ARRAY = 0x2000;
        const int FORMAT_MESSAGE_FROM_HMODULE = 0x800;
        const int FORMAT_MESSAGE_FROM_STRING = 0x400;
        const int FORMAT_MESSAGE_FROM_SYSTEM = 0x1000;
        const int FORMAT_MESSAGE_IGNORE_INSERTS = 0x200;
        const int FORMAT_MESSAGE_MAX_WIDTH_MASK = 0xFF;

        [DllImport("kernel32")]
        static extern int FormatMessage(
            int dwFlags,
            IntPtr lpSource,
            int dwMessageId,
            int dwLanguageId,
            string lpBuffer,
            uint nSize,
            IntPtr argumentsLong);

        [DllImport("kernel32")]
        static extern int GetLastError();

        string fileName;
        string displayName;
        string typeName;
        SHGetFileInfoConstants flags;
        Icon fileIcon;

        [Flags]
        public enum SHGetFileInfoConstants : int
        {
            SHGFI_ICON = 0x100, // get icon 
            SHGFI_DISPLAYNAME = 0x200, // get display name 
            SHGFI_TYPENAME = 0x400, // get type name 
            SHGFI_ATTRIBUTES = 0x800, // get attributes 
            SHGFI_ICONLOCATION = 0x1000, // get icon location 
            SHGFI_EXETYPE = 0x2000, // return exe type 
            SHGFI_SYSICONINDEX = 0x4000, // get system icon index 
            SHGFI_LINKOVERLAY = 0x8000, // put a link overlay on icon 
            SHGFI_SELECTED = 0x10000, // show icon in selected state 
            SHGFI_ATTR_SPECIFIED = 0x20000, // get only specified attributes 
            SHGFI_LARGEICON = 0x0, // get large icon 
            SHGFI_SMALLICON = 0x1, // get small icon 
            SHGFI_OPENICON = 0x2, // get open icon 
            SHGFI_SHELLICONSIZE = 0x4, // get shell size icon 
            //SHGFI_PIDL = 0x8,                  // pszPath is a pidl 
            SHGFI_USEFILEATTRIBUTES = 0x10, // use passed dwFileAttribute 
            SHGFI_ADDOVERLAYS = 0x000000020, // apply the appropriate overlays
            SHGFI_OVERLAYINDEX = 0x000000040 // Get the index of the overlay
        }

        /// <summary>
        /// Gets/sets the flags used to extract the icon
        /// </summary>
        public FileIcon.SHGetFileInfoConstants Flags
        {
            get { return flags; }
            set { flags = value; }
        }

        /// <summary>
        /// Gets/sets the filename to get the icon for
        /// </summary>
        public string FileName
        {
            get { return fileName; }
            set { fileName = value; }
        }

        /// <summary>
        /// Gets the icon for the chosen file
        /// </summary>
        public Icon ShellIcon
        {
            get { return fileIcon; }
        }

        /// <summary>
        /// Gets the display name for the selected file
        /// if the SHGFI_DISPLAYNAME flag was set.
        /// </summary>
        public string DisplayName
        {
            get { return displayName; }
        }

        /// <summary>
        /// Gets the type name for the selected file
        /// if the SHGFI_TYPENAME flag was set.
        /// </summary>
        public string TypeName
        {
            get { return typeName; }
        }

        /// <summary>
        ///  Gets the information for the specified 
        ///  file name and flags.
        /// </summary>
        public void GetInfo()
        {
            fileIcon = null;
            typeName = "";
            displayName = "";

            SHFILEINFO shfi = new SHFILEINFO();
            uint shfiSize = (uint)Marshal.SizeOf(shfi.GetType());

            IntPtr ret = SHGetFileInfo(
                fileName, 0, ref shfi, shfiSize, (uint)(flags));
            if (ret != IntPtr.Zero)
            {
                if (shfi.hIcon != IntPtr.Zero)
                {
                    fileIcon = System.Drawing.Icon.FromHandle(shfi.hIcon);
                    // Now owned by the GDI+ object
                    //DestroyIcon(shfi.hIcon);
                }
                typeName = shfi.szTypeName;
                displayName = shfi.szDisplayName;
            }
            else
            {
                int err = GetLastError();
                Console.WriteLine("Error {0}", err);
                string txtS = new string('\0', 256);
                int len = FormatMessage(
                    FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS,
                    IntPtr.Zero, err, 0, txtS, 256, IntPtr.Zero);
                Console.WriteLine("Len {0} text {1}", len, txtS);

                // throw exception
            }
        }

        /// <summary>
        /// Constructs a new, default instance of the FileIcon
        /// class.  Specify the filename and call GetInfo()
        /// to retrieve an icon.
        /// </summary>
        public FileIcon()
        {
            flags = SHGetFileInfoConstants.SHGFI_ICON |
                SHGetFileInfoConstants.SHGFI_DISPLAYNAME |
                SHGetFileInfoConstants.SHGFI_TYPENAME |
                SHGetFileInfoConstants.SHGFI_ATTRIBUTES |
                SHGetFileInfoConstants.SHGFI_EXETYPE;
        }

        /// <summary>
        /// Constructs a new instance of the FileIcon class
        /// and retrieves the icon, display name and type name
        /// for the specified file.      
        /// </summary>
        /// <param name="fileName">The filename to get the icon, 
        /// display name and type name for</param>
        public FileIcon(string fileName)
            : this()
        {
            this.fileName = fileName;
            GetInfo();
        }

        /// <summary>
        /// Constructs a new instance of the FileIcon class
        /// and retrieves the information specified in the 
        /// flags.
        /// </summary>
        /// <param name="fileName">The filename to get information
        /// for</param>
        /// <param name="flags">The flags to use when extracting the
        /// icon and other shell information.</param>
        public FileIcon(string fileName, FileIcon.SHGetFileInfoConstants flags)
        {
            this.fileName = fileName;
            this.flags = flags;
            GetInfo();
        }
    }
}
