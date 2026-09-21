using UnityEngine;

namespace DronePilot
{
    // Moves the drone with bounded acceleration and gradual force changes.
    internal sealed class DroneMotion
    {
        private readonly FlightContext _context;

        // Binds the flight policy to one drone session.
        internal DroneMotion(FlightContext context)
        {
            _context = context;
        }
        private Vector3 _velocity;
        private Vector3 _acceleration;
        private float _verticalVelocity;

        internal Vector3 Velocity => new Vector3(
            _velocity.x, _verticalVelocity, _velocity.z);
        internal Vector3 Acceleration => _acceleration;
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
            float arrivalSpeed = offset.magnitude / _context.N("kinematics.response_times.horizontal_arrival_time");
            float desiredSpeed = Mathf.Min(targetSpeed, arrivalSpeed);
            Vector3 desiredVelocity = offset.sqrMagnitude >
                _context.N("numerical_tolerances.destination_offset_squared")
                ? offset.normalized * desiredSpeed
                : Vector3.zero;
            return Advance(
                position, destination.y, desiredVelocity,
                emergencyAvoidance);
        }

        // Flies continuously along an orbit with a separate radial correction.
        internal Vector3 StepOrbit(
            Vector3 position, Vector3 destination, Vector3 tangent,
            float targetSpeed)
        {
            tangent.y = 0f;
            if (tangent.sqrMagnitude <
                _context.N("numerical_tolerances.direction_squared"))
            {
                return Step(position, destination, targetSpeed);
            }

            tangent.Normalize();
            Vector3 offset = destination - position;
            offset.y = 0f;
            Vector3 radial = offset - tangent * Vector3.Dot(offset, tangent);
            float radialError = radial.magnitude;
            float radialSpeed = radialError > _context.N("kinematics.radial_correction.dead_zone")
                ? Mathf.Min(
                    _context.N("kinematics.radial_correction.maximum_speed"),
                    (radialError - _context.N("kinematics.radial_correction.dead_zone")) /
                    _context.N("kinematics.radial_correction.correction_time"))
                : 0f;
            Vector3 desiredVelocity = tangent * targetSpeed;
            if (radialError >
                _context.N("numerical_tolerances.minimum_segment_length"))
            {
                desiredVelocity += radial.normalized * radialSpeed;
            }
            return Advance(position, destination.y, desiredVelocity);
        }

        // Applies jerk-limited acceleration and the shared vertical smoothing.
        private Vector3 Advance(
            Vector3 position, float destinationHeight,
            Vector3 desiredVelocity, bool emergencyAvoidance = false)
        {
            float deltaTime = Mathf.Max(Time.deltaTime,
                _context.N("numerical_tolerances.minimum_delta_time"));
            float accelerationLimit = emergencyAvoidance
                ? _context.N("kinematics.limits.emergency_acceleration") : _context.N("kinematics.limits.maximum_acceleration");
            float jerkLimit = emergencyAvoidance
                ? _context.N("kinematics.limits.emergency_jerk") : _context.N("kinematics.limits.maximum_jerk");
            float responseTime = emergencyAvoidance
                ? _context.N("kinematics.response_times.emergency_response_time") : _context.N("kinematics.response_times.velocity_response_time");
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
                _context.N("kinematics.vertical_speeds.cruise_vertical_speed"), _context.N("kinematics.vertical_speeds.catch_up_vertical_speed"),
                Mathf.InverseLerp(
                    _context.N("kinematics.vertical_speeds.catch_up_error_start"),
                    _context.N("kinematics.vertical_speeds.catch_up_error_end"),
                    Mathf.Abs(heightError)));
            float desiredVerticalSpeed = Mathf.Clamp(
                heightError / _context.N("kinematics.response_times.vertical_smooth_time"),
                -verticalSpeedLimit, verticalSpeedLimit);
            _verticalVelocity = Mathf.MoveTowards(
                _verticalVelocity, desiredVerticalSpeed,
                _context.N("kinematics.limits.maximum_vertical_acceleration") * deltaTime);
            next.y = position.y + _verticalVelocity * deltaTime;
            return next;
        }
    }
}
