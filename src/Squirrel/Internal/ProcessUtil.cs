using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Squirrel
{
    internal static class ProcessUtil
    {
        /*
         * caesay — 09/12/2021 at 12:10 PM
         * yeah
         * can I steal this for squirrel? 
         * Roman — 09/12/2021 at 12:10 PM
         * sure :)
         * reference CommandRunner.cs on the github url as source? :)
         * https://github.com/RT-Projects/RT.Util/blob/ef660cd693f66bc946da3aaa368893b03b74eed7/RT.Util.Core/CommandRunner.cs#L327
         */

        /// <summary>
        ///     Given a number of argument strings, constructs a single command line string with all the arguments escaped
        ///     correctly so that a process using standard Windows API for parsing the command line will receive exactly the
        ///     strings passed in here. See Remarks.</summary>
        /// <remarks>
        ///     The string is only valid for passing directly to a process. If the target process is invoked by passing the
        ///     process name + arguments to cmd.exe then further escaping is required, to counteract cmd.exe's interpretation
        ///     of additional special characters. See <see cref="EscapeCmdExeMetachars"/>.</remarks>
        [SupportedOSPlatform("windows")]
        private static string ArgsToCommandLine(IEnumerable<string> args)
        {
            var sb = new StringBuilder();
            foreach (var arg in args) {
                if (arg == null)
                    continue;
                if (sb.Length != 0)
                    sb.Append(' ');
                // For details, see https://web.archive.org/web/20150318010344/http://blogs.msdn.com/b/twistylittlepassagesallalike/archive/2011/04/23/everyone-quotes-arguments-the-wrong-way.aspx
                // or https://devblogs.microsoft.com/oldnewthing/?p=12833
                if (arg.Length != 0 && arg.IndexOfAny(_cmdChars) < 0)
                    sb.Append(arg);
                else {
                    sb.Append('"');
                    for (int c = 0; c < arg.Length; c++) {
                        int backslashes = 0;
                        while (c < arg.Length && arg[c] == '\\') {
                            c++;
                            backslashes++;
                        }
                        if (c == arg.Length) {
                            sb.Append('\\', backslashes * 2);
                            break;
                        } else if (arg[c] == '"') {
                            sb.Append('\\', backslashes * 2 + 1);
                            sb.Append('"');
                        } else {
                            sb.Append('\\', backslashes);
                            sb.Append(arg[c]);
                        }
                    }
                    sb.Append('"');
                }
            }
            return sb.ToString();
        }
        private static readonly char[] _cmdChars = new[] { ' ', '"', '\n', '\t', '\v' };

        /// <summary>
        ///     Escapes all cmd.exe meta-characters by prefixing them with a ^. See <see cref="ArgsToCommandLine"/> for more
        ///     information.</summary>
        [SupportedOSPlatform("windows")]
        private static string EscapeCmdExeMetachars(string command)
        {
            var result = new StringBuilder();
            foreach (var ch in command) {
                switch (ch) {
                case '(':
                case ')':
                case '%':
                case '!':
                case '^':
                case '"':
                case '<':
                case '>':
                case '&':
                case '|':
                    result.Append('^');
                    break;
                }
                result.Append(ch);
            }
            return result.ToString();
        }

        private static string ArgsToCommandLineUnix(IEnumerable<string> args)
        {
            var sb = new StringBuilder();
            foreach (var arg in args) {
                if (arg == null)
                    continue;
                if (sb.Length != 0)
                    sb.Append(' ');

                // there are just too many 'command chars' in unix, so we play it 
                // super safe here and escape the string if there are any non-alpha-numeric
                if (System.Text.RegularExpressions.Regex.IsMatch(arg, @"$[\w]^")) {
                    sb.Append(arg);
                } else {
                    // https://stackoverflow.com/a/33949338/184746
                    // single quotes are 'strong quotes' and can contain everything
                    // except never other single quotes.
                    sb.Append("'");
                    sb.Append(arg.Replace("'", @"'\''"));
                    sb.Append("'");
                }
            }
            return sb.ToString();
        }

        private static ProcessStartInfo CreateProcessStartInfo(string fileName, string workingDirectory)
        {
            var psi = new ProcessStartInfo(fileName);
            psi.UseShellExecute = false;
            psi.WindowStyle = ProcessWindowStyle.Hidden;
            psi.ErrorDialog = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory;
            return psi;
        }

        private static (ProcessStartInfo StartInfo, string CommandDisplayString) CreateProcessStartInfo(string fileName, IEnumerable<string> args, string workingDirectory)
        {
            var psi = CreateProcessStartInfo(fileName, workingDirectory);

            string displayArgs;

#if NET5_0_OR_GREATER
            foreach (var a in args) psi.ArgumentList.Add(a);
            displayArgs = $"['{String.Join("', '", args)}']";
#else
            psi.Arguments = displayArgs = SquirrelRuntimeInfo.IsWindows ? ArgsToCommandLine(args) : ArgsToCommandLineUnix(args);
#endif

            return (psi, fileName + " " + displayArgs);
        }

        private static (int ExitCode, string StdOutput) InvokeProcess(ProcessStartInfo psi, CancellationToken ct)
        {
            var pi = Process.Start(psi);
            while (!ct.IsCancellationRequested) {
                if (pi.WaitForExit(500)) break;
            }

            if (ct.IsCancellationRequested && !pi.HasExited) {
                pi.Kill();
                ct.ThrowIfCancellationRequested();
            }

            string output = pi.StandardOutput.ReadToEnd();
            string error = pi.StandardError.ReadToEnd();
            var all = (output ?? "") + Environment.NewLine + (error ?? "");

            return (pi.ExitCode, all.Trim());
        }

        public static Process StartNonBlocking(string fileName, IEnumerable<string> args, string workingDirectory)
        {
            var (psi, cmd) = CreateProcessStartInfo(fileName, args, workingDirectory);
            return Process.Start(psi);
        }

        public static (int ExitCode, string StdOutput, string Command) InvokeProcess(string fileName, IEnumerable<string> args, string workingDirectory, CancellationToken ct)
        {
            var (psi, cmd) = CreateProcessStartInfo(fileName, args, workingDirectory);
            var p = InvokeProcess(psi, ct);
            return (p.ExitCode, p.StdOutput, cmd);
        }

        //public static (int ExitCode, string StdOutput, string Command) InvokeProcess(string fileName, string args, string workingDirectory, CancellationToken ct)
        //{
        //    var psi = CreateProcessStartInfo(fileName, workingDirectory);
        //    psi.Arguments = args;
        //    var p = InvokeProcess(psi, ct);
        //    return (p.ExitCode, p.StdOutput, fileName + " " + args);
        //}

        public static Task<(int ExitCode, string StdOutput, string Command)> InvokeProcessAsync(string fileName, IEnumerable<string> args, string workingDirectory, CancellationToken ct)
        {
            return Task.Run(() => InvokeProcess(fileName, args, workingDirectory, ct));
        }
    }
}
