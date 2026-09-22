using System;
using DronePilot.Telemetry;
using UnityEngine;

namespace DronePilot
{
    // Flies a supplied Unity camera around a supplied target without owning either.
    public sealed class DronePilotController : IDisposable
    {
        private readonly Camera _camera;
        private readonly GameObject _visual;
        private readonly DroneVisual _droneVisual;
        private readonly FlightContext _context;
        private readonly ConfigWatcher _configuration;
        private readonly PilotProfile[] _profiles;
        private readonly DroneFlightController _flight;
        private readonly DroneTrajectoryPlanner _trajectory;
        private readonly DroneMotion _motion;
        private readonly DroneLook _look;
        private readonly DroneFraming _framing;
        private readonly Session _telemetry;
        private bool _initialized;
        private bool _disposed;

        public string FlightMode => _flight.Mode.ToString();
        public string ProfileName => _context.Profile?.Name;
        public Camera Camera => _camera;
        public float CameraRadius => _context.N(
            "flight_modes.trailing_flight.avoidance.detection.camera_radius");

        // Validates inputs and loads the drone's documented YAML configuration.
        public DronePilotController(
            Camera camera, GameObject target, string configurationPath,
            DroneWorld world, PilotProfile[] profiles,
            Vector3? aimOffset = null,
            Options telemetry = null,
            GameObject visual = null,
            DroneVisualOptions droneVisual = null)
        {
            _camera = camera ?? throw new ArgumentNullException(nameof(camera));
            _visual = visual;
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (profiles == null || profiles.Length == 0)
                throw new ArgumentException("At least one pilot profile is required.");
            _profiles = profiles;
            _configuration = new ConfigWatcher(
                configurationPath, world.LogWarning);
            foreach (PilotProfile profile in profiles)
            {
                if (profile == null || profile.MaximumOrbitRadius <
                    _configuration.Current.Number(
                        "flight_modes.orbit_flight.height_constraints.minimum_radius"))
                {
                    throw new ArgumentException(
                        "Pilot profile orbit radius is below the YAML minimum.");
                }
            }
            _context = new FlightContext
            {
                Target = target, World = world,
                Config = _configuration.Current,
                AimOffset = aimOffset ?? DefaultAimOffset(target)
            };
            _context.Profile = SelectProfile();
            _telemetry = new Session(
                telemetry ?? new Options(), world.LogWarning);
            _context.Event = _telemetry.Event;
            _flight = new DroneFlightController(_context);
            _trajectory = new DroneTrajectoryPlanner(_context);
            _motion = new DroneMotion(_context);
            _look = new DroneLook(_context);
            _framing = new DroneFraming(_context);
            _droneVisual = CreateVisual(droneVisual);
            SynchronizeVisual();
        }

        // Creates and initializes the optional built-in drone model.
        private DroneVisual CreateVisual(DroneVisualOptions options)
        {
            if (options == null)
            {
                return null;
            }
            if (options.ViewerCamera == null)
            {
                throw new ArgumentException("The drone visual needs a viewer camera.");
            }
            GameObject visualObject = new GameObject("DronePilotVisual");
            DroneVisual droneVisual = visualObject.AddComponent<DroneVisual>();
            droneVisual.Initialize(_camera, options);
            droneVisual.SetRadius(CameraRadius);
            return droneVisual;
        }

