using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Options;
using Squirrel;

namespace SquirrelCli
{
    internal class OptionValidationException : Exception
    {
        public OptionValidationException(string message) : base(message)
        {
        }

        public OptionValidationException(string propertyName, string message) : base($"Argument '{propertyName}': {message}")
        {

        }
    }

    internal abstract class ValidatedOptionSet : OptionSet
    {
        protected virtual bool IsNullOrDefault(string propertyName)
        {
            var p = this.GetType().GetProperty(propertyName);
            object argument = p.GetValue(this, null);

            // deal with normal scenarios
            if (argument == null) return true;

            // deal with non-null nullables
            Type methodType = argument.GetType();
            if (Nullable.GetUnderlyingType(methodType) != null) return false;

            // deal with boxed value types
            Type argumentType = argument.GetType();
            if (argumentType.IsValueType && argumentType != methodType) {
                object obj = Activator.CreateInstance(argument.GetType());
                return obj.Equals(argument);
            }

            return false;
        }

        protected virtual void IsRequired(params string[] propertyNames)
        {
            foreach (var property in propertyNames) {
                IsRequired(property);
            }
        }

        protected virtual void IsRequired(string propertyName)
        {
            if (IsNullOrDefault(propertyName))
                throw new OptionValidationException($"Argument '{propertyName}' is required");
        }

        protected virtual void IsValidFile(string propertyName)
        {
            var p = this.GetType().GetProperty(propertyName);
            var path = p.GetValue(this, null) as string;
            if (path != null)
                if (!File.Exists(path))
                    throw new OptionValidationException($"Argument '{propertyName}': Expected file to exist at this location but no file was found");
        }

        protected virtual void IsValidUrl(string propertyName)
        {
            var p = this.GetType().GetProperty(propertyName);
            var val = p.GetValue(this, null) as string;
            if (val != null)
                if (!Utility.IsHttpUrl(val))
                    throw new OptionValidationException(propertyName, "Must start with http or https and be a valid URI.");

        }

        public abstract void Validate();

        public virtual void WriteOptionDescriptions()
        {
            WriteOptionDescriptions(Console.Out);
        }
    }

    internal abstract class CommandAction
    {
        public string Command { get; protected set; }
        public string Description { get; protected set; }
        public abstract void Execute(IEnumerable<string> args);
        public abstract void PrintHelp();
    }

    internal class CommandAction<T> : CommandAction where T : ValidatedOptionSet, new()
    {
        public T Options { get; }
        public Action<T> Action { get; }

        public CommandAction(string command, string description, T options, Action<T> action)
        {
            Command = command;
            Description = description;
            Options = options;
            Action = action;
        }

        public override void Execute(IEnumerable<string> args)
        {
            Options.Parse(args);
            Options.Validate();
            Action(Options);
        }

        public override void PrintHelp()
        {
            Options.WriteOptionDescriptions();
        }
    }

    internal class CommandSet : List<CommandAction>
    {
        //public CommandSet() : base(StringComparer.InvariantCultureIgnoreCase) { }

        public void Add<T>(string command, string description, T options, Action<T> action) where T : ValidatedOptionSet, new()
        {
            this.Add(new CommandAction<T>(command, description, options, action));
        }

        public virtual void Execute(string[] args)
        {
            if (args.Length == 0)
                throw new OptionValidationException("Must specify a command to execute.");

            var combined = String.Join(" ", args);
            CommandAction cmd = null;

            foreach (var k in this.OrderByDescending(k => k.Command.Length)) {
                if (combined.StartsWith(k.Command, StringComparison.InvariantCultureIgnoreCase)) {
                    cmd = k;
                    break;
                }
            }

            if (cmd == null)
                throw new OptionValidationException($"Command was not specified or does not exist.");

            cmd.Execute(combined.Substring(cmd.Command.Length).Split(' '));
        }

        public virtual void WriteHelp()
        {
            var exeName = Path.GetFileName(AssemblyRuntimeInfo.EntryExePath);
            Console.WriteLine($"Usage: {exeName} [command] [options]");
            Console.WriteLine();
            Console.WriteLine("Commands:");

            var array = this.ToArray();
            for (var i = 0; i < array.Length; i++) {
                var c = array[i];

                // print command name + desc
                Console.WriteLine();
                Utility.ConsoleWriteWithColor(c.Command, ConsoleColor.Blue);
                if (!String.IsNullOrWhiteSpace(c.Description))
                    Console.Write(": " + c.Description);


                //Console.Write(c.Command);
                //if(String.IsNullOrWhiteSpace(c.Description))
                //    Console.WriteLine();
                //else 
                //    Console.WriteLine(": " + c.Description);


                //Console.Write(c.);

                // group similar command parameters together
                if (i + 1 < array.Length) {
                    if (c.GetType() == array[i + 1].GetType()) {
                        continue;
                    }

                }

                Console.WriteLine();
                c.PrintHelp();

                //Console.WriteLine();
                //c.Value.WriteOptionDescriptions();
                //Console.WriteLine();
            }
        }
    }
}
