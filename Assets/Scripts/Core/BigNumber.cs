using System;
using System.Globalization;

namespace Evolve.Core
{
    /// <summary>
    /// Double-based big number with mantissa+exponent formatting.
    /// Handles values up to ~1e308 with clean display.
    /// For idle/incremental games this is sufficient and much faster than BigInteger.
    /// </summary>
    [Serializable]
    public struct BigNumber : IComparable<BigNumber>, IEquatable<BigNumber>
    {
        public double Value;

        public static readonly BigNumber Zero = new(0);
        public static readonly BigNumber One = new(1);

        public BigNumber(double value) => Value = value;

        // Arithmetic
        public static BigNumber operator +(BigNumber a, BigNumber b) => new(a.Value + b.Value);
        public static BigNumber operator -(BigNumber a, BigNumber b) => new(a.Value - b.Value);
        public static BigNumber operator *(BigNumber a, BigNumber b) => new(a.Value * b.Value);
        public static BigNumber operator *(BigNumber a, double b) => new(a.Value * b);
        public static BigNumber operator /(BigNumber a, BigNumber b) => new(b.Value == 0 ? 0 : a.Value / b.Value);

        // Comparison
        public static bool operator >(BigNumber a, BigNumber b) => a.Value > b.Value;
        public static bool operator <(BigNumber a, BigNumber b) => a.Value < b.Value;
        public static bool operator >=(BigNumber a, BigNumber b) => a.Value >= b.Value;
        public static bool operator <=(BigNumber a, BigNumber b) => a.Value <= b.Value;
        public static bool operator ==(BigNumber a, BigNumber b) => Math.Abs(a.Value - b.Value) < 1e-10;
        public static bool operator !=(BigNumber a, BigNumber b) => !(a == b);

        // Implicit conversions
        public static implicit operator BigNumber(double v) => new(v);
        public static implicit operator BigNumber(int v) => new(v);
        public static implicit operator BigNumber(long v) => new(v);

        public int CompareTo(BigNumber other) => Value.CompareTo(other.Value);
        public bool Equals(BigNumber other) => this == other;
        public override bool Equals(object obj) => obj is BigNumber bn && this == bn;
        public override int GetHashCode() => Value.GetHashCode();

        /// <summary>
        /// Compact display: 1.5K, 3.2M, 1.0B, 4.5T, then scientific 1.2e15, etc.
        /// </summary>
        public override string ToString()
        {
            double abs = Math.Abs(Value);
            if (abs < 1_000) return Value.ToString("F1", CultureInfo.InvariantCulture);
            if (abs < 1_000_000) return (Value / 1_000).ToString("F2") + "K";
            if (abs < 1_000_000_000) return (Value / 1_000_000).ToString("F2") + "M";
            if (abs < 1_000_000_000_000.0) return (Value / 1_000_000_000).ToString("F2") + "B";
            if (abs < 1e15) return (Value / 1e12).ToString("F2") + "T";
            if (abs < 1e18) return (Value / 1e15).ToString("F2") + "Qa";
            if (abs < 1e21) return (Value / 1e18).ToString("F2") + "Qi";

            // Scientific notation for extreme values
            int exp = (int)Math.Floor(Math.Log10(abs));
            double mantissa = Value / Math.Pow(10, exp);
            return $"{mantissa:F2}e{exp}";
        }

        public string ToExact() => Value.ToString("G17", CultureInfo.InvariantCulture);
    }
}
