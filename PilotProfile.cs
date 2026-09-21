using System;
using UnityEngine;

namespace DronePilot
{
    // Supplies host-selected flight limits without naming a game biome.
    public sealed class PilotProfile
    {
        private readonly Func<float> _maximumHeight;
        private readonly Func<float> _maximumOrbitRadius;
        public string Name { get; }
        public float MaximumHeight => _maximumHeight();
        public float MaximumOrbitRadius => _maximumOrbitRadius();
        public Func<Vector3, bool> IsActive { get; }

        // Stores one named profile and its host-defined selection condition.
        public PilotProfile(string name, float maximumHeight,
            float maximumOrbitRadius, Func<Vector3, bool> isActive)
            : this(name, () => maximumHeight, () => maximumOrbitRadius, isActive)
        {
        }

        // Reads live profile limits from host-owned configuration delegates.
        public PilotProfile(string name, Func<float> maximumHeight,
            Func<float> maximumOrbitRadius, Func<Vector3, bool> isActive)
        {
            if (string.IsNullOrWhiteSpace(name) || maximumHeight == null ||
                maximumOrbitRadius == null || maximumHeight() <= 0f ||
                maximumOrbitRadius() <= 0f)
            {
                throw new ArgumentException("Invalid pilot profile.");
            }
            Name = name;
            _maximumHeight = maximumHeight;
            _maximumOrbitRadius = maximumOrbitRadius;
            IsActive = isActive;
        }
    }
}
