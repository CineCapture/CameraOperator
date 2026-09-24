using UnityEngine;

namespace CameraOperator
{
    // Moves the camera with bounded acceleration and gradual force changes.
    internal sealed class CameraMotion
    {
        private readonly CameraContext _context;

        // Binds the camera movement policy to one camera session.
        internal CameraMotion(CameraContext context)
        {
            _context = context;
        }
        private Vector3 _velocity;
        private Vector3 _acceleration;
        private float _verticalVelocity;

        internal Vector3 Velocity => new Vector3(
            _velocity.x, _verticalVelocity, _velocity.z);
        internal float Speed => Velocity.magnitude;

        // Initializes continuous motion from the gameplay camera velocity.
        internal void Initialize(Vector3 velocity)
        {
            _velocity = velocity;
            _velocity.y = 0f;
            _verticalVelocity = 0f;
            _acceleration = Vector3.zero;
        }

        // Advances one jerk-limited movement step toward the destination.
        internal Vector3 Step(
            Vector3 position, Vector3 destination, float targetSpeed,
            bool emergencyAvoidance = false)
        {
            Vector3 offset = destination - position;
            offset.y = 0f;
            float arrivalSpeed = offset.magnitude / _context.N("motion.response_times.horizontal_arrival_time");
            float desiredSpeed = Mathf.Min(targetSpeed, arrivalSpeed);
            Vector3 desiredVelocity = offset.sqrMagnitude >
                CameraConstants.DestinationOffsetSquared
                ? offset.normalized * desiredSpeed
                : Vector3.zero;
            return Advance(
                position, destination.y, desiredVelocity,
                emergencyAvoidance);
        }

        // Applies jerk-limited acceleration and the shared vertical smoothing.
        private Vector3 Advance(
            Vector3 position, float destinationHeight,
            Vector3 desiredVelocity, bool emergencyAvoidance = false)
        {
            float deltaTime = Mathf.Max(
                Time.deltaTime, CameraConstants.MinimumDeltaTime);
            float accelerationLimit = emergencyAvoidance
                ? _context.N("motion.limits.emergency_acceleration") : _context.N("motion.limits.maximum_acceleration");
            float jerkLimit = emergencyAvoidance
                ? _context.N("motion.limits.emergency_jerk") : _context.N("motion.limits.maximum_jerk");
            float responseTime = emergencyAvoidance
                ? _context.N("motion.response_times.emergency_response_time") : _context.N("motion.response_times.velocity_response_time");
            Vector3 desiredAcceleration = Vector3.ClampMagnitude(
                (desiredVelocity - _velocity) / responseTime,
                accelerationLimit);
            desiredAcceleration.y = 0f;
            _acceleration = Vector3.MoveTowards(
                _acceleration, desiredAcceleration,
                jerkLimit * deltaTime);
            _velocity += _acceleration * deltaTime;
            _velocity.y = 0f;
            Vector3 next = position + _velocity * deltaTime;
            float heightError = destinationHeight - position.y;
            float verticalSpeedLimit = Mathf.Lerp(
                _context.N("motion.vertical_movement.normal_speed"),
                _context.N("motion.vertical_movement.maximum_speed"),
                Mathf.InverseLerp(
                    _context.N("motion.vertical_movement.maximum_speed_error_start"),
                    _context.N("motion.vertical_movement.maximum_speed_error_end"),
                    Mathf.Abs(heightError)));
            float desiredVerticalSpeed = Mathf.Clamp(
                heightError / _context.N("motion.response_times.vertical_smooth_time"),
                -verticalSpeedLimit, verticalSpeedLimit);
            _verticalVelocity = Mathf.MoveTowards(
                _verticalVelocity, desiredVerticalSpeed,
                _context.N("motion.limits.maximum_vertical_acceleration") * deltaTime);
            next.y = position.y + _verticalVelocity * deltaTime;
            return next;
        }
    }
}
