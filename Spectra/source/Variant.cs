using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace Spectra.source {

    class Variant {
        public Object value;
        public Action<Spectra> onChange;

        public Variant(Object value, Action<Spectra> onChange = null) {
            this.value = value;
            this.onChange = onChange;
        }

        virtual public int i {
            get {
                if (value is float) {
                    return Convert.ToInt32(value);
                } else {
                    return (int)value;
                }
            }
        }

        virtual public float f {
            get {
                if (value is int) {
                    return (int)(value);
                } else {
                    return (float)value;
                }
            }
        }

        virtual public String s {
            get { return (String)value; }
        }

        virtual public bool b {
            get { return (bool)value; }
        }

        private static readonly Regex ints = new Regex(@"^-?[0-9]+$");
        private static readonly Regex floats = new Regex(@"^-?[0-9][0-9,\.]+$");
        private static readonly Regex bools = new Regex(@"true|false", RegexOptions.IgnoreCase);

        public static Variant Parse(String value) {
            if (ints.IsMatch(value)) {
                return new Variant(int.Parse(value));
            } else if (floats.IsMatch(value)) {
                return new Variant(float.Parse(value));
            } else if (bools.IsMatch(value)) {
                return new Variant(bool.Parse(value));
            } else {
                return new Variant(value);
            }
        }
    }

    class RandomVariant : Variant {
        private Random r;

        public RandomVariant(Object value, Action<Spectra> onChange = null) : base(value, onChange) {
            r = new Random();
        }

        override public int i {
            get {
                return r.Next() % 255;
            }
        }

        public float f {
            get {
                return (float)r.NextDouble();
            }
        }

        public String s {
            get { return "A Random String???"; }
        }

        public bool b {
            get { return Convert.ToBoolean(r.Next() % 2); }
        }
    }

}
