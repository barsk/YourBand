using System;
using System.Collections.Generic;
using System.Linq;
using YourBand.Core.Band;

namespace YourBand.Core.Performance
{
    public enum Limb { RightHand, LeftHand, RightFoot, LeftFoot }

    /// <summary>The kit's playing points (tools/band_assets' drum kit has them under these names).</summary>
    public enum KitPiece { HiHat, Snare, Crash, Ride, Tom1, Tom2, FloorTom, Kick, HiHatPedal }

    /// <summary>One hit: which limb plays which piece, when (quarter notes), how hard (MIDI velocity).</summary>
    public readonly struct DrumStroke
    {
        public Limb Limb { get; }
        public KitPiece Piece { get; }
        public double AtQuarter { get; }
        public int Velocity { get; }
        /// <summary>A hi-hat hit with the hats open (the foot lets them apart).</summary>
        public bool Open { get; }

        public DrumStroke(Limb limb, KitPiece piece, double atQuarter, int velocity, bool open = false)
        {
            Limb = limb;
            Piece = piece;
            AtQuarter = atQuarter;
            Velocity = velocity;
            Open = open;
        }

        public override string ToString() => $"{Limb} {Piece} at {AtQuarter} ({Velocity}){(Open ? " open" : "")}";
    }

    /// <summary>
    /// Who plays what on the kit, for a right-handed drummer playing the hi-hat crossed: the hi-hat and the ride
    /// with the right hand, the snare with the left, the crash with the right hand when it is free; toms in a fill
    /// hand to hand; the kick with the right foot and the hi-hat pedal with the left.
    /// </summary>
    public static class DrumPerformance
    {
        /// <summary>No hand plays two hits closer than this (quarter notes): a sixteenth.</summary>
        const double Fastest = 0.25 - 1e-6;

        public static KitPiece? PieceOf(int note)
        {
            switch (note)
            {
                case Drum.ClosedHat: case Drum.OpenHat: return KitPiece.HiHat;
                case Drum.PedalHat: return KitPiece.HiHatPedal;
                case Drum.Kick: case 35: return KitPiece.Kick;
                case Drum.Snare: case Drum.SideStick: case 40: return KitPiece.Snare;
                case Drum.Crash: case 57: return KitPiece.Crash;
                case Drum.Ride: case 59: return KitPiece.Ride;
                case Drum.HighTom: case 48: return KitPiece.Tom1;
                case Drum.MidTom: case Drum.LowTom: return KitPiece.Tom2;
                case 41: case 43: return KitPiece.FloorTom;
                default: return null;
            }
        }

        public static List<DrumStroke> Plan(IEnumerable<BandNote> notes)
        {
            var strokes = new List<DrumStroke>();
            var lastRight = double.NegativeInfinity;
            var lastLeft = double.NegativeInfinity;
            foreach (var group in notes.Where(n => n.Part == BandPart.Drums).GroupBy(n => Math.Round(n.AtQuarter, 6)).OrderBy(g => g.Key))
            {
                var at = group.Key;
                var rightTaken = false;
                var leftTaken = false;
                // in a run (hits less than an eighth apart) the hands take turns
                var run = at - Math.Max(lastRight, lastLeft) < 0.5 - 1e-6;
                var previousRight = lastRight >= lastLeft;
                // the hands' pieces by how much they matter: cymbal accents, then drums, then time-keeping
                foreach (var n in group.OrderBy(n => Rank(PieceOf(n.Pitch))))
                {
                    var piece = PieceOf(n.Pitch);
                    if (piece == null)
                        continue;
                    if (piece == KitPiece.Kick || piece == KitPiece.HiHatPedal)
                    {
                        strokes.Add(new DrumStroke(piece == KitPiece.Kick ? Limb.RightFoot : Limb.LeftFoot, piece.Value, at, n.Velocity));
                        continue;
                    }
                    var rightFree = !rightTaken && at - lastRight >= Fastest;
                    var leftFree = !leftTaken && at - lastLeft >= Fastest;
                    bool right;
                    switch (piece.Value)
                    {
                        case KitPiece.HiHat:
                        case KitPiece.Ride:
                            right = true;  // the time-keeping hand; nothing else if it is taken
                            if (!rightFree)
                                continue;
                            break;
                        default:
                            if (!leftFree && !rightFree)
                                continue;
                            if (run && (previousRight ? leftFree : rightFree))
                                right = !previousRight;  // hand to hand, as in a fill
                            else if (piece == KitPiece.Snare)
                                right = !leftFree;  // the left hand's drum
                            else  // crash and toms: the hand that has been free longer, the right one if both
                                right = rightFree && (!leftFree || lastRight <= lastLeft);
                            break;
                    }
                    strokes.Add(new DrumStroke(right ? Limb.RightHand : Limb.LeftHand, piece.Value, at, n.Velocity, n.Pitch == Drum.OpenHat));
                    if (right)
                    {
                        rightTaken = true;
                        lastRight = at;
                    }
                    else
                    {
                        leftTaken = true;
                        lastLeft = at;
                    }
                }
            }
            return strokes;
        }

        static int Rank(KitPiece? piece)
        {
            switch (piece)
            {
                case KitPiece.Crash: return 0;
                case KitPiece.Snare: return 1;
                case KitPiece.Tom1: case KitPiece.Tom2: case KitPiece.FloorTom: return 2;
                case KitPiece.Ride: return 3;
                default: return 4;
            }
        }
    }
}
