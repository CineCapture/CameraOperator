using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace CameraOperator
{
    // Polls a YAML file and publishes only complete validated snapshots.
    internal sealed class ConfigWatcher
    {
        private readonly string _path;
        private readonly Action<string> _reportError;
        private byte[] _contentHash;
        private DateTime _nextCheckUtc;
        private DateTime _retryUtc;
        private int _retryCount;

        internal Configuration Current { get; private set; }

        // Creates a documented file when absent, then validates it at startup.
        internal ConfigWatcher(
            string path, Action<string> reportError = null)
        {
            _path = Path.GetFullPath(path);
            _reportError = reportError;
            Configuration.CreateDefaultIfMissing(_path);
            string yaml = File.ReadAllText(_path);
            Current = Configuration.Parse(yaml);
            _contentHash = Hash(yaml);
        }

        // Checks once per second and retries an incomplete save shortly after.
        internal bool Update()
        {
            DateTime now = DateTime.UtcNow;
            if (now < _nextCheckUtc || now < _retryUtc)
            {
                return false;
            }
            _nextCheckUtc = now.AddSeconds(1);
            try
            {
                if (!File.Exists(_path))
                {
                    return false;
                }
                string yaml = File.ReadAllText(_path);
                byte[] contentHash = Hash(yaml);
                if (HashesMatch(_contentHash, contentHash))
                {
                    return false;
                }
                Configuration next = Configuration.Parse(yaml);
                Current = next;
                _contentHash = contentHash;
                _retryCount = 0;
                _retryUtc = DateTime.MinValue;
                return true;
            }
            catch (Exception error)
            {
                _reportError?.Invoke(
                    $"Camera Operator configuration reload failed: {error.Message}");
                _retryCount++;
                _retryUtc = _retryCount <= 3
                    ? now.AddMilliseconds(250)
                    : DateTime.MinValue;
                _nextCheckUtc = _retryCount <= 3
                    ? _retryUtc : now.AddSeconds(1);
                return false;
            }
        }

        // Computes a stable fingerprint for one complete YAML snapshot.
        private static byte[] Hash(string yaml)
        {
            using (SHA256 algorithm = SHA256.Create())
            {
                return algorithm.ComputeHash(Encoding.UTF8.GetBytes(yaml));
            }
        }

        // Compares two content fingerprints without relying on file metadata.
        private static bool HashesMatch(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }
            for (int index = 0; index < left.Length; index++)
            {
                if (left[index] != right[index])
                {
                    return false;
                }
            }
            return true;
        }
    }
}
