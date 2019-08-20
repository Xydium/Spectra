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

namespace spec
{

    class Spectra
    {
        private static readonly object Lock = new object();

        byte[] arrH, arrHbb;
        byte[] arrS, arrSbb;
        byte[] arrV, arrVbb;
        byte[] arrT;

        List<Processor> hProcs;
        List<Processor> sProcs;
        List<Processor> vProcs;

        Dictionary<String, Variant> v;
        Dictionary<String, MethodInfo> commands;

        SerialPort port;

        Thread processor;
        bool running;

        public Spectra()
        {
            hProcs = new List<Processor>();
            sProcs = new List<Processor>();
            vProcs = new List<Processor>();

            v = new Dictionary<String, Variant>();

            InitVars();
        }

        public void Start()
        {
            Console.WriteLine(">> run usr/preinit");
            Run(new string[] { "usr/preinit" });

            port = new SerialPort(v["ARDUINO_SERIAL_PORT"].s, v["ARDUINO_BAUD_RATE"].i, Parity.None, 8, StopBits.One);
            port.Open();

            processor = new Thread(Process);
            processor.Start();
        }

        private void Process()
        {
            int leds = v["DISPLAY_LED_COUNT"].i;

            arrH = new byte[leds];
            arrHbb = new byte[leds];
            arrS = new byte[leds];
            arrSbb = new byte[leds];
            arrV = new byte[leds];
            arrVbb = new byte[leds];
            arrT = new byte[leds];

            int frameTime = 0;

            Console.WriteLine("run usr/init");
            Run(new string[] { "usr/init"});
            Console.Write(">> ");

            while (running)
            {
                lock(Lock)
                {
                    frameTime = 1000 / v["DISPLAY_FRAME_RATE"].i;
                    float dt = (1.0f / v["DISPLAY_FRAME_RATE"].i) * v["SPECTRA_TIME_SCALE"].f;

                    v["SPECTRA_TIME"].value = (float) Math.IEEERemainder(v["SPECTRA_TIME"].f + dt, v["SPECTRA_TIME_MAX"].f);
                    v["SPECTRA_DELTA_TIME"].value = dt;

                    var stages = CreateComputeOrder();

                    foreach (ComputeStage cs in stages)
                    {
                        foreach (Processor proc in cs.processors)
                        {
                            proc.Process(this, v, cs.buffer, cs.backbuffer, arrT);
                        }
                    }

                    port.Write(new byte[] { 0 }, 0, 1); port.Write(arrH, 0, leds);
                    Thread.Sleep(3);
                    port.Write(new byte[] { 1 }, 0, 1);  port.Write(arrS, 0, leds);
                    Thread.Sleep(3);
                    port.Write(new byte[] { 2 }, 0, 1); port.Write(arrV, 0, leds);

                    Array.Copy(arrH, arrHbb, arrH.Length);
                    Array.Copy(arrS, arrSbb, arrS.Length);
                    Array.Copy(arrV, arrVbb, arrV.Length);
                }
                
                Thread.Sleep(frameTime - 6);
            }
        }

        private ComputeStage[] CreateComputeOrder()
        {
            char[] order = v["SPECTRA_COMPUTE_ORDER"].s.ToUpper().ToCharArray();
            ComputeStage[] stages = new ComputeStage[3];

            for(int i = 0; i < stages.Length; i++)
            {
                if (order[i] == 'H') stages[i] = new ComputeStage(hProcs, arrH, arrHbb);
                else if (order[i] == 'S') stages[i] = new ComputeStage(sProcs, arrS, arrSbb);
                else if (order[i] == 'V') stages[i] = new ComputeStage(vProcs, arrV, arrVbb);
            }

            return stages;
        }

        public byte[] BufferForName(string name)
        {
            switch(name)
            {
                case "H":
                    return arrH;
                case "HBB":
                    return arrHbb;
                case "S":
                    return arrS;
                case "SBB":
                    return arrSbb;
                case "V":
                    return arrV;
                case "VBB":
                    return arrVbb;
                case "T":
                    return arrT;
                default:
                    return null;
            }
        }

