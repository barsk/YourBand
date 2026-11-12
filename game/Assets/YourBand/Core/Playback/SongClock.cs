using System;
using YourBand.Core.Music;

namespace YourBand.Core.Playback
{
    /// <summary>
    /// Where in the song we are, from a wall clock: play, pause, seek, and a practice speed. Song time runs in
    /// seconds of the song (so tempo changes in the map are followed) and can be negative: a count-in plays before
    /// the song's start. The views read it every frame; the audio engine's clock replaces the wall clock in M2.
    /// </summary>
    public sealed class SongClock
    {
        TempoMap tempo;
        double anchorSongSeconds;  // song time at the anchor
        double anchorWall;         // wall time at the anchor
        double speed = 1;

        public SongClock(TempoMap tempo)
        {
            this.tempo = tempo ?? throw new ArgumentNullException(nameof(tempo));
        }

        public bool Playing { get; private set; }

        /// <summary>Practice speed: 1 is the song's tempo, 0.8 is 80% of it.</summary>
        public double Speed => speed;

        public TempoMap Tempo
        {
            get => tempo;
            set => tempo = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>Song time (seconds at the song's own tempo) at a wall time.</summary>
        public double SongSecondsAt(double wall) =>
            Playing ? anchorSongSeconds + (wall - anchorWall) * speed : anchorSongSeconds;

        /// <summary>The position in quarter notes at a wall time (negative during a count-in).</summary>
        public double QuarterAt(double wall) => QuarterOf(SongSecondsAt(wall));

        public void Play(double wall)
        {
            if (Playing)
                return;
            anchorWall = wall;
            Playing = true;
        }

        public void Pause(double wall)
        {
            if (!Playing)
                return;
            anchorSongSeconds = SongSecondsAt(wall);
            Playing = false;
        }

        /// <summary>Moves to a position, playing on from there if it was playing.</summary>
        public void Seek(double quarter, double wall)
        {
            anchorSongSeconds = SecondsOf(quarter);
            anchorWall = wall;
        }

        public void SetSpeed(double newSpeed, double wall)
        {
            if (newSpeed <= 0)
                throw new ArgumentOutOfRangeException(nameof(newSpeed), "the speed must be positive");
            anchorSongSeconds = SongSecondsAt(wall);
            anchorWall = wall;
            speed = newSpeed;
        }

        // Before the start the first tempo holds (TempoMap handles positive positions only).
        double SecondsOf(double quarter) =>
            quarter >= 0 ? tempo.SecondsAt(quarter) : quarter * 60.0 / tempo.BpmAt(0);

        double QuarterOf(double seconds) =>
            seconds >= 0 ? tempo.QuarterAt(seconds) : seconds * tempo.BpmAt(0) / 60.0;
    }
}
