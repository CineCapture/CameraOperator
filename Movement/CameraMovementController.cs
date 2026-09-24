using UnityEngine;

namespace CameraOperator
{
    // Keeps a mobile camera at a world-locked angle around the player.
    internal sealed class CameraMovementController
    {
        private readonly CameraContext _context;
        private Vector3 _travelDirection = Vector3.forward;

        // Binds the camera movement policy to one camera session.
        internal CameraMovementController(CameraContext context)
        {
            _context = context;
        }
        private float _commandedSpeed;
        private bool _speedInitialized;

        // Clears smoothing state after an intentional camera cut.
        internal void Reset()
        {
            _speedInitialized = false;
        }

        // Calculates the world-anchored destination and requested speed.
        internal Vector3 Update(
            Vector3 cameraPosition, float cameraSpeed, out float targetSpeed)
        {
            UpdateTravelDirection(Flatten(_context.TargetVelocity));
            Vector3 target = GetTarget(
                _context.Target.transform.position, cameraPosition,
                _travelDirection, GetTerrainClearance(cameraSpeed));
            targetSpeed = GetSpeed(cameraPosition, target);
            return target;
        }

        // Returns terrain clearance blended from slow to full movement speed.
        internal float GetTerrainClearance(float speed)
        {
            float blend = Mathf.InverseLerp(0f, _context.N(
                "terrain_following." +
                "full_clearance_speed"), speed);
            return Mathf.Lerp(_context.N(
                "terrain_following." +
                "slow_clearance"), _context.N(
                "terrain_following." +
                "minimum_height"), blend);
        }

        // Builds a destination with a fixed world angle and initial distance.
        private Vector3 GetTarget(
            Vector3 playerPosition, Vector3 cameraPosition,
            Vector3 direction, float terrainClearance)
        {
            EnsureAnchor(playerPosition, cameraPosition, direction);
            Vector3 target = playerPosition +
                             _context.CameraDirectionWorld *
                             _context.CameraDistance;
            float preferredHeight = Mathf.Min(
                _context.CameraHeightAboveGround,
                _context.Profile.MaximumHeight);
            float height = Mathf.Max(preferredHeight, terrainClearance);
            target.y = GroundHeight(target) + height;
            return target;
        }

        // Moves toward the world-anchored target even when the player stops.
        private float GetSpeed(Vector3 cameraPosition, Vector3 target)
        {
            float playerSpeed = Flatten(_context.TargetVelocity).magnitude;
            float error = Flatten(target - cameraPosition).magnitude;
            float bonus = Mathf.Min(
                error * _context.N("catch_up.position_error_speed_gain"),
                _context.N("catch_up.max_bonus"));
            float desiredSpeed = Mathf.Min(
                _context.MaxSpeed * _context.N("catch_up.maximum_speed_multiplier"),
                playerSpeed + bonus);
            if (!_speedInitialized)
            {
                _commandedSpeed = playerSpeed;
                _speedInitialized = true;
            }
            _commandedSpeed = Mathf.MoveTowards(
                _commandedSpeed, desiredSpeed,
                (desiredSpeed < _commandedSpeed
                    ? _context.N("catch_up.brake_response") : _context.N("catch_up.speed_response")) *
                Time.deltaTime);
            return _commandedSpeed;
        }

        // Captures the current top-down direction and distance once per shot.
        private void EnsureAnchor(
            Vector3 playerPosition, Vector3 cameraPosition, Vector3 fallback)
        {
            if (_context.HasCameraAnchor)
            {
                return;
            }
            Vector3 radial = Flatten(cameraPosition - playerPosition);
            if (radial.sqrMagnitude < CameraConstants.DirectionSquared)
            {
                radial = -fallback;
            }
            _context.CameraDirectionWorld = radial.normalized;
            _context.CameraDistance = Mathf.Max(
                radial.magnitude,
                _context.N("positioning.minimum_distance"));
            _context.CameraHeightAboveGround = Mathf.Max(
                0f, cameraPosition.y - GroundHeight(cameraPosition));
            _context.HasCameraAnchor = true;
        }

        // Remembers a fallback direction for a zero-distance placement.
        private void UpdateTravelDirection(Vector3 velocity)
        {
            if (velocity.sqrMagnitude >
                CameraConstants.MeaningfulTargetVelocitySquared)
            {
                _travelDirection = velocity.normalized;
                return;
            }
            Vector3 forward = Flatten(_context.Target.transform.forward);
            if (forward.sqrMagnitude > CameraConstants.DirectionSquared)
            {
                _travelDirection = forward.normalized;
            }
        }

        // Reads terrain height beneath a camera destination.
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
