using System;
using GuitarTutor.Core.Music;

namespace GuitarTutor.Core.Playback
{
    /// <summary>
    /// The song's transport as the game sees it: where the music is, and play, pause, seek and speed. The audio
    /// engine runs it when it is connected (its clock is the music); otherwise <see cref="LocalTransport"/> keeps
    /// time on its own, silently.
    /// </summary>
    public interface ITransport
    {
        bool Playing { get; }
        double Speed { get; }
        /// <summary>Where the music is now, in quarter notes (during a count-in: before <see cref="CountInUntil"/>).</summary>
        double Quarter { get; }
        /// <summary>Where the count-in ends and the music starts; NaN when there is none.</summary>
        double CountInUntil { get; }

        /// <summary>Plays from a position, after a count-in of that many quarter notes (0: none).</summary>
        void Play(double fromQuarter, double countInQuarters);
        void Pause();
        void Seek(double quarter);
        void SetSpeed(double speed);
    }

    /// <summary>A transport on a wall clock, for when there is no audio engine (as in M1).</summary>
    public sealed class LocalTransport : ITransport
    {
        readonly SongClock clock;
        readonly Func<double> now;
        double countInUntil = double.NaN;

        /// <param name="wallClock">Seconds from any fixed point (the game's real time).</param>
        public LocalTransport(TempoMap tempo, Func<double> wallClock)
        {
            clock = new SongClock(tempo);
            now = wallClock;
        }

        public bool Playing => clock.Playing;
        public double Speed => clock.Speed;
        public double Quarter => clock.QuarterAt(now());

        public double CountInUntil
        {
            get
            {
                if (!double.IsNaN(countInUntil) && Quarter >= countInUntil)
                    countInUntil = double.NaN;
                return countInUntil;
            }
        }

        public void Play(double fromQuarter, double countInQuarters)
        {
            var t = now();
            clock.Seek(fromQuarter - countInQuarters, t);
            countInUntil = countInQuarters > 0 ? fromQuarter : double.NaN;
            clock.Play(t);
        }

        public void Pause()
        {
            clock.Pause(now());
            countInUntil = double.NaN;
        }

        public void Seek(double quarter)
        {
            clock.Seek(quarter, now());
            countInUntil = double.NaN;
        }

        public void SetSpeed(double speed) => clock.SetSpeed(speed, now());
    }
}
