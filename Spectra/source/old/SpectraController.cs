using System;
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

        [SpectraCommand("pr", 0, 1, "PreGain: pr {Factor: Int?}")]
        public void PreGain(String[] args)
        {
            switch(args.Length)
            {
                case 0:
                    SpectraProcessor.PRE_GAIN = 1000;
                    break;
                case 1:
                    SpectraProcessor.PRE_GAIN = int.Parse(args[0]);
                    break;
                default:
                    Console.WriteLine("Invalid use of command 'pr'.\n");
                    break;
            }
        }

        [SpectraCommand("po", 0, 1, "PostGain: po {Factor: Int?}")]
        public void PostGain(String[] args)
        {
            switch (args.Length)
            {
                case 0:
                    SpectraProcessor.POST_GAIN = 1000;
                    break;
                case 1:
                    SpectraProcessor.POST_GAIN = int.Parse(args[0]);
                    break;
                default:
                    Console.WriteLine("Invalid use of command 'po'.\n");
                    break;
            }
        }

        [SpectraCommand("bl", 0, 1, "Blend: bl {Factor: Float?}")]
        public void Blend(String[] args)
        {
            switch (args.Length)
            {
                case 0:
                    SpectraProcessor.BLEND = 0.1;
                    break;
                case 1:
                    SpectraProcessor.BLEND = float.Parse(args[0]);
                    Console.WriteLine(SpectraProcessor.BLEND);
                    break;
                default:
                    Console.WriteLine("Invalid use of command 'bl'.\n");
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
@"Command Arduino: ca {Command Code: Int!} {Arg2: Int?} ... {ArgN: Int?}
    Valid Commands Codes:
        0 = Use 'cl' Spectra Command
      1-3 = Use 'sp' Spectra Command
       10 = Static Hue Breathing // Arg2[0-255] = Hue // Arg3[1-255] = Breathing Speed // Arg4[0-255] = Max Brightness
       11 = Rotate Hue Breathing // Arg2[0-255] = Hue Rotation Speed // Arg3[0-255] = Breathing Speed // Arg4[0-255] = Max Brightness")]
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
                try
                {
                    buffer[i] = Byte.Parse(args[i]);
                }
                catch (Exception e)
                {
                    buffer[i] = 0;
                }
            }
            buffer[0] += 192;

            Console.WriteLine("Sending Command: {0} ({1}) Metadata Length: {2}.", buffer[0] & 63, buffer[0], buffer.Length - 1);
            Port.Write(buffer, 0, buffer.Length);
            Thread.Sleep(100);
        }

        [SpectraCommand("sp", 2, 3, 
@"Spectrum: sp {Display Mode: Int!} {Arg2: Int!} {Arg3: Int?} ... {ArgN: Int?}
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
                        return;
                    }
                }
                else
                {
                    if(args.Length != 3)
                    {
                        Console.WriteLine("Invalid argument count. Expected 3, received {0}", args.Length);
                        return;
                    }
                }
            }

            CommandArduino(args);

            Processor = new SpectraProcessor(Port);
            Processor.Start();
        }

        [SpectraCommand("wv", 2, 3,
@"Waveform: wv {Display Mode: Int!} {Arg2: Int!} {Arg3: Int?} ... {ArgN: Int?}
    Valid Display Modes:
        1 = Static Hue // Arg2[0-255] = Hue // Arg3[0-255] = Frosting
        2 = Static Rainbow // Arg2[0-255] = Frosting
        3 = Scrolling Rainbow // Arg2[0-255] = Scroll Rate // Arg 3[0-255] = Frosting")]
        public void Waveform(String[] args)
        {
            if (Port == null)
            {
                Connect(null);
                if (Port == null) return;
            }

            var mode = Int32.Parse(args[0]);
            if (mode < 1 || mode > 3)
            {
                Console.WriteLine("Invalid Display Mode.");
                return;
            }
            else
            {
                if (mode == 2)
                {
                    if (args.Length != 2)
                    {
                        Console.WriteLine("Invalid argument count. Expected 2, received {0}", args.Length);
                        return;
                    }
                }
                else
                {
                    if (args.Length != 3)
                    {
                        Console.WriteLine("Invalid argument count. Expected 3, received {0}", args.Length);
                        return;
                    }
                }
            }

         
            CommandArduino(args);

            Processor = new WaveformProcessor(Port);
            Processor.Start();
        }

        [SpectraCommand("pl", 2, 6,
@"Perlin: pl {Display Mode: Int!} {Arg2: Int!} {Arg3: Int?} ... {ArgN: ?}
    Valid Display Modes:
        1 = Static Hue // Arg2[0-255] = Hue // Arg3[0-255] = Frosting // Arg4[1-8] = Octaves // Arg5[Float] = X Scale // Arg6[Float] = T Scale
        2 = Static Rainbow // Arg2[0-255] = Frosting // Arg3[0] // Arg4[1-8] = Octaves // Arg5[Float] = X Scale // Arg6[Float] = T Scale
        3 = Scrolling Rainbow // Arg2[0-255] = Scroll Rate // Arg 3[0-255] = Frosting // Arg4[1-8] = Octaves // Arg5[Float] = X Scale // Arg6[Float] = T Scale")]
        public void Perlin(String[] args)
        {
            if (Port == null)
            {
                Connect(null);
                if (Port == null) return;
            }

            var mode = Int32.Parse(args[0]);
            if (mode < 1 || mode > 3)
            {
                Console.WriteLine("Invalid Display Mode.");
                return;
            }
            else
            {
                if (mode == 2)
                {
                    if (args.Length != 2 && args.Length != 6)
                    {
                        Console.WriteLine("Invalid argument count. Expected 2 or 6, received {0}", args.Length);
                        return;
                    }
                }
                else
                {
                    if (args.Length != 3 && args.Length != 6)
                    {
                        Console.WriteLine("Invalid argument count. Expected 3 or 6, received {0}", args.Length);
                        return;
                    }
                }
            }

            if(args.Length == 6)
            {
                PerlinProcessor.OCTAVES = int.Parse(args[3]);
                Console.WriteLine("Perlin Octaves: {0}", PerlinProcessor.OCTAVES);
                PerlinProcessor.X_SCALE = float.Parse(args[4]);
                Console.WriteLine("Perlin X Scale: {0}", PerlinProcessor.X_SCALE);
                PerlinProcessor.T_SCALE = float.Parse(args[5]);
                Console.WriteLine("Perlin Time Scale: {0}", PerlinProcessor.T_SCALE);

                if(mode == 2)
                {
                    CommandArduino(new string[] {args[0], args[1]});
                } else
                {
                    CommandArduino(new string[] { args[0], args[1], args[2] });
                }

            } else
            {
                CommandArduino(args);
            }

            Processor = new PerlinProcessor(Port);
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

		//public static void Main(string[] args)
		//{
		//	var spectra = new SpectraController();
		//	spectra.RunSpectraCommandLine();
		//}

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
