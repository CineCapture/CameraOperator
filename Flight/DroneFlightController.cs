using UnityEngine;

namespace DronePilot
{
    // Switches between orbit and trailing without owning either flight path.
    internal sealed class DroneFlightController
    {
        private readonly FlightContext _context;

        // Binds the flight policy to one drone session.
        internal DroneFlightController(FlightContext context)
        {
            _context = context;
            _orbit = new OrbitFlightController(context);
            _trailing = new TrailingFlightController(context);
        }
        private readonly OrbitFlightController _orbit;
        private readonly TrailingFlightController _trailing;
        private DroneFlightMode _mode;
        private Vector3 _travelDirection = Vector3.forward;
        private bool _initialized;

        internal DroneFlightMode Mode => _mode;
        internal Vector3 TrajectoryProbeTarget { get; private set; }

        // Predicts the trailing route without involving the orbit controller.
        internal Vector3 PlanTrailingRoute(
            Vector3 origin, Vector3 target, Vector3 droneVelocity,
            Vector3 playerPosition, Vector3 playerVelocity)
        {
            return _trailing.PlanRoute(
                origin, target, droneVelocity,
                playerPosition, playerVelocity);
        }

        // Returns the active mode's terrain clearance at the current speed.
        internal float GetTerrainClearance(float speed)
        {
            float blend = Mathf.InverseLerp(
                _context.N("flight_modes.orbit_flight.speed_control.speed"),
                _context.N("flight_control.clearance_speed.full_terrain_clearance_speed"), speed);
            return _mode == DroneFlightMode.TrailingFlight
                ? Mathf.Lerp(
                    _context.N("flight_modes.trailing_flight.avoidance.terrain_following.slow_clearance"), _context.N("flight_modes.trailing_flight.avoidance.terrain_following.minimum_height"), blend)
                : Mathf.Lerp(
                    _context.N("flight_modes.orbit_flight.height_constraints.minimum_height"), _context.N("flight_modes.trailing_flight.avoidance.terrain_following.minimum_height"), blend);
        }

        // Selects a mode and delegates its desired position and speed.
        internal Vector3 Update(
            Vector3 dronePosition, float droneSpeed,
            out float targetSpeed)
        {
            Vector3 playerPosition = _context.Target.transform.position;
            Vector3 playerVelocity = Flatten(_context.TargetVelocity);
            UpdateTravelDirection(playerVelocity);
            SelectMode(playerPosition, dronePosition,
                       playerVelocity.magnitude, droneSpeed,
                       _context.Profile.MaximumHeight);
            if (_mode == DroneFlightMode.TrailingFlight)
            {
                Vector3 target = _trailing.GetTarget(
                    playerPosition, dronePosition, _travelDirection,
                    _context.Profile,
                    GetTerrainClearance(droneSpeed));
                targetSpeed = _trailing.GetSpeed(
                    dronePosition, target);
                TrajectoryProbeTarget = target;
                return target;
            }

            Vector3 orbitTarget = _orbit.Update(
                _travelDirection, out targetSpeed);
            TrajectoryProbeTarget = _orbit.ProbeTarget;
            return orbitTarget;
        }

        // Chooses a stable mode with distance and speed hysteresis.
        private void SelectMode(
            Vector3 playerPosition, Vector3 dronePosition,
            float playerSpeed, float droneSpeed,
            float maximumOrbitHeight)
        {
            float distance = Flatten(
                dronePosition - playerPosition).magnitude;
            DroneFlightMode next = _mode;
            if (!_initialized)
            {
                next = distance >= _context.N("flight_control.mode_switching.trailing_entry_distance") ||
                       dronePosition.y - playerPosition.y >
                           maximumOrbitHeight
                    ? DroneFlightMode.TrailingFlight
                    : DroneFlightMode.OrbitFlight;
                InitializeMode(next, playerPosition, dronePosition);
                return;
            }
            if (_mode == DroneFlightMode.OrbitFlight &&
                distance >= _context.N("flight_control.mode_switching.trailing_entry_distance"))
            {
                next = DroneFlightMode.TrailingFlight;
            }
            else if (_mode == DroneFlightMode.TrailingFlight &&
                     distance <= _context.N("flight_control.mode_switching.orbit_return_distance") &&
                     dronePosition.y - playerPosition.y <=
                         maximumOrbitHeight &&
                     playerSpeed <= _context.N("flight_modes.orbit_flight.speed_control.speed") &&
                     droneSpeed <= _context.N("flight_control.mode_switching.orbit_entry_maximum_drone_speed"))
            {
                next = DroneFlightMode.OrbitFlight;
            }
            ChangeMode(next, playerPosition, dronePosition);
        }

        // Initializes the first mode without alternating orbit direction.
        private void InitializeMode(
            DroneFlightMode mode, Vector3 playerPosition,
            Vector3 dronePosition)
        {
            _mode = mode;
            _initialized = true;
            if (mode == DroneFlightMode.OrbitFlight)
            {
                _orbit.Enter(playerPosition, dronePosition, false);
            }
            _context.World.LogInfo?.Invoke(
                $"Drone flight initialized: {mode}.");
            _context.Event?.Invoke("flight_initialized", mode.ToString());
        }

        // Logs mode changes and alternates the next orbit direction.
        private void ChangeMode(
            DroneFlightMode next, Vector3 playerPosition,
            Vector3 dronePosition)
        {
            if (next == _mode)
            {
                return;
            }
            DroneFlightMode previous = _mode;
            _mode = next;
            if (previous == DroneFlightMode.TrailingFlight)
            {
                _trailing.Leave();
            }
            if (next == DroneFlightMode.OrbitFlight)
            {
                _orbit.Enter(playerPosition, dronePosition, true);
            }
            _context.World.LogInfo?.Invoke(
                $"Drone flight changed: {previous} -> {next}.");
            _context.Event?.Invoke("flight_mode_changed",
                $"{previous} -> {next}");
        }

        // Updates the horizontal direction from meaningful player velocity.
        private void UpdateTravelDirection(Vector3 velocity)
        {
            if (velocity.sqrMagnitude >
                _context.N("flight_control.meaningful_target_velocity_squared"))
            {
                _travelDirection = velocity.normalized;
                return;
            }
            Vector3 forward = Flatten(_context.Target.transform.forward);
            if (forward.sqrMagnitude >
                _context.N("numerical_tolerances.direction_squared"))
            {
                _travelDirection = forward.normalized;
            }
        }

        // Removes vertical movement from one vector.
        private Vector3 Flatten(Vector3 value)
        {
            value.y = 0f;
            return value;
        }
    }
}
