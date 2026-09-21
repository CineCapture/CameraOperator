using UnityEngine;

namespace DronePilot
{
    // Detects lost framing and produces a fast recovery position.
    internal sealed class DroneFraming
    {
        private readonly FlightContext _context;

        // Binds the flight policy to one drone session.
        internal DroneFraming(FlightContext context)
        {
            _context = context;
        }

        // Returns whether the player focus remains inside the camera view.
        internal bool IsVisible(Camera camera, Vector3 playerFocus)
        {
            Vector3 viewport = camera.WorldToViewportPoint(playerFocus);
            return viewport.z > 0f &&
                   viewport.x >= 0f && viewport.x <= 1f &&
                   viewport.y >= 0f && viewport.y <= 1f;
        }

        // Chooses a nearby position compatible with the pitch restriction.
        internal Vector3 GetRecoveryTarget(
            Vector3 dronePosition)
        {
            Vector3 targetPosition = _context.Target.transform.position;
            Vector3 radial = dronePosition - targetPosition;
            radial.y = 0f;
            if (radial.sqrMagnitude <
                _context.N("numerical_tolerances.direction_squared"))
            {
                radial = -_context.Target.transform.forward;
                radial.y = 0f;
            }

            radial.Normalize();
            float height = Mathf.Max(0f,
                dronePosition.y - targetPosition.y);
            float distanceForHeight = height /
                Mathf.Tan(_context.N("framing.recovery.comfortable_pitch") * Mathf.Deg2Rad);
            float recoveryDistance = Mathf.Max(
                _context.N("framing.recovery.distance"), distanceForHeight);
            Vector3 target = targetPosition +
                             radial * recoveryDistance;
            target.y = GetGroundHeight(target) + _context.N("framing.recovery.height");
            return target;
        }

        // Returns terrain height or the input height when unavailable.
        private float GetGroundHeight(Vector3 position)
        {
            return _context.Ground(position);
        }
    }
}
