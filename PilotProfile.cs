using System;
using UnityEngine;

namespace DronePilot
{
    // Supplies host-selected flight limits without naming a game biome.
    public sealed class PilotProfile
    {
        public string Name { get; }
        public float MaximumHeight { get; }
        public float MaximumOrbitRadius { get; }
        public Func<Vector3, bool> IsActive { get; }

        // Stores one named profile and its host-defined selection condition.
        public PilotProfile(string name, float maximumHeight,
            float maximumOrbitRadius, Func<Vector3, bool> isActive)
        {
            if (string.IsNullOrWhiteSpace(name) || maximumHeight <= 0f ||
                maximumOrbitRadius <= 0f)
            {
                throw new ArgumentException("Invalid pilot profile.");
            }
            Name = name;
            MaximumHeight = maximumHeight;
            MaximumOrbitRadius = maximumOrbitRadius;
            IsActive = isActive;
        }
    }
}
