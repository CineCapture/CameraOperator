using System;
using UnityEngine;

namespace DronePilot
{
    // Supplies a viewer camera and optional visual choices to the pilot.
    public sealed class DroneVisualOptions
    {
        public Camera ViewerCamera { get; set; }
        public bool Visible { get; set; }
        public DroneShellColor Color { get; set; } = DroneShellColor.Metal;
        public Action<string> LogInfo { get; set; }
        public Action<string> LogWarning { get; set; }
        public Action<string> LogError { get; set; }
    }
}
