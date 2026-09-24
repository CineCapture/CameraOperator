using System;
using UnityEngine;

namespace CameraOperator
{
    // Supplies host-selected movement limits without naming a game biome.
    public sealed class CameraProfile
    {
        public string Name { get; }
        public float MaximumHeight { get; }
        public Func<Vector3, bool> IsActive { get; }

        // Stores one named profile and its host-defined selection condition.
        public CameraProfile(string name, float maximumHeight,
            Func<Vector3, bool> isActive)
        {
            if (string.IsNullOrWhiteSpace(name) || maximumHeight <= 0f)
            {
                throw new ArgumentException("Invalid camera profile.");
            }
            Name = name;
            MaximumHeight = maximumHeight;
            IsActive = isActive;
        }
    }
}
