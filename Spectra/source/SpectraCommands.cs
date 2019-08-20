using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using System.IO.Ports;
using System.IO;

namespace Spectra.source {

    partial class Spectra {

        Dictionary<String, MethodInfo> commands;

        [SpectraCommand("help", 0, 1, "help [command: String?]")]
        public void Help(String[] args) {
            Console.WriteLine("Spectra Help (? = Optional, ! = Required):\n");
            switch (args.Length) {
                case 0:
                    Array.ForEach(commands.Values.ToArray(), (m) => Console.WriteLine(m.GetCustomAttribute<SpectraCommand>().usage + '\n'));
                    break;
                case 1:
                    if (commands.ContainsKey(args[0])) {
                        Console.WriteLine(commands[args[0]].GetCustomAttribute<SpectraCommand>().usage + '\n');
                        break;
                    } else {
                        Console.WriteLine("Cannot invoke help on invalid command name.");
                        break;
                    }
                default:
                    Console.WriteLine("Invalid use of command 'help'.\n");
                    break;
            }
        }

        [SpectraCommand("proctypes", 0, 0, "proctypes")]
        public void ProcessorTypes(string[] args) {
            try {
                Assembly a = typeof(Spectra).Assembly;
                foreach (Type t in a.GetTypes()) {
                    if (t.BaseType == typeof(Processor)) {
                        Console.WriteLine(t.Name);
                    }
                }
            } catch (Exception e) {

            }
        }

        [SpectraCommand("describe", 1, 1, "describe [processor: String!]")]
        public void Describe(string[] args) {
            try {
                Assembly a = typeof(Spectra).Assembly;
                Processor p = (Processor)a.CreateInstance("Spectra.source." + args[0], true, BindingFlags.Default, null, new object[] { null, null }, null, null);
                Console.WriteLine("Processor: {0}", args[0]);
                Console.WriteLine("\tDescription: {0}", p.description);
                Console.WriteLine("\tUsage: {0}", p.usage);
            } catch (Exception e) {
                Console.WriteLine("Processor does not exist.");
            }
        }

        [SpectraCommand("clear", 0, 1, "clear [channel: String? in H,S,V]")]
        public void Clear(string[] args) {
            lock (Lock) {
                procs.Clear();
                buffers.Clear();
                defaultBuffers();
            }
        }

        [SpectraCommand("getvar", 1, 1, "getvar [var: String!]")]
        public void GetVar(string[] args) {
            lock (Lock) {
                if (v.ContainsKey(args[0])) {
                    Console.WriteLine("{0} = {1}", args[0], v[args[0]].value);
                } else {
                    Console.WriteLine("Variable does not exist.");
                }
            }
        }

        [SpectraCommand("setvar", 2, 2, "setvar [var: String!] [value: Any!]")]
        public void SetVar(string[] args) {
            lock (Lock) {
                if (v.ContainsKey(args[0])) {
                    Console.Write("{0}: {1} -> ", args[0], v[args[0]].value);
                    v[args[0]].value = Variant.Parse(args[1]).value;
                } else {
                    Console.Write("{0}: null -> ", args[0]);
                    v.Add(args[0], Variant.Parse(args[1]));
                }
                Console.WriteLine("{0}", v[args[0]].value);
                if (v[args[0]].onChange != null)
                    v[args[0]].onChange.Invoke(this);
            }
        }

        [SpectraCommand("mkbuffer", 1, 2, "mkbuffer [name: String!] [size: Int?]")]
        public void MakeBuffer(string[] args) {
            lock (Lock) {
                if (v.ContainsKey(args[0])) {
                    Console.WriteLine("Buffer {0} already exists.", args[0]);
                } else {
                    if (args.Length == 1) {
                        makeBuffer(args[0], v["DISPLAY_LED_COUNT"].i);
                    } else {
                        makeBuffer(args[0], int.Parse(args[1]));
                    }
                }
            }
        }

        [SpectraCommand("listbuffers", 0, 0, "listbuffers")]
        public void ListBuffers(string[] args) {
            lock (Lock) {
                foreach (String name in buffers.Keys) {
                    Console.WriteLine("{0}[{1}]", name, buffers[name].Length);
                }
            }
        }

        [SpectraCommand("delbuffer", 1, 1, "delbuffer [name: String!]")]
        public void DeleteBuffer(string[] args) {
            lock (Lock) {
                if (Array.IndexOf(displayBuffers, args[0]) == -1) {
                    buffers.Remove(args[0]);
                } else {
                    Console.WriteLine("Spectra LITERALLY Can't Even Without the {0} Buffer.", args[0]);
                }
            }
        }

        [SpectraCommand("addproc", 1, 1000, "addproc [target: String!] [processor: String!] [procArg0] ... [procArgN]")]
        public void AddProcessor(string[] args) {
            Variant[] procArgs = new Variant[args.Length - 2];

            for (int i = 2; i < args.Length; i++) {
                if (args[i].StartsWith("$")) {
                    procArgs[i - 2] = v[args[i].Substring(1)];
                } else {
                    procArgs[i - 2] = Variant.Parse(args[i]);
                }
            }

            try {
                Assembly a = typeof(Spectra).Assembly;
                Processor p = (Processor)a.CreateInstance("Spectra.source." + args[1], true, BindingFlags.Default, null, new object[] { args[0], procArgs }, null, null);

                if (p == null) {
                    throw new Exception();
                }

                lock (Lock) {
                    procs.Add(p);
                }
            } catch (Exception e) {
                Console.WriteLine("Failed to Instantiate Processor {0} with the given arguments.", args[1]);

                throw;
            }
        }

