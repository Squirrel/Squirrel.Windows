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
        public OptionSet InsertAt(int index, string prototype, string description, Action<string> action)
        {
            return InsertAt(index, prototype, description, action, false);
        }

        public OptionSet InsertAt(int index, string prototype, string description, Action<string> action, bool hidden)
        {
            if (action == null)
                throw new ArgumentNullException("action");
            Option p = new ActionOption(prototype, description, 1, delegate (OptionValueCollection v) { action(v[0]); }, hidden);
            base.InsertItem(index, p);
            return this;
        }
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

        protected virtual void IsValidFile(string propertyName, string forcedExtension = null)
        {
            var p = this.GetType().GetProperty(propertyName);
            var path = p.GetValue(this, null) as string;
            if (path != null) {
                if (!File.Exists(path)) {
                    throw new OptionValidationException($"Argument '{propertyName}': Expected file to exist at this location but no file was found");
                } else if (forcedExtension != null && !Path.GetExtension(path).TrimStart('.').Equals(forcedExtension.TrimStart('.'), StringComparison.InvariantCultureIgnoreCase)) {
                    throw new OptionValidationException($"Argument '{propertyName}': File must be of type '{forcedExtension}'.");
                }
            }
        }

        protected virtual void IsValidDirectory(string propertyName, bool verifyIsNotEmpty)
        {
            var p = this.GetType().GetProperty(propertyName);
            var path = p.GetValue(this, null) as string;
            if (path != null) {
                if (!Directory.Exists(path)) {
                    throw new OptionValidationException($"Argument '{propertyName}': Expected directory to exist at this location but none was found");
                }

                if (verifyIsNotEmpty && !Directory.EnumerateFileSystemEntries(path).Any()) {
                    throw new OptionValidationException($"Argument '{propertyName}': Expected non-empty directory, but the specified directory was empty.");
                }
            }
        }

        protected virtual void IsValidUrl(string propertyName)
        {
            var p = this.GetType().GetProperty(propertyName);
            var val = p.GetValue(this, null) as string;
            if (val != null)
                if (!Utility.IsHttpUrl(val))
                    throw new OptionValidationException(propertyName, "Must start with http or https and be a valid URI.");
        }

        protected virtual int ParseIntArg(string propertyName, string propertyValue)
        {
            if (int.TryParse(propertyValue, out var value))
                return value;

            throw new OptionValidationException(propertyName, "Must be a valid integer.");
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

    internal class HelpText : CommandAction
    {
        public HelpText(string text)
        {
            Description = text;
        }

        public override void Execute(IEnumerable<string> args)
        {
            throw new NotSupportedException();
        }

        public override void PrintHelp()
        {
            Console.WriteLine(Description);
        }
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
        public void Add(string helpText)
        {
            this.Add(new HelpText(helpText));
        }

        public void Add<T>(string command, string description, T options, Action<T> action) where T : ValidatedOptionSet, new()
        {
            this.Add(new CommandAction<T>(command, description, options, action));
        }

        public virtual void Execute(string[] args)
        {
            if (args.Length == 0)
                throw new OptionValidationException("No arguments provided. Must specify a command to execute.");

            var commands = this.Where(k => !String.IsNullOrWhiteSpace(k.Command));
            CommandAction cmd = null;
            foreach (var k in commands.OrderByDescending(k => k.Command.Length)) {
                if (args[0].Equals(k.Command, StringComparison.InvariantCultureIgnoreCase)) {
                    cmd = k;
                    break;
                }
            }

            if (cmd == null) {
                throw new OptionValidationException(
                    $"Command '{args[0]}' does not exist. First argument must be one of the following: "
                    + String.Join(", ", commands.Select(s => s.Command)) + ".");
            }

            cmd.Execute(args.Skip(1));
        }

        public virtual void WriteHelp()
        {
            var array = this.ToArray();
            for (var i = 0; i < array.Length; i++) {
                var c = array[i];

                if (c is HelpText) {
                    c.PrintHelp();
                    continue;
                }

                // print command name + desc
                Console.WriteLine();
                Utility.ConsoleWriteWithColor(c.Command, ConsoleColor.Blue);
                if (!String.IsNullOrWhiteSpace(c.Description))
                    Console.Write(": " + c.Description);

                // group similar command parameters together
                if (i + 1 < array.Length) {
                    if (c.GetType() == array[i + 1].GetType()) {
                        continue;
                    }

                }

                Console.WriteLine();
                c.PrintHelp();
            }
        }
    }
}
