using UnityEngine;

namespace CameraOperator
{
    // Anticipates terrain and selects simple lateral obstacle detours.
    internal sealed class CameraPathPlanner
    {
        private readonly CameraContext _context;

        // Binds the camera movement policy to one camera session.
        internal CameraPathPlanner(CameraContext context)
        {
            _context = context;
        }
        private Vector3 _plannedTarget;
        private float _nextRefreshTime;
        private float _lastRefreshTime;
        private float _smoothedTerrainLift;
        private float _terrainLiftVelocity;
        private string _lastAvoidanceDecision;
        private float _nextAvoidanceLogTime;
        private int _activeObstacleId;
        private float _avoidanceSide;
        private float _avoidanceSideHoldUntil;
        private bool _initialized;
        internal bool EmergencyAvoidance { get; private set; }

        // Clears cached routes and avoidance state after a camera cut.
        internal void Reset()
        {
            _initialized = false;
            _plannedTarget = Vector3.zero;
            _smoothedTerrainLift = 0f;
            _terrainLiftVelocity = 0f;
            _lastAvoidanceDecision = null;
            _activeObstacleId = 0;
            _avoidanceSide = 0f;
            EmergencyAvoidance = false;
        }

        // Reuses one plan for 100 ms before simulating routes again.
        internal Vector3 Plan(
            Vector3 origin, Vector3 desired, Vector3 probeTarget,
            Vector3 velocity,
            float terrainClearance)
        {
            if (_initialized && Time.time < _nextRefreshTime)
            {
                return _plannedTarget;
            }

            float lookAhead = Mathf.Max(
                _context.N("obstacle_avoidance.minimum_look_ahead"), velocity.magnitude * _context.N("obstacle_avoidance.look_ahead_seconds"));
            float requiredLift = GetRequiredTerrainLift(
                origin, probeTarget, lookAhead, terrainClearance);
            float smoothTime = requiredLift > _smoothedTerrainLift
                ? _context.N("terrain_following.rise_smooth_time")
                : _context.N("terrain_following.fall_smooth_time");
            float elapsed = _initialized
                ? Mathf.Max(CameraConstants.MinimumRefreshElapsed,
                    Time.time - _lastRefreshTime)
                : _context.N("obstacle_avoidance.refresh_interval");
            _smoothedTerrainLift = Mathf.SmoothDamp(
                _smoothedTerrainLift, requiredLift,
                ref _terrainLiftVelocity, smoothTime,
                Mathf.Infinity, elapsed);
            Vector3 target = desired + Vector3.up * _smoothedTerrainLift;
            EmergencyAvoidance = false;
            Vector3 routeTarget = target;
            Vector3 reactiveProbe = probeTarget;
            reactiveProbe = GetReactiveProbe(
                origin, probeTarget, velocity, lookAhead,
                out bool followsMomentum);
            if (followsMomentum)
            {
                routeTarget = reactiveProbe;
                routeTarget.y = target.y;
            }
            _plannedTarget = ChooseObstacleRoute(
                origin, routeTarget, reactiveProbe,
                target, lookAhead, terrainClearance);
            _lastRefreshTime = Time.time;
            _nextRefreshTime = Time.time + _context.N("obstacle_avoidance.refresh_interval");
            _initialized = true;
            return _plannedTarget;
        }

        // Prioritizes the path the moving camera cannot instantly leave.
        private Vector3 GetReactiveProbe(
            Vector3 origin, Vector3 desiredProbe, Vector3 velocity,
            float lookAhead, out bool followsMomentum)
        {
            followsMomentum = false;
            if (velocity.sqrMagnitude <
                _context.N("obstacle_avoidance.momentum_speed_squared"))
            {
                return desiredProbe;
            }

            Vector3 momentumProbe = origin + velocity.normalized * lookAhead;
            if (!TryGetObstacle(
                    origin, momentumProbe, lookAhead,
                    out _, out float obstacleDistance))
            {
                return desiredProbe;
            }

            // A close obstacle needs faster steering than normal movement.
            EmergencyAvoidance = obstacleDistance <
                _context.N("obstacle_avoidance.emergency_distance");
            followsMomentum = true;
            return momentumProbe;
        }