        /*
         * Commands:
         * Clear -- deletes all generators, processors, and wipes the three buffers/backbuffers
         * Quit -- Closes Spectra
         * Add Processor [Target] [Classname] [Arguments]
         * Get Var [Var Name]
         * Set Var [Var Name] [New Value]
         * Describe [Gen name]
         * Run [filename] //clears by default before loading...resets vartable?
         */

        private void InitVars()
        {
            //ARDUINO
            v.Add("ARDUINO_SERIAL_PORT", new Variant("COM3"));
            v.Add("ARDUINO_BAUD_RATE", new Variant(230400));

            //AUDIO
            v.Add("AUDIO_SAMPLE_RATE", new Variant(48000));
            v.Add("AUDIO_SAMPLE_COUNT", new Variant(1 << 13));
            v.Add("AUDIO_SAMPLE_GAIN", new Variant(1000));
            v.Add("AUDIO_BITS_PER_SAMPLE", new Variant(32));
            v.Add("AUDIO_BUFFER_SIZE", new Variant(20 * v["AUDIO_SAMPLE_RATE"].i * v["AUDIO_BITS_PER_SAMPLE"].i / 4));
            v.Add("AUDIO_IS_RECORDING", new Variant(false)); //onchange start/stop 

            //FOURIER
            v.Add("FOURIER_MIN_FREQUENCY", new Variant(20));
            v.Add("FOURIER_MAX_FREQUENCY", new Variant(16000));
            v.Add("FOURIER_LOG_MAGNITUDE", new Variant(false));

            //WAVEFORM

            //PERLIN

            //RIPPLE-RAIN

            //DISPLAY
            v.Add("DISPLAY_FRAME_RATE", new Variant(60));
            v.Add("DISPLAY_LED_COUNT", new Variant(60));

            //SPECTRA
            v.Add("SPECTRA_COMPUTE_ORDER", new Variant("VSH"));
            v.Add("SPECTRA_TIME", new Variant(0f));
            v.Add("SPECTRA_TIME_SCALE", new Variant(1f));
            v.Add("SPECTRA_DELTA_TIME", new Variant(1f));
            v.Add("SPECTRA_TIME_MAX", new Variant(24 * 60 * 60f));

            //SCRIPT
            for(int i = 0; i < 20; i++)
            {
                v.Add("ARG" + i, new Variant(0));
            }
        }

        [SpectraCommand("help", 0, 1, "help [command: String?]")]
        public void Help(String[] args)
        {
            Console.WriteLine("Spectra Help (? = Optional, ! = Required):\n");
            switch (args.Length)
            {
                case 0:
                    Array.ForEach(commands.Values.ToArray(), (m) => Console.WriteLine(m.GetCustomAttribute<SpectraCommand>().usage + '\n'));
                    break;
                case 1:
                    if (commands.ContainsKey(args[0]))
                    {
                        Console.WriteLine(commands[args[0]].GetCustomAttribute<SpectraCommand>().usage + '\n');
                        break;
                    }
                    else
                    {
                        Console.WriteLine("Cannot invoke help on invalid command name.");
                        break;
                    }
                default:
                    Console.WriteLine("Invalid use of command 'help'.\n");
                    break;
            }
        }

        [SpectraCommand("addproc", 1, 1000, "addproc [target: String! in H,S,V] [processor: String!] [procArg0] ... [procArgN]")]
        public void AddProcessor(string[] args)
        {
            List<Processor> target = null;
            if (args[0] == "H") target = hProcs;
            else if (args[0] == "S") target = sProcs;
            else if (args[0] == "V") target = vProcs;
            else
            {
                Console.WriteLine("Invalid Target.");
                return;
            }

            Variant[] procArgs = new Variant[args.Length - 2];

            for (int i = 2; i < args.Length; i++)
            {
                if(args[i].StartsWith("$"))
                {
                    procArgs[i - 2] = v[args[i].Substring(1)];
                }
                else
                {
                    procArgs[i - 2] = Variant.Parse(args[i]);
                }
            }

            try
            {
                Assembly a = typeof(Spectra).Assembly;
                Processor p = (Processor)a.CreateInstance("spec." + args[1], true, BindingFlags.Default, null, new object[] { procArgs }, null, null);

                if(p == null)
                {
                    throw new Exception();
                }

                lock (Lock)
                {
                    target.Add(p);
                }
            } catch(Exception e)
            {
                Console.WriteLine("Failed to Instantiate Processor {0} with the given arguments.", args[1]);

                throw;
            }
        }

