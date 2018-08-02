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
        private bool running;

        [SpectraCommand("h", 0, 1, "Help: h {Command Name: String?}")]
        public void Help(String[] args)
        {
            Console.WriteLine("Spectra Help (? = Optional, ! = Required):\n");
            switch(args.Length)
            {
                case 0:
                    Array.ForEach(Commands.Values.ToArray(), (m) => Console.WriteLine(m.GetCustomAttribute<SpectraCommand>().usage + '\n'));
                    break;
                case 1:
                    Console.WriteLine(Commands[args[0]].GetCustomAttribute<SpectraCommand>().usage + '\n');
                    break;
                default:
                    Console.WriteLine("Invalid use of command 'h'.\n");
                    break;
            }
        }

        [SpectraCommand("q", 0, 0, "Quit: q")]
        public void Quit(String[] args)
        {
            running = false;
        }

        [SpectraCommand("sp", 2, 3, 
@"Spectrum: sp {Display Mode: Int!} {Arg2: Int!} {Arg3: Int?}
    Valid Display Modes:
        1 = Static Hue // Arg2[0-255] = Hue // Arg3[0-255] = Frosting
        2 = Static Rainbow // Arg2[0-255] = Frosting
        3 = Scrolling Rainbow // Arg2[0-255] = Scroll Rate // Arg 3[0-255] = Frosting")]
        public void Spectrum(String[] args)
        {
            Console.WriteLine("This is where I'd put my Spectrum constroller, if I had one!");
        }

		public void RunSpectraCommandLine()
		{
			Commands = ParseSpectraCommands();
            running = true;

			while(running)
			{
				Console.Write(">> ");

				var input = Console.ReadLine().Split(' ');
				var command = input[0];
				var args = input.Skip(1).ToArray();

                if (Commands.ContainsKey(command))
                    Commands[command].Invoke(this, new object[] { args });
                else
                    Console.WriteLine("Invalid command. Execute command 'h' to see valid commands.\n");

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
