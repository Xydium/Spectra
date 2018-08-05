﻿using System;
using System.Collections.Generic;
using System.IO.Ports;
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
        private SerialPort Port;
        private bool Running;
        private SpectraProcessor Processor;

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
                    if (Commands.ContainsKey(args[0]))
                    {
                        Console.WriteLine(Commands[args[0]].GetCustomAttribute<SpectraCommand>().usage + '\n');
                        break;
                    }
                    else
                    {
                        Console.WriteLine("Cannot invoke help on invalid command name.");
                        break;
                    }
                default:
                    Console.WriteLine("Invalid use of command 'h'.\n");
                    break;
            }
        }

        [SpectraCommand("q", 0, 0, "Quit: q")]
        public void Quit(String[] args)
        {
            Running = false;
        }

        [SpectraCommand("c", 0, 0, @"Connect: c {Port Name: String? = 'COM3'} {Baud Rate: Int? = 230400}")]
        public void Connect(String[] args)
        {
            if(Port != null)
            {
                Disconnect(null);
            }

            if(args == null || args.Length == 0)
            {
                args = new String[] { "COM3", "230400" };
            } else if(args.Length != 2)
            {
                Console.WriteLine("Connect accepts either 0 or 2 arguments.");
                return;
            }

            try
            {
                Port = new SerialPort(args[0], Int32.Parse(args[1]), Parity.None, 8, StopBits.One);
                Port.Open();
                Console.WriteLine("Successfully opened port {0} at {1} bps.", args[0], args[1]);
            } catch(Exception e)
            {
                Console.WriteLine("Failed to open port {0} at {1} bps.", args[0], args[1]);
                Port = null;
            }
        }

        [SpectraCommand("d", 0, 0, "Disconnect: d")]
        public void Disconnect(String[] args)
        {
            if(Port == null)
            {
                Console.WriteLine("Failed to disconnect port, none open.");
                return;
            }

            try
            {
                Port.Close();
                Console.WriteLine("Successfully disconnected port {0}.", Port.PortName);
                Port = null;
            } catch(Exception e)
            {
                Console.WriteLine("Failed to disconnect port {0}.", Port.PortName);
            }
        }

        [SpectraCommand("ca", 0, 10, 
@"Command Arduino: c {Command Code: Int!} {arg1: Int?} ... {argN: Int?}
    Valid Commands Codes:
        0 = Use 'cl' Spectra Command
      1-3 = Use 'sp' Spectra Command")]
        public void CommandArduino(String[] args)
        {
            if(Port == null)
            {
                Console.WriteLine("Cannot write to serial port, none open.");
                return;
            }

            byte[] buffer = new byte[args.Length];

            for (int i = 0; i < args.Length; i++)
            {
                buffer[i] = Byte.Parse(args[i]);
            }
            buffer[0] += 192;

            Console.WriteLine("Sending Command: {0} ({1}) Metadata Length: {2}.", buffer[0] & 63, buffer[0], buffer.Length - 1);
            Port.Write(buffer, 0, buffer.Length);
            Thread.Sleep(100);
        }

        [SpectraCommand("sp", 2, 3, 
@"Spectrum: sp {Display Mode: Int!} {Arg2: Int!} {Arg3: Int?}
    Valid Display Modes:
        1 = Static Hue // Arg2[0-255] = Hue // Arg3[0-255] = Frosting
        2 = Static Rainbow // Arg2[0-255] = Frosting
        3 = Scrolling Rainbow // Arg2[0-255] = Scroll Rate // Arg 3[0-255] = Frosting")]
        public void Spectrum(String[] args)
        {
            if(Port == null)
            {
                Connect(null);
                if (Port == null) return;
            }

            var mode = Int32.Parse(args[0]);
            if (mode < 1 || mode > 3)
            {
                Console.WriteLine("Invalid Display Mode.");
                return;
            } else
            {
                if(mode == 2)
                {
                    if(args.Length != 2)
                    {
                        Console.WriteLine("Invalid argument count. Expected 2, received {0}", args.Length);
                    }
                }
                else
                {
                    if(args.Length != 3)
                    {
                        Console.WriteLine("Invalid argument count. Expected 3, received {0}", args.Length);
                    }
                }
            }

            CommandArduino(args);

            Processor = new SpectraProcessor(Port);
            Processor.Start();
        }

        [SpectraCommand("cl", 0, 0, "Clear: cl")]
        public void Clear(String[] args)
        {
            if(Processor != null)
            {
                Processor.Stop();
                Processor = null;
            }

            CommandArduino(new String[] { "0" });
        }

		public void RunSpectraCommandLine()
		{
			Commands = ParseSpectraCommands();
            Running = true;

            Console.WriteLine("------ Spectra Controller ------");

            while (Running)
			{
				Console.Write(">> ");

				var input = Console.ReadLine().Split(' ');
				var command = input[0];
				var args = input.Skip(1).ToArray();

                if (Commands.ContainsKey(command))
                {
                    try
                    {
                        Commands[command].Invoke(this, new object[] { args });
                    } catch(Exception e)
                    {
                        Console.WriteLine(e.Message);
                    }
                } 
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

		public static void Main(string[] args)
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