        [SpectraCommand("describe", 1, 1, "describe [processor: String!]")]
        public void Describe(string[] args)
        {
            try
            {
                Assembly a = typeof(Spectra).Assembly;
                Processor p = (Processor)a.CreateInstance("spec." + args[0], true, BindingFlags.Default, null, new object[] { null }, null, null);
                Console.WriteLine("Processor: {0}", args[0]);
                Console.WriteLine("\tOperation: {0}", p.Description());
                Console.WriteLine("\tParamaters: {0}", p.Parameters());
            } catch(Exception e)
            {
                Console.WriteLine("Processor does not exist.");
            }
        }

        [SpectraCommand("getvar", 1, 1, "getvar [var: String!]")]
        public void GetVar(string[] args)
        {
            lock(Lock)
            {
                if (v.ContainsKey(args[0]))
                {
                    Console.WriteLine("{0} = {1}", args[0], v[args[0]].value);
                }
                else
                {
                    Console.WriteLine("Variable does not exist.");
                }
            }
        }

        [SpectraCommand("setvar", 2, 2, "setvar [var: String!] [value: Any!]")]
        public void SetVar(string[] args)
        {
            lock(Lock)
            {
                if(v.ContainsKey(args[0]))
                {
                    Console.Write("{0}: {1} -> ", args[0], v[args[0]].value);
                    v[args[0]].value = Variant.Parse(args[1]).value;
                } else
                {
                    Console.Write("{0}: null -> ", args[0]);
                    v.Add(args[0], Variant.Parse(args[1]));
                }
                Console.WriteLine("{0}", v[args[0]].value);
                if (v[args[0]].onChange != null)
                    v[args[0]].onChange.Invoke(this);
            }
        }

        [SpectraCommand("readback", 0, 0, "readback")]
        public void ReadBack(string[] args)
        {
            lock(Lock)
            {
                Console.WriteLine(port.ReadExisting());
            }
        }

        [SpectraCommand("clear", 0, 1, "clear [channel: String? in H,S,V]")]
        public void Clear(string[] args)
        {
            lock(Lock)
            {
                if(args == null || args.Length == 0 || args[0] == "H") hProcs.Clear();
                if(args == null || args.Length == 0 || args[0] == "S") sProcs.Clear();
                if(args == null || args.Length == 0 || args[0] == "V") vProcs.Clear();

                for(int i = 0; i < arrH.Length; i++)
                {
                    if (args == null || args.Length == 0 || args[0] == "H") { arrH[i] = 0; arrHbb[i] = 0; }
                    if (args == null || args.Length == 0 || args[0] == "S") { arrS[i] = 0; arrSbb[i] = 0; }
                    if (args == null || args.Length == 0 || args[0] == "V") { arrV[i] = 0; arrVbb[i] = 0; }
                    if (args == null || args.Length == 0) arrT[i] = 0;
                }
            }
        }

