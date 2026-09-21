using System;

namespace DronePilot
{
    // Defines one caller-owned diagnostic session independently of recording.
    public sealed class TelemetryOptions
    {
        public bool Enabled { get; set; }
        public string RootDirectory { get; set; }
        public float SampleIntervalSeconds { get; set; }
        public float FlushIntervalSeconds { get; set; }
        public float OrbitDirectionThreshold { get; set; }

        // Rejects invalid host options before a session starts.
        public void Validate()
        {
            if (!Enabled)
            {
                return;
            }
            if (string.IsNullOrWhiteSpace(RootDirectory) ||
                SampleIntervalSeconds <= 0f ||
                FlushIntervalSeconds <= SampleIntervalSeconds ||
                OrbitDirectionThreshold < 0f)
            {
                throw new ArgumentException("Invalid drone telemetry options.");
            }
        }
    }
}