        // Chooses the first clear lateral route or keeps the direct route.
        private Vector3 ChooseObstacleRoute(
            Vector3 origin, Vector3 target, Vector3 probeTarget,
            Vector3 fallbackTarget, float lookAhead,
            float terrainClearance)
        {
            if (!TryGetObstacle(
                    origin, probeTarget, lookAhead,
                    out Collider obstacle, out float obstacleDistance))
            {
                LogDirectPathRestored();
                ClearExpiredAvoidanceSide();
                return target;
            }
            EmergencyAvoidance |= obstacleDistance <
                _context.N("obstacle_avoidance.emergency_distance");
            PrepareAvoidanceSide(obstacle);

            Vector3 direction = target - origin;
            direction.y = 0f;
            if (direction.sqrMagnitude < CameraConstants.DirectionSquared)
            {
                return target;
            }

            Vector3 right = Vector3.Cross(Vector3.up, direction.normalized);
            string offsetsPath =
                "obstacle_avoidance.route_offsets";
            for (int index = 0; index < _context.Config.Count(offsetsPath); index++)
            {
                Vector2 offset = new Vector2(
                    _context.N($"{offsetsPath}[{index}][0]"),
                    _context.N($"{offsetsPath}[{index}][1]"));
                if (_avoidanceSide != 0f && offset.x != 0f &&
                    Mathf.Sign(offset.x) != _avoidanceSide)
                {
                    continue;
                }
                Vector3 candidate = target + right * offset.x +
                                    Vector3.up * offset.y;
                candidate = RaiseForTerrain(
                    origin, candidate, lookAhead, terrainClearance);
                if (!HasObstacle(origin, candidate, lookAhead))
                {
                    if (_avoidanceSide == 0f && offset.x != 0f)
                    {
                        _avoidanceSide = Mathf.Sign(offset.x);
                    }
                    LogAvoidance(obstacle, DescribeManeuver(offset));
                    return candidate;
                }
            }

            if (HasCharacterObstacle(origin, probeTarget, lookAhead))
            {
                LogAvoidance(obstacle, "actor retreat");
                Vector3 retreat = origin - direction.normalized *
                                  _context.N("obstacle_avoidance.actor_retreat_distance");
                return RaiseForTerrain(
                    origin, retreat, lookAhead, terrainClearance);
            }

            LogAvoidance(obstacle, "no clear detour; continuing direct");
            return fallbackTarget;
        }

        // Keeps one lateral side while passing the same obstacle.
        private void PrepareAvoidanceSide(Collider obstacle)
        {
            int obstacleId = obstacle.GetInstanceID();
            if (_activeObstacleId == obstacleId)
            {
                return;
            }

            _activeObstacleId = obstacleId;
            _avoidanceSide = 0f;
            _avoidanceSideHoldUntil = 0f;
        }

        // Describes one combined lateral and vertical avoidance maneuver.
        private string DescribeManeuver(Vector2 offset)
        {
            string side = offset.x < 0f
                ? $"left {Mathf.Abs(offset.x):F1}m"
                : offset.x > 0f
                    ? $"right {offset.x:F1}m"
                    : "straight";
            return offset.y > 0f
                ? $"{side} + climb {offset.y:F1}m"
                : side;
        }

        // Logs a changed avoidance decision and periodically repeats it.
        private void LogAvoidance(Collider obstacle, string maneuver)
        {
            string kind = obstacle == null ? "Unknown" :
                _context.World.IsActor?.Invoke(obstacle) == true
                    ? "Actor" : obstacle.GetType().Name;
            string name = obstacle != null ? obstacle.name : "unknown";
            string decision = $"{kind}:{name}:{maneuver}";
            if (decision == _lastAvoidanceDecision &&
                Time.time < _nextAvoidanceLogTime)
            {
                return;
            }

            _context.World.LogWarning?.Invoke(
                $"Camera avoidance: obstacle={kind} '{name}', " +
                $"maneuver={maneuver}.");
            _lastAvoidanceDecision = decision;
            _nextAvoidanceLogTime = Time.time +
                _context.N("obstacle_avoidance.avoidance_side_hold");
        }

