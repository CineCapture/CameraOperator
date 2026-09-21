using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

namespace DronePilot
{
    // Buffers plain diagnostic data and writes one JSON array per interval.
    internal sealed class TelemetrySession : IDisposable
    {
        private readonly TelemetryOptions _options;
        private readonly Action<string> _report;
        private readonly string _directory;
        private readonly DateTime _startedUtc;
        private List<Dictionary<string, object>> _buffer =
            new List<Dictionary<string, object>>();
        private Task _writer;
        private List<Dictionary<string, object>> _pendingBatch;
        private float _nextSample;
        private float _nextFlush;
        private int _part;
        private bool _disposed;

        // Creates a unique diagnostic directory for this control session.
        internal TelemetrySession(TelemetryOptions options, Action<string> report)
        {
            _options = options;
            _report = report;
            options.Validate();
            _startedUtc = DateTime.UtcNow;
            if (!options.Enabled)
            {
                return;
            }
            string session = _startedUtc.ToString("yyyy-MM-dd_HH-mm-ss-fff") +
                "_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            _directory = Path.Combine(
                options.RootDirectory, "Sessions", session);
            Directory.CreateDirectory(_directory);
            _nextSample = Time.unscaledTime;
            _nextFlush = Time.unscaledTime + options.FlushIntervalSeconds;
        }

        // Adds a flight sample without retaining any Unity object references.
        internal void Sample(DronePilotController pilot,
            FlightContext context, DroneMotion motion,
            Vector3 desired, Vector3 probe, float clearance)
        {
            if (!_options.Enabled || _disposed || !CanCollect() ||
                Time.unscaledTime < _nextSample)
            {
                return;
            }
            _nextSample = Time.unscaledTime + _options.SampleIntervalSeconds;
            Vector3 camera = pilot.Camera.transform.position;
            Vector3 target = context.Target.transform.position;
            Vector3 relative = camera - target;
            Vector3 viewport = pilot.Camera.WorldToViewportPoint(context.Focus);
            Vector3 velocity = motion.Velocity;
            Vector3 acceleration = motion.Acceleration;
            var sample = NewEntry("sample");
            sample["flight_mode"] = pilot.FlightMode;
            sample["pilot_profile"] = pilot.ProfileName;
            sample["camera_world_m"] = Components(camera);
            sample["target_world_m"] = Components(target);
            sample["relative_world_m"] = Components(relative);
            sample["horizontal_distance_m"] =
                new Vector2(relative.x, relative.z).magnitude;
            sample["world_y_difference_m"] = relative.y;
            sample["pitch_deg"] = Mathf.DeltaAngle(
                0f, pilot.Camera.transform.eulerAngles.x);
            sample["target_viewport_0_to_100"] = new[]
                { viewport.x * 100f, viewport.y * 100f };
            sample["target_in_frame"] = viewport.z > 0f &&
                viewport.x >= 0f && viewport.x <= 1f &&
                viewport.y >= 0f && viewport.y <= 1f;
            sample["camera_velocity_mps"] = Components(velocity);
            sample["camera_acceleration_mps2"] = Components(acceleration);
            sample["target_velocity_mps"] = Components(context.TargetVelocity);
            sample["desired_waypoint_world_m"] = Components(desired);
            sample["terrain_clearance_m"] = clearance;
            AddOrbitFields(sample, pilot.FlightMode, camera, target,
                desired, probe, velocity, acceleration);
            _buffer.Add(sample);
            FlushIfDue();
        }

        // Adds an event so short mode and avoidance changes are not lost.
        internal void Event(string category, string detail)
        {
            if (!_options.Enabled || _disposed || !CanCollect())
            {
                return;
            }
            var entry = NewEntry("event");
            entry["category"] = category;
            entry["detail"] = detail;
            _buffer.Add(entry);
            FlushIfDue();
        }