        [SpectraCommand("run", 1, 1000, "run [filepath: String!] [scriptArg0] ... [scriptArgN]")]
        public void Run(string[] args)
        {
            if(File.Exists(args[0]))
            {
                //Clear(null);

                lock (Lock) {
                    for (int i = 1; i < args.Length; i++)
                    {
                        v["ARG" + (i - 1)] = Variant.Parse(args[i]);
                        Console.WriteLine("ARG{0} = {1}", i - 1, args[i]);
                    }
                }

                foreach(string line in File.ReadAllLines(args[0]))
                {
                    if (line.Length == 0) continue;
                    if (line.StartsWith("#")) continue;

                    Console.WriteLine(line);

                    try
                    {
                        executeCommand(line);
                    } catch(Exception e)
                    {
                        Console.WriteLine("Script Failed to Run. Clearing and Halting.");
                        Clear(null);
                    }
                }

                Console.WriteLine();
            }
            else
            {
                Console.WriteLine("Could not find file {0}.", args[0]);
            }
        }

        [SpectraCommand("quit", 0, 0, "Quit: quit")]
        public void Quit(String[] args)
        {
            Clear(null);

            Thread.Sleep(100);

            running = false;
        }

        private void executeCommand(string line)
        {
            var parts = line.Split(' ');
            var command = parts[0];
            var args = parts.Skip(1).ToArray();

            for(int i = 0; i < args.Length; i++)
            {
                //Substitute in script args, we don't want those to change
                //Non-script variables should be passed as variants
                if(args[i].StartsWith("$ARG"))
                {
                    args[i] = v[args[i].Substring(1)].value.ToString();
                }
            }

            if (commands.ContainsKey(command))
            {
                commands[command].Invoke(this, new object[] { args });
            }
            else
                Console.WriteLine("Invalid command. Execute command 'help' to see valid commands.\n");
        }

        public void RunSpectraCommandLine()
        {
            commands = ParseSpectraCommands();
            running = true;

            Console.WriteLine("------ Spectra Controller ------");

            Start();

            while (running)
            {
                Console.Write(">> ");

                try
                {
                    executeCommand(Console.ReadLine());
                } catch (Exception e)
                {
                    Console.WriteLine("An error occurred.");
                }

                Thread.Sleep(50);
            }

            port.Close();
        }

        private Dictionary<String, MethodInfo> ParseSpectraCommands()
        {
            var commands = new Dictionary<String, MethodInfo>();
            var methods = GetType().GetMethods().Where(m => m.GetCustomAttribute<SpectraCommand>() != null).ToArray();
            foreach (var method in methods)
            {
                commands.Add(method.GetCustomAttribute<SpectraCommand>().command, method);
            }
            return commands;
        }

        //public static void Main(string[] args)
        //{
        //    var spectra = new Spectra();
        //    spectra.RunSpectraCommandLine();
        //}
    }

    class Variant
    {
        public Object value;
        public Action<Spectra> onChange;

        public Variant(Object value, Action<Spectra> onChange = null)
        {
            this.value = value;
            this.onChange = onChange;
        }

        public int i
        {
            get {
                if (value is float)
                {
                    return Convert.ToInt32(value);
                } else
                {
                    return (int)value;
                }
            }
        }

        public float f
        {
            get {
                if (value is int)
                {
                    return (int)(value);
                }
                else
                {
                    return (float)value;
                }
            }
        }

        public String s
        {
            get { return (String)value; }
        }

        public bool b
        {
            get { return (bool)value; }
        }

        private static readonly Regex ints = new Regex(@"^-?[0-9]+$");
        private static readonly Regex floats = new Regex(@"^-?[0-9][0-9,\.]+$");
        private static readonly Regex bools = new Regex(@"true|false", RegexOptions.IgnoreCase);

        public static Variant Parse(String value)
        {
            if (ints.IsMatch(value))
            {
                return new Variant(int.Parse(value));
            }
            else if (floats.IsMatch(value))
            {
                return new Variant(float.Parse(value));
            }
            else if(bools.IsMatch(value))
            {
                return new Variant(bool.Parse(value));
            }
            else
            {
                return new Variant(value);
            }
        }
    }

    abstract class Processor
    {
        public Processor(Variant[] args) { }

        abstract public void Process(Spectra s, Dictionary<String, Variant> v, byte[] b, byte[] bb, byte[] t);
        abstract public String Description();
        abstract public String Parameters();
    }

    class ConstantGen : Processor
    {
        private Variant constant;

