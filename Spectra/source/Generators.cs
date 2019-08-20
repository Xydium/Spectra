using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Spectra.source {

    class ConstantGen : Processor {

        private static readonly int CONSTANT = 0;

        public ConstantGen(string target, Variant[] args) : base(target, args) {
            description = "Sets the entire target buffer to [value].";
            usage = "addproc [target: String!] ConstantGen [value: Int!]";
        }

        override public void Process(Spectra s, Dictionary<String, Variant> v, byte[] b) {
            byte value = (byte)args[CONSTANT].i;

            for (int i = 0; i < b.Length; i++) {
                b[i] = value;
            }
        }

    }

    class LinearGen : Processor {

        private static readonly int START = 0, END = 1;

        public LinearGen(string target, Variant[] args) : base(target, args) {
            description = "Linearly interpolates from [start] to [end] along the buffer.";
            usage = "addproc [target: String!] LinearGen [start: Int!] [end: Int!]";
        }

        override public void Process(Spectra s, Dictionary<String, Variant> v, byte[] b) {
            float dvdx = (args[END].i - args[START].i) / (float)(b.Length - 1);

            for (int i = 0; i < b.Length; i++) {
                b[i] = (byte)(args[START].i + dvdx * i);
            }
        }

    }

    class RandomIndexGen : Processor {
        private Random r;

        public RandomIndexGen(string target, Variant[] args) : base(target, args) {
            description = "Randomly sets indices in the buffer to [value]";
            usage = "addproc [target: String!] RandomIndexGen [probability: float!] [value: int!]";
            r = new Random();
        }

        override public void Process(Spectra s, Dictionary<String, Variant> v, byte[] b) {
            for (int i = 0; i < b.Length; i++) {
                if (r.NextDouble() < args[0].f) {
                    b[i] = (byte)args[1].i;
                }
            }
        }
    }

}
