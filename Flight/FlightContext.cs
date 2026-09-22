using UnityEngine;

namespace DronePilot
{
    // Shares the current configuration, world queries, and flight inputs.
    internal sealed class FlightContext
    {
        internal Configuration Config;
        internal DroneWorld World;
        internal PilotProfile Profile;
        internal GameObject Target;
        internal Vector3 AimOffset;
        internal Vector3 TargetVelocity;
        internal float MaxSpeed;
        internal Vector3 TrailingViewDirectionLocal;
        internal bool HasTrailingViewDirection;
        internal System.Action<string, string> Event;

        // Reads one flight tuning value from the active YAML snapshot.
        internal float N(string path)
        {
            return Config.Number(path);
        }

        // Reads one integer flight tuning value from the active YAML snapshot.
        internal int I(string path)
        {
            return Config.Integer(path);
        }

        // Reads one feature flag from the active YAML snapshot.
        internal bool B(string path)
        {
            return Config.Enabled(path);
        }

        // Returns the current aim point in world axes.
        internal Vector3 Focus => Target.transform.position + AimOffset;

        // Resolves the camera-cut sector relative to the target's orientation.
        internal Vector3 TrailingViewDirection
        {
            get
            {
                Vector3 direction = Target.transform.TransformDirection(
                    TrailingViewDirectionLocal);
                direction.y = 0f;
                return direction.normalized;
            }
        }

        // Returns the terrain height or the input elevation when unavailable.
        internal float Ground(Vector3 position)
        {
            return World.HeightOr(position, position.y);
        }
    }
}