        public ConstantGen(Variant[] args) : base(args)
        {
            if (args == null) return;
            this.constant = args[0];
        }

        override public void Process(Spectra s, Dictionary<String, Variant> v, byte[] b, byte[] bb, byte[] t)
        {
            for(int i = 0; i < b.Length; i++)
            {
                b[i] = (byte) constant.i;
            }
        }

        override public String Description()
        {
            return "f(x, t) = k";
        }

        override public String Parameters() {
            return "[constant: int 0-255]";
        }
    }

    class LinearGen : Processor
    {
        private Variant minValue;
        private Variant maxValue;

        public LinearGen(Variant[] args) : base(args) {
            if (args == null) return;
            this.minValue = args[0];
            this.maxValue = args[1];
        }

        override public void Process(Spectra s, Dictionary<String, Variant> v, byte[] b, byte[] bb, byte[] t)
        {
            float dvdx = (maxValue.i - minValue.i) / (float) (b.Length - 1);

            for(int i = 0; i < b.Length; i++)
            {
                b[i] = (byte) (minValue.i + dvdx * i);
            }
        }

        override public String Description()
        {
            return "f(x, t) = ax + b";
        }

        override public String Parameters()
        {
            return "[start: int 0-255] [end: int 0-255]";
        }
    }

    class ConstantSineGen : Processor
    {
        private Variant period;
        private Variant minimum;
        private Variant maximum;

        public ConstantSineGen(Variant[] args) : base(args)
        {
            if (args == null) return;
            this.period = args[0];
            this.minimum = args[1];
            this.maximum = args[2];
        }

        override public void Process(Spectra s, Dictionary<String, Variant> v, byte[] b, byte[] bb, byte[] t)
        {
            float midpoint = (maximum.i + minimum.i) / 2f;
            float halfdiff = (maximum.i - minimum.i) / 2f;

            for (int i = 0; i < b.Length; i++)
            {
                b[i] = (byte) (halfdiff * Math.Sin(6.28 / period.f * v["SPECTRA_TIME"].f) + midpoint);
            }
        }

        override public String Description()
        {
            return "f(x, t) = a * sin(b * t) + c";
        }

        override public String Parameters()
        {
            return "[period: float!] [min: int! 0-255] [max: int! 0-255]";
        }
    }

    class LinearSineGen : Processor
    {
        private float period;
        private float speed;
        private int minimum;
        private int maximum;
        private float phase;

        public LinearSineGen(Variant[] args) : base(args)
        {
            if (args == null) return;
            this.period = args[0].f;
            this.speed = args[1].f;
            this.minimum = args[2].i;
            this.maximum = args[3].i;
            this.phase = args[4].f;
        }

        override public void Process(Spectra s, Dictionary<String, Variant> v, byte[] b, byte[] bb, byte[] t)
        {
            float midpoint = (maximum + minimum) / 2f;
            float halfdiff = (maximum - minimum) / 2f;

            for (int i = 0; i < b.Length; i++)
            {
                b[i] = (byte)(halfdiff * Math.Sin((6.28 / period) * i + speed * v["SPECTRA_TIME"].f + phase) + midpoint);
            }
        }

        override public String Description()
        {
            return "f(x, t) = a * sin(b * x + c * t + k) + d";
        }

        override public String Parameters()
        {
            return "[period: float!] [speed: float!] [min: int! 0-255] [max: int! 0-255] [phase: float!]";
        }
    }

    //Class RandomValueGen

    class RandomIndexGen : Processor {
        private float probability;
        private int value;

        private Random r;

        public RandomIndexGen(Variant[] args) : base(args) {
            if (args == null) return;
            this.probability = args[0].f;
            this.value = args[1].i;
            this.r = new Random();
        }

        override public void Process(Spectra s, Dictionary<String, Variant> v, byte[] b, byte[] bb, byte[] t) {
            for (int i = 0; i < b.Length; i++) {
                if (r.NextDouble() < probability) {
                    b[i] = (byte)value;
                }
            }
        }