        [SpectraCommand("addproci", 1, 1000, "addproci [index: Int!] [target: String!] [processor: String!] [procArg0] ... [procArgN]")]
        public void AddProcessorIndex(string[] args) {
            Variant[] procArgs = new Variant[args.Length - 3];

            for (int i = 3; i < args.Length; i++) {
                if (args[i].StartsWith("$")) {
                    procArgs[i - 3] = v[args[i].Substring(1)];
                } else {
                    procArgs[i - 3] = Variant.Parse(args[i]);
                }
            }

            try {
                Assembly a = typeof(Spectra).Assembly;
                Processor p = (Processor)a.CreateInstance("Spectra.source." + args[2], true, BindingFlags.Default, null, new object[] { args[1], procArgs }, null, null);

                if (p == null) {
                    throw new Exception();
                }

                lock (Lock) {
                    procs.Insert(int.Parse(args[0]), p);
                }
            } catch (Exception e) {
                Console.WriteLine("Failed to Instantiate Processor {0} with the given arguments.", args[2]);

                throw;
            }
        }

        [SpectraCommand("delproci", 1, 1, "delproci [index: Int!]")]
        public void DeleteProcessorIndex(string[] args) {
            lock (Lock) {
                try {
                    procs.RemoveAt(int.Parse(args[0]));
                } catch (Exception e) {
                    Console.WriteLine("Failed to Remove Processor at Index {0}", args[0]);
                }
            }
        }

        [SpectraCommand("delprocb", 1, 1, "delprocb [target: String!]")]
        public void DeleteProcessorsForBuffer(string[] args) {
            lock (Lock) {
                procs.RemoveAll(pr => pr.target == args[0]);
            }
        }

        [SpectraCommand("listprocs", 0, 0, "listprocs")]
        public void ListProcessors(string[] args) {
            lock (Lock) {
                for (int i = 0; i < procs.Count(); i++) {
                    Console.WriteLine("{0} {1} on {2} with Variants {3}", i, procs[i].GetType().Name, procs[i].target, procs[i].varsAsString());
                }
            }
        }

        [SpectraCommand("run", 1, 1000, "run [filepath: String!] [scriptArg0] ... [scriptArgN]")]
        public void Run(string[] args) {
            if (File.Exists(args[0])) {
                lock (Lock) {
                    for (int i = 1; i < args.Length; i++) {
                        if (v.ContainsKey("ARG" + (i - 1))) {
                            v["ARG" + (i - 1)] = Variant.Parse(args[i]);
                        } else {
                            v.Add("ARG" + (i - 1), Variant.Parse(args[i]));
                        }
                        Console.WriteLine("ARG{0} = {1}", i - 1, args[i]);
                    }
                }

                foreach (string line in File.ReadAllLines(args[0])) {
                    if (line.Length == 0) continue;
                    if (line.StartsWith("#")) continue;

                    Console.WriteLine(line);

                    try {
                        executeCommand(line);
                    } catch (Exception e) {
                        Console.WriteLine("Script Failed to Run. Clearing and Halting.");
                        Clear(null);
                    }
                }
            } else {
                Console.WriteLine("Could not find file {0}.", args[0]);
            }
        }

        [SpectraCommand("quit", 0, 0, "Quit: quit")]
        public void Quit(String[] args) {
            Clear(null);

            Thread.Sleep(100);

            running = false;
        }

        private void executeCommand(string line) {
            line = Regex.Replace(line, @"""[^""]+""", m => m.Value.Replace(" ", "")).Replace("\"", "");
            var parts = line.Split(' ');
            var command = parts[0];
            var args = parts.Skip(1).ToArray();

            for (int i = 0; i < args.Length; i++) {
                if (args[i].StartsWith("$ARG")) {
                    args[i] = v[args[i].Substring(1)].value.ToString();
                }
            }

            if (commands.ContainsKey(command)) {
                commands[command].Invoke(this, new object[] { args });
            } else
                Console.WriteLine("Invalid command. Execute command 'help' to see valid commands.\n");
        }

        public void RunSpectraCommandLine() {
            commands = ParseSpectraCommands();
            running = true;

            Console.WriteLine("------ Spectra Controller ------");

            Start();

            while (running) {
                Console.Write(">> ");

                try {
                    executeCommand(Console.ReadLine());
                } catch (Exception e) {
                    Console.WriteLine("An error occurred.");
                }

                Console.WriteLine();

                Thread.Sleep(50);
            }

            port.Close();
        }

        private Dictionary<String, MethodInfo> ParseSpectraCommands() {
            var commands = new Dictionary<String, MethodInfo>();
            var methods = GetType().GetMethods().Where(m => m.GetCustomAttribute<SpectraCommand>() != null).ToArray();
            foreach (var method in methods) {
                commands.Add(method.GetCustomAttribute<SpectraCommand>().command, method);
            }
            return commands;
        }

    }

}
