using UnityEngine;

namespace DronePilot
{
    // Preplans short curved trailing routes from drone and player motion.
    internal sealed class TrailingRoutePlanner
    {
        private readonly FlightContext _context;

        // Binds the flight policy to one drone session.
        internal TrailingRoutePlanner(FlightContext context)
        {
            _context = context;
        }
        private Vector3 _waypoint;
        private float _nextRefreshTime;
        private float _preferredSide;
        private bool _initialized;
        private bool _directRoute;

        // Returns a stable near-term waypoint on the clearest predicted route.
        internal Vector3 Plan(
            Vector3 origin, Vector3 desired, Vector3 droneVelocity,
            Vector3 playerPosition, Vector3 playerVelocity)
        {
            float speed = Mathf.Max(
                droneVelocity.magnitude, playerVelocity.magnitude);
            float horizon = Mathf.Clamp(
                _context.N("flight_modes.trailing_flight.planner.base_horizon") +
                speed * _context.N("flight_modes.trailing_flight.planner.horizon_speed_gain"),
                _context.N("flight_modes.trailing_flight.planner.minimum_horizon"),
                _context.N("flight_modes.trailing_flight.planner.maximum_horizon"));
            Vector3 endpoint = desired + Flatten(playerVelocity) * horizon;
            endpoint = KeepBehindPlayer(
                endpoint, playerPosition, playerVelocity);
            if (_initialized && Time.time < _nextRefreshTime)
            {
                return _directRoute ? endpoint : _waypoint;
            }
            Vector3 tangent = GetInitialTangent(
                origin, endpoint, droneVelocity, horizon);
            _waypoint = SelectRoute(origin, endpoint, tangent);
            _nextRefreshTime = Time.time + _context.N("flight_modes.trailing_flight.planner.refresh_interval");
            _initialized = true;
            return _waypoint;
        }

        // Prevents prediction from putting the trailing target ahead.
        private Vector3 KeepBehindPlayer(
            Vector3 endpoint, Vector3 playerPosition,
            Vector3 playerVelocity)
        {
            Vector3 travel = Flatten(playerVelocity);
            if (travel.sqrMagnitude <
                _context.N("flight_control.meaningful_target_velocity_squared"))
            {
                return endpoint;
            }
            Vector3 direction = travel.normalized;
            float behind = Vector3.Dot(
                Flatten(playerPosition - endpoint), direction);
            if (behind < _context.N("flight_modes.trailing_flight.planner.minimum_predicted_behind_distance"))
            {
                endpoint -= direction *
                    (_context.N("flight_modes.trailing_flight.planner.minimum_predicted_behind_distance") - behind);
            }
            return endpoint;
        }

        internal void Reset()
        {
            _initialized = false;
            _preferredSide = 0f;
            _directRoute = false;
        }

        // Preserves current momentum while still bending toward the player.
        private Vector3 GetInitialTangent(
            Vector3 origin, Vector3 endpoint, Vector3 velocity, float horizon)
        {
            if (velocity.sqrMagnitude >
                _context.N("flight_control.meaningful_target_velocity_squared"))
            {
                return origin + velocity * horizon *
                    _context.N("flight_modes.trailing_flight.planner.initial_tangent_momentum_fraction");
            }

            return Vector3.Lerp(origin, endpoint,
                _context.N("flight_modes.trailing_flight.planner.initial_tangent_momentum_fraction"));
        }

        // Chooses a clear quadratic path, preferring the previous side.
        private Vector3 SelectRoute(
            Vector3 origin, Vector3 endpoint, Vector3 tangent)
        {
            Vector3 direction = Flatten(endpoint - origin);
            Vector3 right = direction.sqrMagnitude >
                _context.N("numerical_tolerances.direction_squared")
                ? Vector3.Cross(Vector3.up, direction.normalized)
                : Vector3.right;
            foreach (float offset in OrderedOffsets())
            {
                Vector3 control = tangent + right * offset;
                if (IsClear(origin, control, endpoint))
                {
                    if (Mathf.Abs(offset) <
                        _context.N("numerical_tolerances.direct_route_offset"))
                    {
                        _preferredSide = 0f;
                        _directRoute = true;
                        return endpoint;
                    }
                    _directRoute = false;
                    if (Mathf.Abs(offset) >
                        _context.N("numerical_tolerances.direct_route_offset"))
                    {
                        _preferredSide = Mathf.Sign(offset);
                    }
                    return Bezier(origin, control, endpoint, _context.N("flight_modes.trailing_flight.planner.waypoint_progress"));
                }
            }

            _directRoute = false;
            return endpoint;
        }

        // Tests the previous avoidance side first to prevent oscillation.
        private float[] OrderedOffsets()
        {
            float[] offsets = _context.Config.Numbers(
                "flight_modes.trailing_flight.planner.lateral_offsets");
            if (_preferredSide == 0f)
            {
                return offsets;
            }
            System.Array.Sort(offsets, (a, b) =>
            {
                if (a == 0f) return -1;
                if (b == 0f) return 1;
                bool aSide = Mathf.Sign(a) == _preferredSide;
                bool bSide = Mathf.Sign(b) == _preferredSide;
                return aSide == bSide ? Mathf.Abs(a).CompareTo(Mathf.Abs(b))
                    : aSide ? -1 : 1;
            });
            return offsets;
        }

        // Samples the complete curve with the camera collision radius.
        private bool IsClear(
            Vector3 origin, Vector3 control, Vector3 endpoint)
        {
            Vector3 previous = origin;
            for (int index = 1; index <= _context.N("flight_modes.trailing_flight.planner.route_samples"); index++)
            {
                float amount = index / (float)_context.N("flight_modes.trailing_flight.planner.route_samples");
                Vector3 point = Bezier(origin, control, endpoint, amount);
                if (DroneTrajectoryPlanner.IsRouteBlocked(
                    _context, previous, point))
                {
                    return false;
                }
                previous = point;
            }

            return true;
        }

        private Vector3 Bezier(
            Vector3 start, Vector3 control, Vector3 end, float amount)
        {
            float inverse = 1f - amount;
            return inverse * inverse * start +
                   2f * inverse * amount * control +
                   amount * amount * end;
        }

        private Vector3 Flatten(Vector3 value)
        {
            value.y = 0f;
            return value;
        }
    }
}
