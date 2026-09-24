using UnityEngine;

namespace CameraOperator
{
    // Shares the current configuration, world queries, and camera inputs.
    internal sealed class CameraContext
    {
        internal Configuration Config;
        internal CameraWorld World;
        internal CameraProfile Profile;
        internal GameObject Target;
        internal Vector3 TargetVelocity;
        internal float MaxSpeed;
        internal Vector3 CameraDirectionWorld;
        internal float CameraDistance;
        internal float CameraHeightAboveGround;
        internal bool HasCameraAnchor;

        // Reads one camera tuning value from the active YAML snapshot.
        internal float N(string path)
        {
            return Config.Number(path);
        }

        // Returns the current aim point in world axes.
        internal Vector3 Focus => Target.transform.position +
            Vector3.up * N("aiming.target_height");

        // Returns the terrain height or the input elevation when unavailable.
        internal float Ground(Vector3 position)
        {
            return World.HeightOr(position, position.y);
        }
    }
}