        // Flushes final samples without discarding a failed previous batch.
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (!_options.Enabled) return;
            try
            {
                try
                {
                    _writer?.GetAwaiter().GetResult();
                    _pendingBatch = null;
                }
                catch
                {
                    WriteBatch(_pendingBatch, _part);
                    _pendingBatch = null;
                }
                WriteBatch(_buffer, ++_part);
                _buffer.Clear();
            }
            catch (Exception error)
            {
                _report?.Invoke($"Drone telemetry write failed: {error.Message}");
            }
        }

        // Creates one timestamped plain-data diagnostic entry.
        private Dictionary<string, object> NewEntry(string type)
        {
            DateTime now = DateTime.UtcNow;
            return new Dictionary<string, object>
            {
                ["type"] = type,
                ["timestamp_utc"] = now.ToString("o"),
                ["elapsed_session_seconds"] =
                    (now - _startedUtc).TotalSeconds
            };
        }

        // Copies vector components before background serialization.
        private static float[] Components(Vector3 value)
        {
            return new[] { value.x, value.y, value.z };
        }

        // Adds direction and radial diagnostics for orbit samples only.
        private void AddOrbitFields(Dictionary<string, object> entry,
            string mode, Vector3 camera, Vector3 target,
            Vector3 desired, Vector3 probe,
            Vector3 velocity, Vector3 acceleration)
        {
            if (mode != "OrbitFlight") return;
            Vector3 tangent = Flatten(probe - desired).normalized;
            Vector3 radial = Flatten(camera - target).normalized;
            float tangential = Vector3.Dot(Flatten(velocity), tangent);
            float radialSpeed = Vector3.Dot(Flatten(velocity), radial);
            float tangentialAcceleration =
                Vector3.Dot(Flatten(acceleration), tangent);
            entry["orbit_direction"] = tangential >
                _options.OrbitDirectionThreshold ? "forward" :
                tangential < -_options.OrbitDirectionThreshold
                    ? "backward" : "stationary";
            entry["orbit_tangential_speed_mps"] = tangential;
            entry["orbit_radial_speed_mps"] = radialSpeed;
            entry["orbit_tangential_acceleration_mps2"] =
                tangentialAcceleration;
            entry["orbit_target_distance_m"] =
                Flatten(desired - camera).magnitude;
            Vector3 targetOffset = Flatten(desired - camera);
            entry["orbit_motion_reason"] = OrbitReason(
                tangential, tangentialAcceleration, velocity, targetOffset);
        }

        // Explains orbital drift using the same tests as the previous logger.
        private string OrbitReason(float tangential, float acceleration,
            Vector3 velocity, Vector3 targetOffset)
        {
            if (tangential >= -_options.OrbitDirectionThreshold)
            {
                return Mathf.Abs(Vector3.Dot(Flatten(velocity),
                    targetOffset.normalized)) <
                    _options.OrbitDirectionThreshold
                        ? "radial correction" : "following orbit target";
            }
            if (Vector3.Dot(Flatten(velocity), targetOffset) < 0f)
            {
                return "target overshoot";
            }
            return acceleration > 0f
                ? "reverse inertia while braking"
                : "controller steering toward target";
        }

        // Removes the vertical component of an orbit direction.
        private static Vector3 Flatten(Vector3 value)
        {
            value.y = 0f;
            return value;
        }

        // Bounds active memory if writing lags and preserves failed batches.
        private bool CanCollect()
        {
            if (_writer != null && _writer.IsCompleted)
            {
                if (_writer.IsFaulted)
                {
                    _report?.Invoke(
                        $"Drone telemetry write failed: {_writer.Exception}");
                    return false;
                }
                _writer = null;
                _pendingBatch = null;
            }
            int limit = (int)Math.Ceiling(
                _options.FlushIntervalSeconds /
                _options.SampleIntervalSeconds) * 2;
            return _buffer.Count < limit;
        }

        // Swaps the active buffer and serializes the finished batch off-thread.
        private void FlushIfDue()
        {
            if (Time.unscaledTime < _nextFlush || _writer != null) return;
            _nextFlush = Time.unscaledTime + _options.FlushIntervalSeconds;
            if (_buffer.Count == 0) return;
            List<Dictionary<string, object>> batch = _buffer;
            _pendingBatch = batch;
            _buffer = new List<Dictionary<string, object>>();
            int part = ++_part;
            _writer = Task.Run(() => WriteBatch(batch, part));
        }

        // Writes one complete JSON array to a uniquely numbered file.
        private void WriteBatch(List<Dictionary<string, object>> batch, int part)
        {
            if (batch.Count == 0) return;
            string path = Path.Combine(_directory, $"part-{part:D4}.json");
            File.WriteAllText(path, JsonConvert.SerializeObject(batch));
        }
    }
}