        override public String Description() {
            return "f(random(x), t) = k";
        }

        override public String Parameters() {
            return "[probability: float!] [value: int!]";
        }
    }

    class PixelScrollProc : Processor
    {
        private float scroll;
        public float scrollRate;

        public PixelScrollProc(Variant[] args) : base(args)
        {
            if (args == null) return;
            this.scroll = 0;
            this.scrollRate = args[0].f;
        }

        override public void Process(Spectra s, Dictionary<String, Variant> v, byte[] b, byte[] bb, byte[] t)
        {
            scroll = v["SPECTRA_TIME"].f * scrollRate;

            for (int i = 0; i < b.Length; i++)
            {
                t[i] = b[Utils.nfmod((int) (i + scroll), b.Length)];
            }

            for (int i = 0; i < b.Length; i++)
            {
                b[i] = t[i];
            }
        }

        override public String Description()
        {
            return "f(x, t) = f(wrap(x + t * rate), t)";
        }

        override public String Parameters()
        {
            return "[rate: float!]";
        }
    }

    class BlurProc : Processor
    {
        private float factor;
        private int passes;

        public BlurProc(Variant[] args) : base(args)
        {
            if (args == null) return;
            this.factor = args[0].f;
            this.passes = args[1].i;
        }

        override public void Process(Spectra s, Dictionary<String, Variant> v, byte[] b, byte[] bb, byte[] t)
        {
            for(int p = 0; p < passes; p++)
            {
                t[0] = (byte) ((b[0] + b[1] * factor) / (1 + factor));
                t[b.Length - 1] = (byte)((b[b.Length - 1] + b[b.Length - 2] * factor) / (1 + factor));

                for(int i = 1; i < b.Length - 1; i++)
                {
                    t[i] = (byte)((b[i] + b[i - 1] * factor + b[i + 1] * factor) / (1 + 2 * factor));
                }

                Array.Copy(t, b, b.Length);
            }
        }

        override public String Description()
        {
            return "f(x, t) = mean(f(x-k...x+k, t))";
        }

        override public String Parameters()
        {
            return "[factor: float!] [passes: int!]";
        }
    }

    class RippleProc : Processor {
        private float momentum;
        private float speed;
        private float decay;
        private float[] heights;
        private float[] gradients;

        public RippleProc(Variant[] args) : base(args) {
            if (args == null) return;
            this.momentum = args[0].f;
            this.speed = args[1].f;
            this.decay = args[2].f;
        }

        override public void Process(Spectra s, Dictionary<String, Variant> v, byte[] b, byte[] bb, byte[] t) {
            if (gradients == null) {
                heights = new float[b.Length];
                gradients = new float[b.Length];
            }

            for (int i = 1; i < b.Length - 1; i++) {
                if (b[i] == 255) {
                    heights[i] = 255;
                }
            }

            gradients[0] = gradients[0] * momentum + (b[1] - b[0]) * (1 - momentum);
            gradients[b.Length - 1] = gradients[b.Length - 1] * momentum + (b[b.Length - 2] - b[b.Length - 1]) * (1 - momentum);
            for (int i = 1; i < b.Length - 1; i++) {
                gradients[i] = gradients[i] * momentum + ((b[i - 1] - b[i]) * (1 - momentum) + (b[i + 1] - b[i]) * (1 - momentum)) * 0.5f;
            }

            float dt = v["SPECTRA_DELTA_TIME"].f;

            for (int i = 0; i < b.Length; i++) {
                heights[i] = (heights[i] + gradients[i] * speed * dt);
                b[i] = (byte)heights[i];
                heights[i] *= decay;
                gradients[i] *= decay;
            }

            //Array.ForEach(gradients, f => Console.Write("{0} ", (byte)f));
            //Console.WriteLine();
        }

        override public String Description() {
            return "f(x, t) = lol idk";
        }

        override public String Parameters() {
            return "[momentum: float!] [speed: float!] [decay: float!]";
        }
    }

