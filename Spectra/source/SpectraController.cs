using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Spectra
{
	class SpectraController
	{
        private Dictionary<String, MethodInfo> Commands;

        [SpectraCommand("h", 0, 1, "h {Command Name: String?}")]
        public void Help(String[] args)
        {
            switch(args.Length)
            {
                case 0:
                    Array.ForEach(Commands.Values.ToArray(), (m) => Console.WriteLine(m.GetCustomAttribute<SpectraCommand>().usage));
                    break;
                case 1:
                    Console.WriteLine(Commands[args[0]].GetCustomAttribute<SpectraCommand>().usage);
                    break;
                default:
                    Console.WriteLine("Invalid use of command 'h'.");
                    break;
            }
        }

		public void RunSpectraCommandLine()
		{
			Commands = ParseSpectraCommands();

			while(true)
			{
				Console.Write(">> ");

				var input = Console.ReadLine().Split(' ');
				var command = input[0];
				var args = input.Skip(1).ToArray();

				Commands[command].Invoke(this, new object[] { args });

				Thread.Sleep(50);
			}
		}

		private Dictionary<String, MethodInfo> ParseSpectraCommands()
		{
			var commands = new Dictionary<String, MethodInfo>();
			var methods = GetType().GetMethods().Where(m => m.GetCustomAttribute<SpectraCommand>() != null).ToArray();
			foreach(var method in methods)
			{
				commands.Add(method.GetCustomAttribute<SpectraCommand>().command, method);
			}
			return commands;
		}

		public static void NotYetMain(string[] args)
		{
			var spectra = new SpectraController();
			spectra.RunSpectraCommandLine();
		}

	}

	class SpectraCommand : Attribute
	{

		public readonly String command;
		public readonly int minArgs;
		public readonly int maxArgs;
        public readonly String usage;

		public SpectraCommand(String command, int minArgs, int maxArgs, String usage)
		{
			this.command = command;
			this.minArgs = minArgs;
			this.maxArgs = maxArgs;
            this.usage = usage;
		}

	}
}
