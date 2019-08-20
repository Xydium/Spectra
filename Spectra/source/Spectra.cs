using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

        private static readonly object Lock = new object();
        private static readonly string[] displayBuffers = { "H", "S", "V", "HB", "SB", "VB" };

        Dictionary<String, Variant> v;
        Dictionary<String, byte[]> buffers;
        List<Processor> procs;

        SerialPort port;
        Thread processor;
        bool running;

        public Spectra() {
            v = new Dictionary<string, Variant>();
            buffers = new Dictionary<string, byte[]>();
            procs = new List<Processor>();
        }

        public void Start() {
            Console.WriteLine(">> run usr/preinit");
            Run(new string[] { "usr/preinit" });
            v.Add("SPECTRA_RANDOM", new RandomVariant(0f));
            Console.WriteLine();

            port = new SerialPort(v["ARDUINO_SERIAL_PORT"].s, v["ARDUINO_BAUD_RATE"].i, Parity.None, 8, StopBits.One);
            port.Open();

            processor = new Thread(Process);
            processor.Start();
        }

        public void Process() {
            int leds = v["DISPLAY_LED_COUNT"].i;
            int frameTime = 0;

            defaultBuffers();

            Console.WriteLine("run usr/init");
            Run(new string[] { "usr/init" });
            Console.Write("\n>> ");

            while (running) {
                lock (Lock) {
                    frameTime = 1000 / v["DISPLAY_FRAME_RATE"].i;
                    float dt = (1.0f / v["DISPLAY_FRAME_RATE"].i) * v["SPECTRA_TIME_SCALE"].f;

                    v["SPECTRA_TIME"].value = (float) Math.IEEERemainder(v["SPECTRA_TIME"].f + dt, v["SPECTRA_TIME_MAX"].f);
                    v["SPECTRA_DELTA_TIME"].value = dt;
                    
                    if(v["SPECTRA_PROCESSING"].b) {
                        foreach (Processor proc in procs) {
                            proc.Process(this, v, buffers[proc.target]);
                        }
                    }

                    for(byte i = 0; i < 3; i++) {
                        byte[] buffer = buffers[displayBuffers[i]];
                        byte[] backbuffer = buffers[displayBuffers[i + 3]];

                        port.Write(new byte[] { i }, 0, 1); port.Write(buffers[displayBuffers[i]], 0, leds);
                        Array.Copy(buffer, backbuffer, leds);

                        Thread.Sleep(3);
                    }
                }

                Thread.Sleep(frameTime - 6);
            }
        }

        public byte[] GetBuffer(string name) {
            return buffers[name];
        }

        public ReadOnlyDictionary<string, byte[]> GetBuffers() {
            return new ReadOnlyDictionary<string, byte[]>(buffers);
        }

        public void makeBuffer(string name, int size) {
            buffers.Add(name, new byte[size]);
        }

        private void defaultBuffers() {
            makeBuffer("H", v["DISPLAY_LED_COUNT"].i);
            makeBuffer("HB", v["DISPLAY_LED_COUNT"].i);
            makeBuffer("S", v["DISPLAY_LED_COUNT"].i);
            makeBuffer("SB", v["DISPLAY_LED_COUNT"].i);
            makeBuffer("V", v["DISPLAY_LED_COUNT"].i);
            makeBuffer("VB", v["DISPLAY_LED_COUNT"].i);
        }

        public static void Main(string[] args) {
            var spectra = new Spectra();
            spectra.RunSpectraCommandLine();
        }

    }

}