    class DecayProc : Processor
    {
        private float factor;

        public DecayProc(Variant[] args) : base(args)
        {
            if (args == null) return;
            this.factor = args[0].f;
        }

        override public void Process(Spectra s, Dictionary<String, Variant> v, byte[] b, byte[] bb, byte[] t)
        {
            for (int i = 0; i < b.Length; i++)
            {
                b[i] = (byte) (b[i] * factor);
            }
        }

        override public String Description()
        {
            return "f(x, t) = f(x, t - 1) * factor";
        }

        override public String Parameters()
        {
            return "[factor: float!]";
        }
    }

    class CopyToProc : Processor
    {
        private string target;

        public CopyToProc(Variant[] args) : base(args)
        {
            if (args == null) return;
            this.target = args[0].s;
        }

        override public void Process(Spectra s, Dictionary<String, Variant> v, byte[] b, byte[] bb, byte[] t)
        {
            var copy = s.BufferForName(target);

            for (int i = 0; i < b.Length; i++)
            {
                copy[i] = b[i];
            }
        }

        override public String Description()
        {
            return "f(x, t) = these dont matter";
        }

        override public String Parameters()
        {
            return "[target: String!]";
        }
    }

    class CopyFromProc : Processor
    {
        private string target;

        public CopyFromProc(Variant[] args) : base(args)
        {
            if (args == null) return;
            this.target = args[0].s;
        }

        override public void Process(Spectra s, Dictionary<String, Variant> v, byte[] b, byte[] bb, byte[] t)
        {
            var copy = s.BufferForName(target);

            for (int i = 0; i < b.Length; i++)
            {
                b[i] = copy[i];
            }
        }

        override public String Description()
        {
            return "f(x, t) = these dont matter";
        }

        override public String Parameters()
        {
            return "[target: String!]";
        }
    }

    //CopyFromIfProc
    //CopyToIfProc

    class InvertProc : Processor
    {
        public InvertProc(Variant[] args) : base(args)
        {
            if (args == null) return;
        }

        override public void Process(Spectra s, Dictionary<String, Variant> v, byte[] b, byte[] bb, byte[] t)
        {
            for (int i = 0; i < b.Length; i++)
            {
                b[i] = (byte) (255 - b[i]);
            }
        }

        override public String Description()
        {
            return "f(x, t) = these dont matter";
        }

        override public String Parameters()
        {
            return "none";
        }
    }

    class AddConstantProc : Processor
    {
        private Variant constant;
        private Variant wrap;

        public AddConstantProc(Variant[] args) : base(args)
        {
            if (args == null) return;
            this.constant = args[0];
            this.wrap = args[1];
        }

        override public void Process(Spectra s, Dictionary<String, Variant> v, byte[] b, byte[] bb, byte[] t)
        {
            for (int i = 0; i < b.Length; i++)
            {
                if(wrap.b)
                {
                    b[i] = (byte)(b[i] + constant.i);
                } else
                {
                    b[i] = Utils.limit(b[i] + constant.i);
                }
            }
        }

        override public String Description()
        {
            return "f(x, t) = these dont matter";
        }

        override public String Parameters()
        {
            return "none";
        }
    }

    class RangeProc : Processor
    {
        private Variant minimum;
        private Variant maximum;

        public RangeProc(Variant[] args) : base(args)
        {
            if (args == null) return;
            this.minimum = args[0];
            this.maximum = args[1];
        }

        override public void Process(Spectra s, Dictionary<String, Variant> v, byte[] b, byte[] bb, byte[] t)
        {
            for (int i = 0; i < b.Length; i++)
            {
                b[i] = (byte) (minimum.i + (maximum.i - minimum.i) * (b[i] / 255f));
            }
        }

        override public String Description()
        {
            return "f(x, t) = these dont matter";
        }

        override public String Parameters()
        {
            return "none";
        }
    }

    class ReflectProc : Processor
    {
        public ReflectProc(Variant[] args) : base(args)
        {
            if (args == null) return;
        }

