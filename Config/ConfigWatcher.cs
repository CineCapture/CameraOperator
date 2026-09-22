using System;
using System.IO;

namespace DronePilot
{
    // Polls a YAML file and publishes only complete validated snapshots.
    public sealed class ConfigWatcher
    {
        private readonly string _path;
        private readonly Action<string> _reportError;
        private DateTime _lastWriteUtc;
        private long _lastLength;
        private DateTime _nextCheckUtc;
        private DateTime _retryUtc;
        private int _retryCount;

        public Configuration Current { get; private set; }

        // Creates a documented file when absent, then validates it at startup.
        public ConfigWatcher(
            string path, Action<string> reportError = null)
        {
            _path = Path.GetFullPath(path);
            _reportError = reportError;
            Configuration.CreateDefaultIfMissing(_path);
            Current = Configuration.Load(_path);
            RememberFile();
        }

        // Checks once per second and retries an incomplete save shortly after.
        public bool Update()
        {
            DateTime now = DateTime.UtcNow;
            if (now < _nextCheckUtc || now < _retryUtc)
            {
                return false;
            }
            _nextCheckUtc = now.AddSeconds(1);
            try
            {
                var info = new FileInfo(_path);
                if (!info.Exists || info.LastWriteTimeUtc == _lastWriteUtc &&
                    info.Length == _lastLength)
                {
                    return false;
                }
                Configuration next = Configuration.Load(_path);
                Current = next;
                RememberFile();
                _retryCount = 0;
                _retryUtc = DateTime.MinValue;
                return true;
            }
            catch (Exception error)
            {
                _reportError?.Invoke(
                    $"Drone configuration reload failed: {error.Message}");
                _retryCount++;
                _retryUtc = _retryCount <= 3
                    ? now.AddMilliseconds(250)
                    : DateTime.MinValue;
                _nextCheckUtc = _retryCount <= 3
                    ? _retryUtc : now.AddSeconds(1);
                return false;
            }
        }

        // Saves the timestamp and length of the last valid file.
        private void RememberFile()
        {
            var info = new FileInfo(_path);
            _lastWriteUtc = info.LastWriteTimeUtc;
            _lastLength = info.Length;
        }
    }
}
