using UnityEngine;

namespace CameraOperator
{
    // Detects lost framing and produces a fast recovery position.
    internal sealed class CameraFraming
    {
        private readonly CameraContext _context;

        // Binds the camera movement policy to one camera session.
        internal CameraFraming(CameraContext context)
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
            Vector3 cameraPosition)
        {
            Vector3 targetPosition = _context.Target.transform.position;
            Vector3 radial = cameraPosition - targetPosition;
            radial.y = 0f;
            if (radial.sqrMagnitude < CameraConstants.DirectionSquared)
            {
                radial = -_context.Target.transform.forward;
                radial.y = 0f;
            }

            radial.Normalize();
            float height = Mathf.Max(0f,
                cameraPosition.y - targetPosition.y);
            float distanceForHeight = height /
                Mathf.Tan(_context.N("recovery.comfortable_pitch") * Mathf.Deg2Rad);
            float recoveryDistance = Mathf.Max(
                _context.N("recovery.distance"), distanceForHeight);
            Vector3 target = targetPosition +
                             radial * recoveryDistance;
            target.y = GetGroundHeight(target) + _context.N("recovery.height");
            return target;
        }

        // Returns terrain height or the input height when unavailable.
        private float GetGroundHeight(Vector3 position)
        {
            return _context.Ground(position);
        }
    }
}
