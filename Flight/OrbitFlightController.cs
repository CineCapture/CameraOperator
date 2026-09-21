using UnityEngine;

namespace DronePilot
{
    // Plans and advances the drone's terrain-aware orbit.
    internal sealed class OrbitFlightController
    {
        private readonly FlightContext _context;

        // Binds the flight policy to one drone session.
        internal OrbitFlightController(FlightContext context)
        {
            _context = context;
        }
        private float _angle;
        private float _radius;
        private float _preferredRadius;
        private float _radiusVelocity;
        private float _slopeX;
        private float _slopeZ;
        private float _direction = 1f;
        private bool _planeInitialized;

        internal Vector3 ProbeTarget { get; private set; }

        // Starts an orbit at the drone's current radial angle.
        internal void Enter(Vector3 playerPosition, Vector3 dronePosition,
                            bool alternateDirection)
        {
            if (alternateDirection)
            {
                _direction *= -1f;
            }
            Vector3 radial = dronePosition - playerPosition;
            radial.y = 0f;
            _angle = Mathf.Atan2(radial.z, radial.x);
            _radius = Mathf.Max(_context.N("flight_modes.orbit_flight.height_constraints.minimum_radius"), radial.magnitude);
            _preferredRadius = _radius;
            _radiusVelocity = 0f;
            _planeInitialized = false;
        }

        // Advances the orbit and returns its smooth look-ahead destination.
        internal Vector3 Update(Vector3 travelDirection,
                                out float targetSpeed)
        {
            float permittedRadius = Mathf.Clamp(
                _preferredRadius, _context.N("flight_modes.orbit_flight.height_constraints.minimum_radius"),
                _context.Profile.MaximumOrbitRadius);
            _radius = Mathf.SmoothDamp(
                _radius, permittedRadius, ref _radiusVelocity,
                _context.N("flight_modes.orbit_flight.radius_adaptation.smooth_time"), _context.N("flight_modes.orbit_flight.radius_adaptation.max_speed"));
            if (!_planeInitialized)
            {
                InitializePlan();
            }
            Vector3 radial = new Vector3(
                Mathf.Cos(_angle), 0f, Mathf.Sin(_angle));
            float frontAmount = Vector3.Dot(radial, travelDirection) *
                                0.5f + 0.5f;
            targetSpeed = _context.N("flight_modes.orbit_flight.speed_control.speed") * Mathf.Lerp(
                _context.N("flight_modes.orbit_flight.speed_control.rear_speed_multiplier"), _context.N("flight_modes.orbit_flight.speed_control.front_speed_multiplier"), frontAmount);
            _angle += _direction * targetSpeed /
                      Mathf.Max(_radius,
                          _context.N("flight_modes.orbit_flight.speed_control.minimum_angular_radius")) *
                      Time.deltaTime;
            Vector3 target = GetTargetHeight(
                radial);
            Vector3 tangent = new Vector3(-radial.z, 0f, radial.x) *
                              _direction;
            target += tangent * targetSpeed * _context.N("flight_modes.orbit_flight.speed_control.motion_lead_seconds");
            ProbeTarget = target + tangent * targetSpeed *
                          _context.N("flight_modes.orbit_flight.speed_control.anticipation_seconds");
            return target;
        }

        // Positions one orbit target at eye height with terrain limits.
        private Vector3 GetTargetHeight(Vector3 radial)
        {
            Vector3 center = _context.Target.transform.position;
            Vector3 target = center + radial * _radius;
            float closeBlend = Mathf.SmoothStep(
                0f, 1f, Mathf.InverseLerp(
                    _context.N("flight_modes.orbit_flight.height_constraints.close_distance"), _context.N("flight_modes.orbit_flight.height_constraints.close_distance") + _context.N("flight_modes.orbit_flight.height_constraints.close_height_blend_distance"),
                    _radius));
            float maximumHeight = Mathf.Lerp(
                Mathf.Min(_context.N("flight_modes.orbit_flight.height_constraints.close_maximum_height"),
                    _context.Profile.MaximumHeight),
                _context.Profile.MaximumHeight, closeBlend);
            float preferredHeight = _context.Focus.y;
            preferredHeight += _slopeX * (target.x - center.x) +
                               _slopeZ * (target.z - center.z);
            target.y = Mathf.Clamp(preferredHeight,
                GroundHeight(target) + _context.N("flight_modes.orbit_flight.height_constraints.minimum_height"),
                GroundHeight(target) + maximumHeight);
            return target;
        }