        override public void Process(Spectra s, Dictionary<String, Variant> v, byte[] b, byte[] bb, byte[] t)
        {
            for(int i = 0; i < b.Length / 2; i++)
            {
                b[b.Length - i - 1] = b[i];
            }
        }

        override public String Description()
        {
            return "f(x, t) = these dont matter";
        }

        override public String Parameters()
        {
            return "none";
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

    class ComputeStage
    {
        public List<Processor> processors;
        public byte[] buffer;
        public byte[] backbuffer;

        public ComputeStage(List<Processor> processors, byte[] buffer, byte[] backbuffer)
        {
            this.processors = processors;
            this.buffer = buffer;
            this.backbuffer = backbuffer;
        }
    }

    class Utils
    {
        public static int nfmod(int a, int b)
        {
            return (int) (a - b * Math.Floor((float) a / b));
        }

        public static byte limit(int a)
        {
            return (byte)Math.Min(Math.Max(a, 0), 255);
        }
    }

}

/*
 * So....
 * 
 * Spectra will have different HVS functions, Generator Functions
 * Stream at 180Bps, so 37.5% of each frame is just transmitting
 * Can load script files which set parameters
 * Parameters will be in a vartable
 * 
 * Variables include:
 * Pre and post gain
 * Frosting
 * Rotation speed
 * Scales
 * 
 * Streaming is continuous, eliminates sync issues
 * No commands sent to the arduino, can just display raw bytes
 * 
 * Types of generators:
 * f(x) = k DONE
 * f(x) = x DONE
 * f(x, t) = fourierBuckets(x)
 * f(x, t) = waveform(x, t)
 * f(x, t) = bb(x - 1, t) ?? would this even work? perhaps DONE
 * f = perlin... each function needs its own generator tho
 * f(x, t) = sin(t) DONE
 * f(x, t) = sin(x +/- t)
 * f(x, t) = random
 * 
 * but then each function needs a vartable for itself right? or at least separate vartable entries
 * 
 * post processes:
 * blending
 * scaling
 * ceiling
 * flooring
 * addconstant
 * addbuffer
 * multiplybyconstant
 * multiplybybuffer
 * copybuffer
 * backbuffer averaging
 * reflect around center
 * reverse
 * invert
 * pixel scrolling
 * smooth scrolling
 * apply palette, will also be 
 * to backbuffer and from backbuffer
 * 
 * PostProcesses are specially labeled generators
 * 
 * 
 * Rain:
 *  Occasionally set pixels to full brightness RippleSpawnGen
 *  Values that are zero become 80% of their neighbors RippleSpreadProc
 *  Values that are nonzero decay rapidly RippleDecayProc
 *  Hues change by their gradient to their neighbors times the values of their neighbors SmoothGradientProc
 *  Needs a way to set hues where ripples just started, need an IfBufferEquals proc CopyIfInRangeProc from bb to b if o in 0 to 254
 *      So, if V < 255, Restore Backbuffer
 *      
 *  Value buffer randomly set to 255 at some indexes
 *  Hue buffer set to random values across the board
 *  All values in columns not at max value are reverted to the back buffer
 *  Ripple spread rules are applied to Value and Hue buffers
 *  
 *  Why does Ripple not work correctly the first time? What changes when run again?
 *  
 *  Command to query procs
 *  
 *  Create scripts with defaults function to fill in values for script variables
 *  
 *  TODO:
 *  Remove compute order, do indexed procs linearly DONE
 *      listprocs, addproci, delproci               DONE
 *  Custom buffers on demand                        DONE
 *  Toggle processing with var                      DONE
 *  Local script variables                          EHHH
 *  Bake script arguments                           
 *  Variable update rule w/ C# syntax expressions
 *  ReplaceRangeProc
 *  Random Variant accessible from var table, generates new number any type on each access DONE
 *  CACHING PROCESSOR SEQUENCES
 *  
 *  For the lols, use the new system to do fibonacci
 *  Single value buffers which update variables which control single index setting
 */
