using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq.Expressions;
using System.Linq.Dynamic;
using System.Text;
using System.Threading.Tasks;

namespace Spectra.source {

    abstract class Processor {

        public readonly string target;
        public readonly Variant[] args;
        public string description;
        public string usage;

        public Processor(string target, Variant[] args) {
            if (args == null) return;
            this.target = target;
            this.args = args;
        }

        abstract public void Process(Spectra s, Dictionary<String, Variant> v, byte[] b);

        public string varsAsString() {
            string result = "";

            foreach (Variant v in args) {
                result += v.value.ToString() + "  ";
            }

            return result;
        }

    }

    class OperatorProc : Processor {
        private Dictionary<String, Func<float, float, float>> funcs;

        public OperatorProc(string target, Variant[] args) : base(target, args) {
            description = "Applies the [operator] and [operand]. Faster than ExpressionProc for basic operations.";
            usage = "addproc [target: String!] OperatorProc [operator: String! in +,-,*,/,^,%] [operand: float!]";

            funcs = new Dictionary<string, Func<float, float, float>>();

            funcs.Add("+", (x1, x2) => x1 + x2);
            funcs.Add("-", (x1, x2) => x1 - x2);
            funcs.Add("*", (x1, x2) => x1 * x2);
            funcs.Add("/", (x1, x2) => x1 / x2);
            funcs.Add("^", (x1, x2) => (float) Math.Pow(x1, x2));
            funcs.Add("%", (x1, x2) => (float) Math.IEEERemainder(x1, x2));
        }

        override public void Process(Spectra s, Dictionary<String, Variant> v, byte[] b) {
            var opt = args[0].s;
            var opn = args[1].f;

            var func = funcs[opt];

            for (int i = 0; i < b.Length; i++) {
                b[i] = (byte) Convert.ToInt32(func((float) b[i], opn));
            }
        }

    }

    class CopyProc : Processor {
        public CopyProc(string target, Variant[] args) : base(target, args) {
            description = "Copies the values from [bufferName].";
            usage = "addproc [target: String!] CopyProc [bufferName: String!]";
        }

        override public void Process(Spectra s, Dictionary<String, Variant> v, byte[] b) {
            var copy = s.GetBuffer(args[0].s);

            for (int i = 0; i < b.Length; i++) {
                b[i] = copy[i];
            }
        }
    }

    class InvertProc : Processor {
        public InvertProc(string target, Variant[] args) : base(target, args) {
            if (args == null) return;
            description = "Inverts a buffer's values.";
            usage = "addproc [target: String!] InvertProc";
        }

        override public void Process(Spectra s, Dictionary<String, Variant> v, byte[] b) {
            for (int i = 0; i < b.Length; i++) {
                b[i] = (byte)(255 - b[i]);
            }
        }
    }

    class RippleProc : Processor {
        private float[] heights;
        private float[] gradients;

        public RippleProc(string target, Variant[] args) : base(target, args) {
            usage = "[momentum: float!][speed: float!][decay: float!]";
        }

        override public void Process(Spectra s, Dictionary<String, Variant> v, byte[] b) {
            if (gradients == null) {
                heights = new float[b.Length];
                gradients = new float[b.Length];
            }

            for (int i = 1; i < b.Length - 1; i++) {
                if (b[i] == 255) {
                    heights[i] = 255;
                }
            }

            float momentum = args[0].f;
            float speed = args[1].f;
            float decay = args[2].f;

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
        }

    }

    //Sine gen
    //Triangle gen

    /*
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
     */

    class ExpressionProc : Processor {

        private Delegate exp;

        public ExpressionProc(string target, Variant[] args) : base(target, args) {
            description =
@"Sets each index to the byte result of an expression.
    Expression Variables:
        X - The target buffer
        c - The size of the target buffer
        i - The index being changed
        x - The value of the buffer at the index
        b - The buffer table
        s - The Spectra Instance
        v - The Spectra Variable Table";
            usage = "addproc [target: String!] ExpressionProc [expression: String!]";

            constructExpression();
        }

        private void constructExpression() {
            ParameterExpression X = Expression.Parameter(typeof(byte[]), "X");
            ParameterExpression x = Expression.Parameter(typeof(int), "x");
            ParameterExpression c = Expression.Parameter(typeof(int), "c");
            ParameterExpression i = Expression.Parameter(typeof(int), "i");
            ParameterExpression s = Expression.Parameter(typeof(Spectra), "s");
            ParameterExpression v = Expression.Parameter(typeof(Dictionary<String, Variant>), "v");
            ParameterExpression b = Expression.Parameter(typeof(ReadOnlyDictionary<String, byte[]>), "b");

            IDictionary<string, object> symbols = new Dictionary<string, object>();
            symbols.Add("X", X);
            symbols.Add("x", x);
            symbols.Add("c", c);
            symbols.Add("i", i);
            symbols.Add("s", s);
            symbols.Add("v", v);
            symbols.Add("b", b);

            var tree = System.Linq.Dynamic.DynamicExpression.Parse(null, args[0].s, symbols);
            var lmb = Expression.Lambda(tree, new ParameterExpression[] { X, x, c, i, s, v, b });
            exp = lmb.Compile();
        }

        override public void Process(Spectra s, Dictionary<String, Variant> v, byte[] b) {
            for(int i = 0; i < b.Length; i++) {
                b[i] = (byte) Convert.ToInt32(exp.DynamicInvoke(new object[] { b, (int) b[i], b.Length, i, s, v, s.GetBuffers() }));
            }
        }

    }

}
