// PROTOTYPE - NOT FOR PRODUCTION
// Question: 降帧或折叠是否只影响绘制而不丢弃模拟时间？
// Date: 2026-10-23
using System;

namespace NormalCampaignPrototype
{
    public sealed class PrototypeClock
    {
        public double RealSeconds { get; private set; }
        public double PendingSeconds { get; private set; }
        public double SuspendedSeconds { get; private set; }
        public bool Suspended { get; private set; }

        public double Next(double elapsed, bool paused, double speed)
        {
            if (elapsed < 0 || double.IsNaN(elapsed) || double.IsInfinity(elapsed)) throw new ArgumentOutOfRangeException("elapsed");
            RealSeconds += elapsed;
            Suspended = elapsed > 30;
            if (Suspended) { SuspendedSeconds += elapsed; return 0; }
            if (paused) return 0;
            PendingSeconds += elapsed * speed;
            double batch = Math.Min(3, PendingSeconds);
            PendingSeconds -= batch;
            return batch;
        }
    }
}
