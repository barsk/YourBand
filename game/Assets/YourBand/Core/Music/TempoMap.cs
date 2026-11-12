using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace YourBand.Core.Music
{
    /// <summary>A time signature: <c>Numerator</c> notes of length 1/<c>Denominator</c> per bar.</summary>
    public readonly struct TimeSignature : IEquatable<TimeSignature>
    {
        public int Numerator { get; }
        public int Denominator { get; }

        public TimeSignature(int numerator, int denominator)
        {
            if (numerator <= 0 || denominator <= 0)
                throw new ArgumentOutOfRangeException(nameof(numerator), "a time signature needs positive numbers");
            Numerator = numerator;
            Denominator = denominator;
        }

        public static TimeSignature FourFour => new TimeSignature(4, 4);

        /// <summary>The length of a bar in quarter notes (3/4: 3, 6/8: 3, 4/4: 4).</summary>
        public double QuartersPerBar => Numerator * 4.0 / Denominator;

        public bool Equals(TimeSignature other) => Numerator == other.Numerator && Denominator == other.Denominator;
        public override bool Equals(object obj) => obj is TimeSignature other && Equals(other);
        public override int GetHashCode() => Numerator * 397 ^ Denominator;
        public override string ToString() => $"{Numerator}/{Denominator}";
    }

    /// <summary>A tempo from a position onwards: quarter notes per minute.</summary>
    public sealed class TempoChange
    {
        public double AtQuarter { get; set; }
        public double Bpm { get; set; }
    }

    /// <summary>A time signature from a bar onwards (bars are numbered from 0).</summary>
    public sealed class MeterChange
    {
        public int AtBar { get; set; }
        public int Numerator { get; set; } = 4;
        public int Denominator { get; set; } = 4;
        [JsonIgnore] public TimeSignature Signature => new TimeSignature(Numerator, Denominator);
    }

    /// <summary>
    /// Musical time and clock time. Positions are in quarter notes from the start of the song (as in MIDI), so
    /// a tempo change never moves a chord; bars come from the time signatures, seconds from the tempos. The
    /// band changes tempo while you play by adding tempo changes ahead of the music (see docs/plan.md).
    /// </summary>
    public sealed class TempoMap
    {
        public List<TempoChange> Tempos { get; set; } = new List<TempoChange>();
        public List<MeterChange> Meters { get; set; } = new List<MeterChange>();

        public TempoMap() { }

        public TempoMap(double bpm, TimeSignature signature)
        {
            SetTempo(0, bpm);
            SetMeter(0, signature);
        }

        // ------------------------------------------------------------ editing
        /// <summary>Sets the tempo from <paramref name="atQuarter"/> onwards (replacing one at the same place).</summary>
        public void SetTempo(double atQuarter, double bpm)
        {
            if (bpm <= 0)
                throw new ArgumentOutOfRangeException(nameof(bpm), "a tempo must be positive");
            Tempos.RemoveAll(t => t.AtQuarter == atQuarter);
            Tempos.Add(new TempoChange { AtQuarter = atQuarter, Bpm = bpm });
            Tempos.Sort((a, b) => a.AtQuarter.CompareTo(b.AtQuarter));
        }

        /// <summary>Sets the time signature from bar <paramref name="atBar"/> onwards.</summary>
        public void SetMeter(int atBar, TimeSignature signature)
        {
            Meters.RemoveAll(m => m.AtBar == atBar);
            Meters.Add(new MeterChange { AtBar = atBar, Numerator = signature.Numerator, Denominator = signature.Denominator });
            Meters.Sort((a, b) => a.AtBar.CompareTo(b.AtBar));
        }

        // ------------------------------------------------------------ tempo
        public double BpmAt(double quarter)
        {
            var bpm = Tempos.Count > 0 ? Tempos[0].Bpm : 120.0;
            foreach (var t in Tempos)
            {
                if (t.AtQuarter > quarter)
                    break;
                bpm = t.Bpm;
            }
            return bpm;
        }

        /// <summary>Seconds from the start of the song to a position (before the first tempo: the first tempo).</summary>
        public double SecondsAt(double quarter)
        {
            if (Tempos.Count == 0)
                return quarter * 60.0 / 120.0;
            double seconds = 0, at = 0, bpm = Tempos[0].Bpm;
            foreach (var t in Tempos)
            {
                if (t.AtQuarter >= quarter)
                    break;
                if (t.AtQuarter > at)
                {
                    seconds += (t.AtQuarter - at) * 60.0 / bpm;
                    at = t.AtQuarter;
                }
                bpm = t.Bpm;
            }
            return seconds + (quarter - at) * 60.0 / bpm;
        }

        /// <summary>The position at a time (the inverse of <see cref="SecondsAt"/>).</summary>
        public double QuarterAt(double seconds)
        {
            if (Tempos.Count == 0)
                return seconds * 120.0 / 60.0;
            double at = 0, elapsed = 0, bpm = Tempos[0].Bpm;
            foreach (var t in Tempos)
            {
                if (t.AtQuarter > at)
                {
                    var span = (t.AtQuarter - at) * 60.0 / bpm;
                    if (elapsed + span > seconds)
                        break;
                    elapsed += span;
                    at = t.AtQuarter;
                }
                bpm = t.Bpm;
            }
            return at + (seconds - elapsed) * bpm / 60.0;
        }

        // ------------------------------------------------------------ bars
        public TimeSignature SignatureAtBar(int bar)
        {
            var signature = TimeSignature.FourFour;
            foreach (var m in Meters)
            {
                if (m.AtBar > bar)
                    break;
                signature = m.Signature;
            }
            return signature;
        }

        /// <summary>Where bar <paramref name="bar"/> starts, in quarter notes.</summary>
        public double QuarterOfBar(int bar)
        {
            double quarter = 0;
            for (int b = 0; b < bar; ++b)
                quarter += SignatureAtBar(b).QuartersPerBar;
            return quarter;
        }

        /// <summary>The bar a position falls in, and how far into that bar it is (in quarter notes).</summary>
        public (int bar, double quarterInBar) BarAt(double quarter)
        {
            if (quarter < 0)
                return (0, quarter);
            int bar = 0;
            double start = 0;
            while (true)
            {
                var length = SignatureAtBar(bar).QuartersPerBar;
                if (quarter < start + length)
                    return (bar, quarter - start);
                start += length;
                ++bar;
            }
        }
    }
}