        // Chooses the least blocked complete ellipse before the orbit starts.
        private void InitializePlan()
        {
            Vector3 center = _context.Target.transform.position;
            float preferredRadius = Mathf.Clamp(
                _preferredRadius, _context.N("flight_modes.orbit_flight.height_constraints.minimum_radius"),
                _context.Profile.MaximumOrbitRadius);
            float headHeight = _context.Focus.y;
            int bestObstacles = int.MaxValue;
            float bestDistance = float.MaxValue;
            for (int index = 0; index < _context.I("flight_modes.orbit_flight.plane_fitting.radius_candidates"); index++)
            {
                float amount = index / (_context.I("flight_modes.orbit_flight.plane_fitting.radius_candidates") - 1f);
                float radius = Mathf.Lerp(
                    _context.N("flight_modes.orbit_flight.height_constraints.minimum_radius"),
                    _context.Profile.MaximumOrbitRadius, amount);
                Vector2 slope = FitPlane(center, radius);
                int obstacles = CountObstacles(
                    center, headHeight, radius, slope);
                float distance = Mathf.Abs(radius - preferredRadius);
                if (obstacles < bestObstacles ||
                    obstacles == bestObstacles && distance < bestDistance)
                {
                    bestObstacles = obstacles;
                    bestDistance = distance;
                    _preferredRadius = radius;
                    _slopeX = slope.x;
                    _slopeZ = slope.y;
                }
            }
            _planeInitialized = true;
            LogPlan(bestObstacles);
        }

        // Fits one terrain plane from a complete candidate orbit.
        private Vector2 FitPlane(Vector3 center, float radius)
        {
            float sumXHeight = 0f;
            float sumZHeight = 0f;
            float sumSquared = 0f;
            for (int index = 0; index < _context.I("flight_modes.orbit_flight.plane_fitting.terrain_samples"); index++)
            {
                float angle = Mathf.PI * 2f * index / _context.I("flight_modes.orbit_flight.plane_fitting.terrain_samples");
                float x = Mathf.Cos(angle) * radius;
                float z = Mathf.Sin(angle) * radius;
                float height = GroundHeight(
                    center + new Vector3(x, 0f, z));
                sumXHeight += x * height;
                sumZHeight += z * height;
                sumSquared += x * x;
            }
            return sumSquared >
                _context.N("numerical_tolerances.direction_squared")
                ? new Vector2(
                    sumXHeight / sumSquared,
                    sumZHeight / sumSquared)
                : Vector2.zero;
        }

        // Counts blocked segments on a full candidate ellipse.
        private int CountObstacles(
            Vector3 center, float headHeight, float radius, Vector2 slope)
        {
            int obstacles = 0;
            Vector3 previous = GetPlannedPoint(
                center, headHeight, radius, slope, _context.I("flight_modes.orbit_flight.plane_fitting.terrain_samples") - 1);
            for (int index = 0; index < _context.I("flight_modes.orbit_flight.plane_fitting.terrain_samples"); index++)
            {
                Vector3 point = GetPlannedPoint(
                    center, headHeight, radius, slope, index);
                if (DroneTrajectoryPlanner.IsRouteBlocked(
                    _context, previous, point))
                {
                    obstacles++;
                }
                previous = point;
            }
            return obstacles;
        }

        // Creates one point on a terrain-inclined candidate ellipse.
        private Vector3 GetPlannedPoint(
            Vector3 center, float headHeight, float radius,
            Vector2 slope, int index)
        {
            float angle = Mathf.PI * 2f * index / _context.I("flight_modes.orbit_flight.plane_fitting.terrain_samples");
            float x = Mathf.Cos(angle) * radius;
            float z = Mathf.Sin(angle) * radius;
            return new Vector3(center.x + x,
                headHeight + slope.x * x + slope.y * z, center.z + z);
        }

        // Logs the planned ellipse incline for trajectory diagnostics.
        private void LogPlan(int obstacleCount)
        {
            float slope = Mathf.Sqrt(_slopeX * _slopeX + _slopeZ * _slopeZ);
            float angle = Mathf.Atan(slope) * Mathf.Rad2Deg;
            _context.World.LogInfo?.Invoke(
                $"Orbit ellipse planned: incline={angle:F1} degrees, " +
                $"radius={_radius:F2}m, obstacles={obstacleCount}, " +
                $"slope=({_slopeX:F3}, {_slopeZ:F3}).");
        }

        // Reads terrain height or preserves the input height if unavailable.
        private float GroundHeight(Vector3 position)
        {
            return _context.Ground(position);
        }
    }
}
