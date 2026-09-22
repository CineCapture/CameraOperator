using UnityEngine;

namespace DronePilot
{
    // Places a higher trailing drone farther behind the player.
    internal sealed class TrailingFlightController
    {
        private readonly FlightContext _context;

        // Binds the flight policy to one drone session.
        internal TrailingFlightController(FlightContext context)
        {
            _context = context;
            _route = new TrailingRoutePlanner(context);
        }
        private readonly TrailingRoutePlanner _route;
        private float _altitude;
        private float _altitudeVelocity;
        private float _commandedSpeed;
        private bool _speedInitialized;
        private bool _altitudeInitialized;

        // Predicts a short trailing route around nearby obstacles.
        internal Vector3 PlanRoute(
            Vector3 origin, Vector3 target, Vector3 droneVelocity,
            Vector3 playerPosition, Vector3 playerVelocity)
        {
            if (!_context.B("flight_modes.trailing_flight.planner.enabled"))
            {
                return target;
            }
            return _route.Plan(
                origin, target, droneVelocity,
                playerPosition, playerVelocity);
        }

        // Clears any route state when trailing flight ends.
        internal void Leave()
        {
            _route.Reset();
            _altitudeInitialized = false;
            _altitudeVelocity = 0f;
            _speedInitialized = false;
        }

        // Builds a trailing destination with altitude-dependent distance.
        internal Vector3 GetTarget(
            Vector3 playerPosition, Vector3 dronePosition,
            Vector3 direction,
            PilotProfile environment, float terrainClearance)
        {
            float horizontalDistance = Flatten(
                dronePosition - playerPosition).magnitude;
            float allowedHeight = Mathf.Min(
                environment.MaximumHeight,
                Mathf.Lerp(
                    _context.N("flight_modes.trailing_flight.altitude_limit_by_distance.near_maximum_height"),
                    _context.N("flight_modes.trailing_flight.altitude_limit_by_distance.far_maximum_height"),
                    Mathf.InverseLerp(
                        _context.N("flight_modes.trailing_flight.altitude_limit_by_distance.near_distance"),
                        _context.N("flight_modes.trailing_flight.altitude_limit_by_distance.far_distance"),
                        horizontalDistance)));
            float altitude = GetAltitude(
                terrainClearance, allowedHeight);
            float distance = Mathf.Lerp(
                _context.N("flight_modes.trailing_flight.distances.minimum"), _context.N("flight_modes.trailing_flight.distances.maximum"),
                _context.N("flight_modes.trailing_flight.position_variation.distance_base_fraction") +
                Mathf.Sin(Time.time * _context.N("flight_modes.trailing_flight.position_variation.distance_rate")) *
                _context.N("flight_modes.trailing_flight.position_variation.distance_amplitude_fraction"));
            distance += HeightDistance(altitude);
            Vector3 radial = _context.HasTrailingViewDirection
                ? _context.TrailingViewDirection : -direction;
            Vector3 right = Vector3.Cross(Vector3.up, radial);
            float lateral = Mathf.Sin(Time.time *
                _context.N("flight_modes.trailing_flight.position_variation.lateral_rate")) *
                _context.N("flight_modes.trailing_flight.position_variation.lateral_amplitude");
            Vector3 target = playerPosition + radial * distance +
                             right * lateral;
            target.y = GroundHeight(target) + altitude;
            return target;
        }

        // Moves toward the trailing target even when the player stands still.
        internal float GetSpeed(Vector3 dronePosition, Vector3 target)
        {
            float playerSpeed = Flatten(_context.TargetVelocity).magnitude;
            float error = Flatten(target - dronePosition).magnitude;
            float bonus = Mathf.Min(
                error * _context.N("flight_modes.trailing_flight.catch_up.position_error_speed_gain"),
                _context.N("flight_modes.trailing_flight.catch_up.max_bonus"));
            if (!_context.HasTrailingViewDirection &&
                playerSpeed > _context.N("flight_modes.trailing_flight.catch_up.moving_target_speed_threshold"))
            {
                Vector3 travel = Flatten(_context.TargetVelocity).normalized;
                float behind = Vector3.Dot(
                    Flatten(_context.Target.transform.position - dronePosition),
                    travel);
                bonus *= Mathf.InverseLerp(
                    _context.N("flight_modes.trailing_flight.catch_up.behind_bonus_fade_start"),
                    _context.N("flight_modes.trailing_flight.catch_up.behind_bonus_fade_end"), behind);
            }
            float desiredSpeed = Mathf.Min(
                _context.MaxSpeed * _context.N("flight_modes.trailing_flight.catch_up.maximum_speed_multiplier"),
                playerSpeed + bonus);
            if (!_speedInitialized)
            {
                _commandedSpeed = playerSpeed;
                _speedInitialized = true;
            }
            _commandedSpeed = Mathf.MoveTowards(
                _commandedSpeed, desiredSpeed,
                (desiredSpeed < _commandedSpeed
                    ? _context.N("flight_modes.trailing_flight.catch_up.brake_response") : _context.N("flight_modes.trailing_flight.catch_up.speed_response")) *
                Time.deltaTime);
            return _commandedSpeed;
        }

        // Prefers low flight while respecting the distance-based height cap.
        private float GetAltitude(float minimum, float maximum)
        {
            float amount = _context.B("flight_modes.trailing_flight.altitude_wave.enabled")
                ? 0.5f + Mathf.Sin(Time.time *
                    _context.N("flight_modes.trailing_flight.altitude_wave.cycle_rate")) * 0.5f
                : 0.5f;
            float desired = Mathf.Min(
                maximum, minimum + amount * _context.N("flight_modes.trailing_flight.altitude_wave.preferred_variation"));
            if (!_altitudeInitialized)
            {
                _altitude = desired;
                _altitudeInitialized = true;
            }
            _altitude = Mathf.SmoothDamp(
                _altitude, desired, ref _altitudeVelocity,
                _context.N("flight_modes.trailing_flight.altitude_wave.smooth_time"), _context.N("flight_modes.trailing_flight.altitude_wave.max_change_speed"));
            return _altitude;
        }

        // Converts height above ground into extra room behind the player.
        private float HeightDistance(float altitude)
        {
            return Mathf.InverseLerp(
                _context.N("flight_modes.trailing_flight.distances.blend_start"), _context.N("flight_modes.trailing_flight.distances.blend_end"), altitude) *
                _context.N("flight_modes.trailing_flight.distances.high_altitude_extra");
        }

        // Reads terrain height beneath a trailing destination.
        private float GroundHeight(Vector3 position)
        {
            return _context.Ground(position);
        }

        // Removes the vertical component from a velocity or offset.
        private Vector3 Flatten(Vector3 value)
        {
            value.y = 0f;
            return value;
        }
    }
}
