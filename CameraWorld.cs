using System;
using UnityEngine;

namespace CameraOperator
{
    // Provides game-independent terrain and collider classification callbacks.
    public sealed class CameraWorld
    {
        public Func<Vector3, float?> GroundHeight { get; set; }
        public Func<Collider, bool> IgnoreObstacle { get; set; }
        public Func<Collider, bool> IsActor { get; set; }
        public Action<string> LogDebug { get; set; }
        public Action<string> LogInfo { get; set; }
        public Action<string> LogWarning { get; set; }

        // Returns a terrain height or the supplied fallback elevation.
        public float HeightOr(Vector3 position, float fallback)
        {
            return GroundHeight?.Invoke(position) ?? fallback;
        }
    }
}
