using YourBand.Core.Music;
using NUnit.Framework;

namespace YourBand.Core.Tests
{
    public class TempoMapTests
    {
        const double Tolerance = 1e-9;

        [Test]
        public void ConstantTempo_QuartersAndSeconds()
        {
            var map = new TempoMap(112, TimeSignature.FourFour);  // Wicked Game
            Assert.AreEqual(60.0 / 112, map.SecondsAt(1), Tolerance);
            Assert.AreEqual(8 * 4 * 60.0 / 112, map.SecondsAt(map.QuarterOfBar(8)), Tolerance);  // the intro: about 17 s
            Assert.AreEqual(4, map.QuarterAt(4 * 60.0 / 112), Tolerance);
        }

        [Test]
        public void TempoChange_SecondsAddUpPiecewise()
        {
            var map = new TempoMap(120, TimeSignature.FourFour);
            map.SetTempo(8, 60);  // half speed from bar 2
            Assert.AreEqual(4.0, map.SecondsAt(8), Tolerance);          // 8 quarters at 0.5 s
            Assert.AreEqual(4.0 + 4.0, map.SecondsAt(12), Tolerance);   // then 4 quarters at 1 s
            Assert.AreEqual(60, map.BpmAt(10));
            Assert.AreEqual(120, map.BpmAt(7.99));
        }

        [TestCase(0.0)]
        [TestCase(3.25)]
        [TestCase(8.0)]
        [TestCase(17.5)]
        public void QuarterAt_InvertsSecondsAt(double quarter)
        {
            var map = new TempoMap(96, TimeSignature.FourFour);
            map.SetTempo(8, 104);
            map.SetTempo(16, 90);
            Assert.AreEqual(quarter, map.QuarterAt(map.SecondsAt(quarter)), 1e-9);
        }

        [Test]
        public void MeterChange_BarsFollowTheTimeSignature()
        {
            var map = new TempoMap(120, TimeSignature.FourFour);
            map.SetMeter(2, new TimeSignature(3, 4));  // two bars of 4/4, then 3/4
            Assert.AreEqual(8, map.QuarterOfBar(2), Tolerance);
            Assert.AreEqual(11, map.QuarterOfBar(3), Tolerance);
            Assert.AreEqual((2, 1.5), map.BarAt(9.5));
            Assert.AreEqual(new TimeSignature(3, 4), map.SignatureAtBar(5));
        }

        [Test]
        public void SixEight_IsThreeQuartersPerBar()
        {
            Assert.AreEqual(3, new TimeSignature(6, 8).QuartersPerBar, Tolerance);
        }
    }
}
