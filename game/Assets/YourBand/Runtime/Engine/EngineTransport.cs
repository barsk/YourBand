using YourBand.Core.Engine;
using YourBand.Core.Playback;

namespace YourBand.Runtime.Engine
{
    /// <summary>
    /// The transport when the audio engine plays the band: commands go to the engine, and the position comes from
    /// its clock, so the chart moves with the sound. <see cref="Refresh"/> once a frame.
    /// </summary>
    public sealed class EngineTransport : ITransport
    {
        readonly EngineLink link;
        readonly EngineClock clock;
        ClockSnapshot snapshot;
        double speed = 1;

        public EngineTransport(EngineLink link, EngineClock clock)
        {
            this.link = link;
            this.clock = clock;
        }

        /// <summary>True if the clock could be read on the last <see cref="Refresh"/>.</summary>
        public bool HasClock { get; private set; }

        public void Refresh()
        {
            var read = clock.Read();
            HasClock = read != null;
            if (read != null)
                snapshot = read.Value;
        }

        public bool Playing => snapshot.Playing;
        public double Speed => speed;
        public double Quarter => snapshot.QuarterAt(EngineClock.Now());
        public double CountInUntil => snapshot.CountInUntil;

        public void Play(double fromQuarter, double countInQuarters) => link.Send(EngineProtocol.Play(fromQuarter, countInQuarters));
        public void Pause() => link.Send(EngineProtocol.Pause());
        public void Seek(double quarter) => link.Send(EngineProtocol.Seek(quarter));

        public void SetSpeed(double value)
        {
            speed = value;
            link.Send(EngineProtocol.Speed(value));
        }
    }
}
