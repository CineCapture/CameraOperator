using UnityEngine;

namespace DronePilot
{
    // Keeps the player framed with a smooth level-horizon rotation.
    internal sealed class DroneLook
    {
        private readonly FlightContext _context;

        // Binds the flight policy to one drone session.
        internal DroneLook(FlightContext context)
        {
            _context = context;
        }
        private float _yawVelocity;
        private float _pitchVelocity;

        // Preserves the copied view and resets smooth rotation state.
        internal void Initialize()
        {
            _yawVelocity = 0f;
            _pitchVelocity = 0f;
        }

        // Smooths pitch and yaw while keeping the horizon level.
        internal void Update(Transform cameraTransform, Vector3 focus)
        {
            Vector3 target = GetLevelAngles(cameraTransform.position, focus);
            Vector3 current = cameraTransform.eulerAngles;
            float pitch = Mathf.SmoothDampAngle(
                current.x, ClampPitch(target.x), ref _pitchVelocity,
                _context.N("aiming.look_smooth.rotation_smooth_time"), _context.N("aiming.look_smooth.maximum_rotation_speed"));
            float yaw = Mathf.SmoothDampAngle(
                current.y, target.y, ref _yawVelocity,
                _context.N("aiming.look_smooth.rotation_smooth_time"), _context.N("aiming.look_smooth.maximum_rotation_speed"));
            cameraTransform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        // Returns look angles whose roll is always zero.
        private Vector3 GetLevelAngles(Vector3 position, Vector3 focus)
        {
            Vector3 direction = focus - position;
            if (direction.sqrMagnitude <
                _context.N("numerical_tolerances.direction_squared"))
            {
                return Vector3.zero;
            }

            Vector3 angles = Quaternion.LookRotation(
                direction.normalized, Vector3.up).eulerAngles;
            return new Vector3(angles.x, angles.y, 0f);
        }

        // Limits forward and backward tilt while preserving a level horizon.
        private float ClampPitch(float angle)
        {
            float signed = Mathf.DeltaAngle(0f, angle);
            return Mathf.Clamp(
                signed, -_context.N("aiming.look_smooth.maximum_pitch_angle"), _context.N("aiming.look_smooth.maximum_pitch_angle"));
        }
    }
}
