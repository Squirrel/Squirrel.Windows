using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

// this file was not being used anywhere, and I commented it out so I could remove the Microsoft.CSharp dependency
// but I'm not sure if I'm ready to delete this yet. Note: It does not work on later versions of windows 10 
// possibly see https://pinto10blog.wordpress.com/ for some newer options

namespace Squirrel.Shell
{
    //public static class TaskbarHelper
    //{
    //    public static bool IsPinnedToTaskbar(string executablePath)
    //    {
    //        var taskbarPath = Path.Combine(
    //            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    //            "Microsoft\\Internet Explorer\\Quick Launch\\User Pinned\\TaskBar");

    //        return Directory
    //            .GetFiles(taskbarPath, "*.lnk")
    //            .Select(pinnedShortcut => new ShellLink(pinnedShortcut))
    //            .Any(shortcut => String.Equals(shortcut.Target, executablePath, StringComparison.OrdinalIgnoreCase));
    //    }

    //    public static void PinToTaskbar(string executablePath)
    //    {
    //        pinUnpin(executablePath, "pin to taskbar");

    //        if (!IsPinnedToTaskbar(executablePath)) {
    //            throw new Exception("Pinning executable to taskbar failed.");
    //        }
    //    }

    //    public static void UnpinFromTaskbar(string executablePath)
    //    {
    //        pinUnpin(executablePath, "unpin from taskbar");

    //        if (IsPinnedToTaskbar(executablePath)) {
    //            throw new Exception("Executable is still pinned to taskbar.");
    //        }
    //    }

    //    static void pinUnpin(string executablePath, string verbToExecute)
    //    {
    //        if (!File.Exists(executablePath)) {
    //            throw new FileNotFoundException(executablePath);
    //        }

    //        dynamic shellApplication = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));

    //        try {
    //            var path = Path.GetDirectoryName(executablePath);
    //            var fileName = Path.GetFileName(executablePath);

    //            dynamic directory = shellApplication.NameSpace(path);
    //            dynamic link = directory.ParseName(fileName);

    //            dynamic verbs = link.Verbs();

    //            for (var i = 0; i < verbs.Count(); i++) {
    //                dynamic verb = verbs.Item(i);
    //                string verbName = verb.Name.Replace(@"&", String.Empty).ToLower();

    //                if (verbName.Equals(verbToExecute)) {
    //                    verb.DoIt();
    //                }
    //            }
    //        } finally {
    //            Marshal.ReleaseComObject(shellApplication);
    //        }
    //    }
    //}
}