        // Logs when a previous avoidance ends and direct movement resumes.
        private void LogDirectPathRestored()
        {
            if (string.IsNullOrEmpty(_lastAvoidanceDecision) ||
                _lastAvoidanceDecision == "direct")
            {
                return;
            }

            _context.World.LogInfo?.Invoke(
                "Camera avoidance ended: direct trajectory restored.");
            _lastAvoidanceDecision = "direct";
            _avoidanceSideHoldUntil = Time.time +
                _context.N("obstacle_avoidance.avoidance_side_hold");
        }

        // Clears a completed avoidance only after a stable clear interval.
        private void ClearExpiredAvoidanceSide()
        {
            if (_lastAvoidanceDecision == "direct" &&
                Time.time >= _avoidanceSideHoldUntil)
            {
                _activeObstacleId = 0;
                _avoidanceSide = 0f;
            }
        }

        // Detects a character so a failed detour never continues through it.
        private bool HasCharacterObstacle(
            Vector3 origin, Vector3 target, float lookAhead)
        {
            Vector3 movement = target - origin;
            float distance = Mathf.Min(movement.magnitude, lookAhead);
            if (distance < CameraConstants.MinimumSegmentLength)
            {
                return false;
            }

            RaycastHit[] hits = Physics.SphereCastAll(
                origin, _context.N("obstacle_avoidance.camera_radius"), movement.normalized, distance,
                Physics.AllLayers, QueryTriggerInteraction.Ignore);
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider != null &&
                    _context.World.IsActor?.Invoke(hit.collider) == true)
                {
                    return true;
                }
            }

            return false;
        }

        // Detects scenery along the speed-scaled start of one route.
        private bool HasObstacle(
            Vector3 origin, Vector3 target, float lookAhead)
        {
            return TryGetObstacle(origin, target, lookAhead, out _, out _);
        }

        // Returns the nearest collider blocking one simulated route.
        private bool TryGetObstacle(
            Vector3 origin, Vector3 target, float lookAhead,
            out Collider obstacle, out float obstacleDistance)
        {
            obstacle = null;
            obstacleDistance = float.MaxValue;
            Vector3 movement = target - origin;
            float distance = Mathf.Min(movement.magnitude, lookAhead);
            if (distance < CameraConstants.MinimumSegmentLength)
            {
                return false;
            }

            RaycastHit[] hits = Physics.SphereCastAll(
                origin, _context.N("obstacle_avoidance.camera_radius"), movement.normalized, distance,
                Physics.AllLayers, QueryTriggerInteraction.Ignore);
            foreach (RaycastHit hit in hits)
            {
                if (IsObstacle(hit.collider) &&
                    hit.distance < obstacleDistance)
                {
                    obstacle = hit.collider;
                    obstacleDistance = hit.distance;
                }
            }

            return obstacle != null;
        }

        // Treats characters as obstacles and leaves terrain to height sampling.
        private bool IsObstacle(Collider collider)
        {
            return collider != null &&
                _context.World.IgnoreObstacle?.Invoke(collider) != true;
        }

        // Raises the target when any sampled route point approaches terrain.
        private Vector3 RaiseForTerrain(
            Vector3 origin, Vector3 target, float lookAhead,
            float terrainClearance)
        {
            target.y += GetRequiredTerrainLift(
                origin, target, lookAhead, terrainClearance);
            return target;
        }

        // Returns the lift required by all sampled future terrain points.
        private float GetRequiredTerrainLift(
            Vector3 origin, Vector3 target, float lookAhead,
            float terrainClearance)
        {
            Vector3 route = target - origin;
            float distance = Mathf.Min(route.magnitude, lookAhead);
            if (distance < CameraConstants.MinimumSegmentLength)
            {
                return 0f;
            }

            float requiredLift = 0f;
            for (int index = 1; index <= _context.N("terrain_following.samples"); index++)
            {
                float amount = distance * index / _context.N("terrain_following.samples");
                Vector3 point = origin + route.normalized * amount;
                if (TryGroundHeight(point, out float ground))
                {
                    requiredLift = Mathf.Max(
                        requiredLift, ground + terrainClearance - point.y);
                }
            }

            return Mathf.Max(0f, requiredLift);
        }

        // Reads the heightmap elevation below one point.
        private bool TryGroundHeight(
            Vector3 position, out float groundHeight)
        {
            float? height = _context.World.GroundHeight?.Invoke(position);
            groundHeight = height ?? 0f;
            return height.HasValue;
        }
    }
}
