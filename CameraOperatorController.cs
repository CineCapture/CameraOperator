using System;
using UnityEngine;

namespace CameraOperator
{
    // Flies a supplied Unity camera around a supplied target without owning either.
    public sealed class CameraOperatorController : IDisposable
    {
        private readonly Camera _camera;
        private readonly CameraContext _context;
        private readonly ConfigWatcher _configuration;
        private readonly CameraProfile[] _profiles;
        private readonly CameraMovementController _movement;
        private readonly CameraPathPlanner _trajectory;
        private readonly CameraMotion _motion;
        private readonly CameraAiming _look;
        private readonly CameraFraming _framing;
        private bool _initialized;
        private bool _disposed;

        public float MaximumHeight => _context.Profile.MaximumHeight;
        public float TargetHeight => _context.N("aiming.target_height");
        public Configuration Settings => _context.Config;

        // Validates inputs and loads the camera's documented YAML configuration.
        public CameraOperatorController(
            Camera camera, GameObject target, string configurationPath,
            CameraWorld world, CameraProfile[] profiles)
        {
            _camera = camera ?? throw new ArgumentNullException(nameof(camera));
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (profiles == null || profiles.Length == 0)
                throw new ArgumentException("At least one camera profile is required.");
            _profiles = profiles;
            _configuration = new ConfigWatcher(
                configurationPath, world.LogWarning);
            foreach (CameraProfile profile in profiles)
            {
                if (profile == null)
                {
                    throw new ArgumentException("Invalid camera profile.");
                }
            }
            _context = new CameraContext
            {
                Target = target, World = world,
                Config = _configuration.Current
            };
            _context.Profile = SelectProfile();
            _movement = new CameraMovementController(_context);
            _trajectory = new CameraPathPlanner(_context);
            _motion = new CameraMotion(_context);
            _look = new CameraAiming(_context);
            _framing = new CameraFraming(_context);
        }

        // Advances movement once after the target has moved for this frame.
        public void Update(Vector3 targetVelocity, float maxSpeed)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(CameraOperatorController));
            if (_context.Target == null) return;
            if (maxSpeed <= 0f) throw new ArgumentOutOfRangeException(nameof(maxSpeed));
            _context.TargetVelocity = targetVelocity;
            _context.MaxSpeed = maxSpeed;
            if (_configuration.Update())
            {
                _context.Config = _configuration.Current;
                _context.World.LogInfo?.Invoke("Camera Operator configuration reloaded.");
            }
            CameraProfile selected = SelectProfile();
            if (selected != _context.Profile)
            {
                _context.Profile = selected;
                _context.World.LogInfo?.Invoke($"Camera profile: {selected.Name}.");
            }
            if (!_initialized)
            {
                _motion.Initialize(targetVelocity);
                _look.Initialize();
                _initialized = true;
            }
            AdvanceMovement();
        }

        // Cuts to a position and selects its initial mode from target movement.
        public void Reposition(Vector3 position, Vector3 targetVelocity)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(CameraOperatorController));
            _context.TargetVelocity = targetVelocity;
            Vector3 radial = position - _context.Target.transform.position;
            radial.y = 0f;
            _context.HasCameraAnchor = radial.sqrMagnitude > 0.001f;
            if (_context.HasCameraAnchor)
            {
                _context.CameraDirectionWorld = radial.normalized;
                _context.CameraDistance = radial.magnitude;
                _context.CameraHeightAboveGround = Mathf.Max(
                    0f, position.y - _context.Ground(position));
            }
            _camera.transform.position = position;
            _camera.transform.LookAt(_context.Focus, Vector3.up);
            _motion.Initialize(Vector3.zero);
            _movement.Reset();
            _trajectory.Reset();
            _look.Initialize();
        }

        // Marks the controller disposed without destroying caller-owned objects.
        public void Dispose()
        {
            _disposed = true;
        }

        // Selects the first host profile whose condition matches the camera.
        private CameraProfile SelectProfile()
        {
            Vector3 position = _camera.transform.position;
            foreach (CameraProfile profile in _profiles)
            {
                if (profile.IsActive?.Invoke(position) == true)
                {
                    return profile;
                }
            }
            return _profiles[_profiles.Length - 1];
        }

        // Applies the former SagaCapture movement pipeline to the supplied camera.
        private void AdvanceMovement()
        {
            Vector3 position = _camera.transform.position;
            Vector3 desired = _movement.Update(
                position, _motion.Speed, out float targetSpeed);
            Vector3 probeTarget = desired;
            if (!_framing.IsVisible(_camera, _context.Focus))
            {
                desired = _framing.GetRecoveryTarget(position);
                probeTarget = desired;
                targetSpeed = Mathf.Max(targetSpeed, _context.MaxSpeed);
            }
            float clearance = Mathf.Max(
                _movement.GetTerrainClearance(_motion.Speed),
                _context.N("obstacle_avoidance.camera_radius"));
            desired = _trajectory.Plan(position, desired, probeTarget,
                _motion.Velocity, clearance);
            float targetHeight = _context.Focus.y;
            desired.y = Mathf.Max(desired.y, targetHeight);
            Vector3 next = _motion.Step(position, desired, targetSpeed,
                _trajectory.EmergencyAvoidance);
            next.y = Mathf.Max(next.y, targetHeight);
            next.y = Mathf.Max(next.y,
                _context.Ground(next) + clearance);
            _camera.transform.position = next;
            _look.Update(_camera.transform, _context.Focus);
        }
    }
}
