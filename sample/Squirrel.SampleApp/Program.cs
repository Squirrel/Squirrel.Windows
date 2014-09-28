using System;
using System.Collections.Generic;
using System.IO;
using Mono.Options;

namespace Squirrel.SampleApp
{
	class Program
	{
		public static OptionSet Options { get; set; }
		public static string UpdateUrl { get; set; }
		public static string LogFile { get; set; }
		
		enum Commands
		{
			Undefined,
			Update
		}

		private static readonly IDictionary<Commands, Action> CommandHandlers = new Dictionary<Commands, Action>
		{
			{Commands.Update, Update}
		};

		static void Main(string[] args)
		{
			Log("Launched");

			SquirrelAwareApp.HandleEvents(
				OnInitialInstall, 
				OnAppUpdate, 
				OnAppObsoleted, 
				OnAppUninstall, 
				OnFirstRun, 
				args);

			var command = Commands.Undefined;

			Options = new OptionSet
			{
				"Usage: Squirrel.SampleApp.exe command [OPTS]",
				"Tests Squirrel functionality",
				"",
				"Commands",
				{"update", "Runs Squirrel Update", _ => command = Commands.Update},
				"",
				"Options:",
				{"h|?|help", "Display Help and exit", _ => ShowHelp()},
				{"u=|updateUrl=", "Path to the RELEASE file", v => UpdateUrl = v},
				{"l=|log=", "Path to a log file", v => LogFile = v},
			};

			Options.Parse(args);

			Action handler;
			if(!CommandHandlers.TryGetValue(command, out handler))
				throw new ApplicationException("Unknown or missing command");

			handler();
		}

		#region Commands

		private async static void Update()
		{
			using (var mgr = CreateUpdateManager())
			{
				await mgr.UpdateApp(progress => Console.WriteLine("Progress: {0}", progress));
			}
		}

		private static void ShowHelp()
		{
			Options.WriteOptionDescriptions(Console.Out);
		}

		#endregion

		#region Utils

		private static UpdateManager CreateUpdateManager()
		{
			if (String.IsNullOrWhiteSpace(UpdateUrl))
				throw new ApplicationException("-updateUrl must be provided");

			return new UpdateManager(UpdateUrl, "Squirrel.SampleApp", FrameworkVersion.Net45);
		}

		private static void Log(string message)
		{
			if (String.IsNullOrEmpty(LogFile))
				return;

			using (var stream = File.AppendText(LogFile))
			{
				stream.WriteLine("{0:yyyy-MM-dd HH:mm:ss} {1}", DateTime.Now, message);
			}
		}

		#endregion

		#region Squirrel Events

		private static void OnFirstRun()
		{
			Log("First Run");
		}

		private static void OnAppUninstall(Version v)
		{
			Log("Uninstall " + v);
		}

		private static void OnAppObsoleted(Version v)
		{
			Log("Obsoleted " + v);
		}

		private static void OnAppUpdate(Version v)
		{
			Log("Update " + v);
		}

		private static void OnInitialInstall(Version v)
		{
			Log("Install " + v);
		}

		#endregion
	}
}