        // Advances flight once after the target has moved for this frame.
        public void Update(Vector3 targetVelocity, float maxSpeed)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(DronePilotController));
            if (_context.Target == null) return;
            if (maxSpeed <= 0f) throw new ArgumentOutOfRangeException(nameof(maxSpeed));
            _context.TargetVelocity = targetVelocity;
            _context.MaxSpeed = maxSpeed;
            if (_configuration.Update())
            {
                _context.Config = _configuration.Current;
                _context.World.LogInfo?.Invoke("Drone configuration reloaded.");
                _telemetry.Event("configuration_reloaded", "config.yaml");
            }
            PilotProfile selected = SelectProfile();
            if (selected != _context.Profile)
            {
                _context.Profile = selected;
                _context.World.LogInfo?.Invoke($"Drone profile: {selected.Name}.");
                _telemetry.Event("profile_changed", selected.Name);
            }
            if (!_initialized)
            {
                _motion.Initialize(targetVelocity);
                _look.Initialize();
                _initialized = true;
            }
            AdvanceFlight();
        }

        // Changes the built-in visual without affecting drone flight.
        public void SetVisual(bool visible, DroneShellColor color)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(DronePilotController));
            _droneVisual?.SetAppearance(visible, color);
        }

        // Releases telemetry and the built-in visual without destroying the camera.
        public void Dispose()
        {
            _droneVisual?.Dispose();
            if (_droneVisual != null)
            {
                UnityEngine.Object.Destroy(_droneVisual.gameObject);
            }
            _telemetry.Dispose();
            _disposed = true;
        }

        // Selects the first host profile whose condition matches the drone.
        private PilotProfile SelectProfile()
        {
            Vector3 position = _camera.transform.position;
            foreach (PilotProfile profile in _profiles)
            {
                if (profile.IsActive?.Invoke(position) == true)
                {
                    return profile;
                }
            }
            return _profiles[_profiles.Length - 1];
        }

        // Uses the target's visual bounds center when no aim offset is supplied.
        private static Vector3 DefaultAimOffset(GameObject target)
        {
            Renderer[] renderers = target.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                Bounds bounds = renderers[0].bounds;
                for (int index = 1; index < renderers.Length; index++)
                {
                    bounds.Encapsulate(renderers[index].bounds);
                }
                return bounds.center - target.transform.position;
            }
            Collider[] colliders = target.GetComponentsInChildren<Collider>();
            if (colliders.Length == 0) return Vector3.zero;
            Bounds colliderBounds = colliders[0].bounds;
            for (int index = 1; index < colliders.Length; index++)
            {
                colliderBounds.Encapsulate(colliders[index].bounds);
            }
            return colliderBounds.center - target.transform.position;
        }

        // Applies the former SagaCapture flight pipeline to the supplied camera.
        private void AdvanceFlight()
        {
            Vector3 position = _camera.transform.position;
            Vector3 desired = _flight.Update(
                position, _motion.Speed, out float targetSpeed);
            Vector3 probeTarget = _flight.TrajectoryProbeTarget;
            bool recovering = false;
            if (_flight.Mode == DroneFlightMode.TrailingFlight)
            {
                desired = _flight.PlanTrailingRoute(position, desired,
                    _motion.Velocity, _context.Target.transform.position,
                    _context.TargetVelocity);
                probeTarget = desired;
            }
            if (!_framing.IsVisible(_camera, _context.Focus))
            {
                recovering = true;
                desired = _framing.GetRecoveryTarget(position);
                probeTarget = desired;
                targetSpeed = Mathf.Max(targetSpeed, _context.MaxSpeed);
            }
            float clearance = Mathf.Max(
                _flight.GetTerrainClearance(_motion.Speed),
                _context.N("flight_modes.trailing_flight.avoidance.detection.camera_radius"));
            bool reactive = _flight.Mode == DroneFlightMode.TrailingFlight
                ? _context.B("flight_modes.trailing_flight.avoidance.detection.enabled")
                : _context.B("flight_modes.orbit_flight.reactive_obstacle_avoidance.enabled");
            desired = _trajectory.Plan(position, desired, probeTarget,
                _motion.Velocity, clearance, reactive);
            float targetHeight = _context.Focus.y;
            desired.y = Mathf.Max(desired.y, targetHeight);
            Vector3 next = _flight.Mode == DroneFlightMode.OrbitFlight &&
                !recovering
                ? _motion.StepOrbit(position, desired,
                    probeTarget - desired, targetSpeed)
                : _motion.Step(position, desired, targetSpeed,
                    _trajectory.EmergencyAvoidance);
            next.y = Mathf.Max(next.y, targetHeight);
            next.y = Mathf.Max(next.y,
                _context.Ground(next) + clearance);
            _camera.transform.position = next;
            _look.Update(_camera.transform, _context.Focus);
            SynchronizeVisual();
            _telemetry.Sample(this, _context, _motion,
                desired, probeTarget, clearance);
        }

        // Moves the caller-owned visual with the supplied camera.
        private void SynchronizeVisual()
        {
            if (_visual != null)
            {
                _visual.transform.SetPositionAndRotation(
                    _camera.transform.position, _camera.transform.rotation);
            }
            _droneVisual?.Synchronize();
            _droneVisual?.SetRadius(CameraRadius);
        }
    }
}
