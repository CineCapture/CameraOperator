using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

namespace CameraOperator
{
    // Holds a validated, immutable snapshot of the camera's YAML settings.
    public sealed class Configuration
    {
        private readonly Dictionary<string, int> _sequences;
        private readonly Dictionary<string, float> _numbers =
            new Dictionary<string, float>(StringComparer.Ordinal);

        private Configuration(
            Dictionary<string, string> values,
            Dictionary<string, int> sequences)
        {
            _sequences = sequences;
            foreach (var pair in values)
            {
                if (float.TryParse(pair.Value, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out float number) &&
                    !float.IsNaN(number) && !float.IsInfinity(number))
                {
                    _numbers.Add(pair.Key, number);
                }
            }
        }

        // Loads the documented default template embedded in this assembly.
        private static string DefaultYaml()
        {
            Assembly assembly = typeof(Configuration).Assembly;
            using (Stream stream = assembly.GetManifestResourceStream(
                "CameraOperator.config.yaml"))
            using (var reader = new StreamReader(stream))
            {
                return reader.ReadToEnd();
            }
        }

        // Creates the documented default file only when it does not exist.
        internal static void CreateDefaultIfMissing(string path)
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(path));
            Directory.CreateDirectory(directory);
            if (!File.Exists(path))
            {
                using (var stream = new FileStream(
                    path, FileMode.CreateNew, FileAccess.Write))
                using (var writer = new StreamWriter(stream))
                {
                    writer.Write(DefaultYaml());
                }
            }
        }

        // Parses one complete YAML document and rejects missing or invalid keys.
        internal static Configuration Parse(string yaml)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            var sequences = new Dictionary<string, int>(StringComparer.Ordinal);
            ReadNode(ParseRoot(yaml), "", values, sequences);
            var defaults = new Dictionary<string, string>(StringComparer.Ordinal);
            var defaultSequences = new Dictionary<string, int>(StringComparer.Ordinal);
            ReadNode(ParseRoot(DefaultYaml()), "", defaults, defaultSequences);
            ValidateShape(values, sequences, defaults, defaultSequences);
            var configuration = new Configuration(values, sequences);
            configuration.ValidateDocumentedRanges();
            configuration.ValidateRelationships();
            return configuration;
        }

        // Reads a finite scalar setting by its dotted YAML path.
        public float Number(string path)
        {
            if (!_numbers.TryGetValue(path, out float value))
            {
                throw new FormatException($"Invalid numeric setting: {path}.");
            }
            return value;
        }

        // Returns the number of entries in one validated YAML sequence.
        public int Count(string path)
        {
            if (!_sequences.TryGetValue(path, out int count))
            {
                throw new FormatException($"Missing sequence: {path}.");
            }
            return count;
        }

        // Parses a YAML mapping as the only supported document root.
        private static YamlMappingNode ParseRoot(string yaml)
        {
            var stream = new YamlStream();
            using (var reader = new StringReader(yaml))
            {
                stream.Load(reader);
            }
            if (stream.Documents.Count != 1 ||
                !(stream.Documents[0].RootNode is YamlMappingNode root))
            {
                throw new FormatException("Camera Operator YAML must have one mapping root.");
            }
            return root;
        }

        // Flattens mapping and sequence leaves into stable setting paths.
        private static void ReadNode(
            YamlNode node, string path, Dictionary<string, string> values,
            Dictionary<string, int> sequences)
        {
            if (node is YamlMappingNode map)
            {
                foreach (var pair in map.Children)
                {
                    string key = ((YamlScalarNode)pair.Key).Value;
                    ReadNode(pair.Value, path.Length == 0 ? key :
                        path + "." + key, values, sequences);
                }
            }
            else if (node is YamlSequenceNode sequence)
            {
                sequences.Add(path, sequence.Children.Count);
                for (int index = 0; index < sequence.Children.Count; index++)
                {
                    ReadNode(sequence.Children[index],
                        $"{path}[{index}]", values, sequences);
                }
            }
            else if (node is YamlScalarNode scalar)
            {
                values.Add(path, scalar.Value);
            }
            else
            {
                throw new FormatException($"Unsupported YAML node at {path}.");
            }
        }

        // Ensures every supplied key matches the embedded template's type.
        private static void ValidateShape(
            Dictionary<string, string> values, Dictionary<string, int> sequences,
            Dictionary<string, string> defaults,
            Dictionary<string, int> defaultSequences)
        {
            foreach (var pair in defaultSequences)
            {
                if (!sequences.TryGetValue(pair.Key, out int count) ||
                    count != pair.Value)
                {
                    throw new FormatException($"Invalid sequence: {pair.Key}.");
                }
            }
            var candidate = new Configuration(values, sequences);
            foreach (var pair in defaults)
            {
                if (!values.ContainsKey(pair.Key))
                {
                    throw new FormatException($"Missing setting: {pair.Key}.");
                }
                candidate.Number(pair.Key);
            }
            if (values.Count != defaults.Count ||
                sequences.Count != defaultSequences.Count)
            {
                throw new FormatException("Camera Operator YAML contains unknown settings.");
            }
        }

        // Checks relational constraints that basic scalar parsing cannot catch.
        private void ValidateRelationships()
        {
            ValidatePositioning();
            Less("motion.limits.maximum_acceleration",
                "motion.limits.emergency_acceleration");
            Less("motion.limits.maximum_jerk",
                "motion.limits.emergency_jerk");
            Less("motion.vertical_movement.maximum_speed_error_start",
                "motion.vertical_movement.maximum_speed_error_end");
            foreach (var pair in _numbers)
            {
                if (pair.Value < 0f && !pair.Key.Contains("offset"))
                {
                    throw new FormatException($"Negative setting: {pair.Key}.");
                }
            }
        }

        // Validates the shared camera positioning distance and height bounds.
        private void ValidatePositioning()
        {
            const string root = "positioning.";
            Less(root + "minimum_distance", root + "maximum_distance");
        }

        // Enforces the min/max bounds documented beside template settings.
        private void ValidateDocumentedRanges()
        {
            var names = new List<string>();
            var indents = new List<int>();
            float? minimum = null;
            float? maximum = null;
            foreach (string line in DefaultYaml().Split('\n'))
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("#"))
                {
                    Match bounds = Regex.Match(trimmed,
                        @"Min:\s*(-?[0-9.]+)\s*\|\s*Max:\s*(-?[0-9.]+)");
                    if (bounds.Success)
                    {
                        minimum = float.Parse(bounds.Groups[1].Value,
                            CultureInfo.InvariantCulture);
                        maximum = float.Parse(bounds.Groups[2].Value,
                            CultureInfo.InvariantCulture);
                    }
                    continue;
                }
                Match setting = Regex.Match(line,
                    @"^(\s*)([a-z_]+):(?:\s*(.*))?$");
                if (!setting.Success) continue;
                int indentation = setting.Groups[1].Length;
                while (indents.Count > 0 &&
                    indentation <= indents[indents.Count - 1])
                {
                    indents.RemoveAt(indents.Count - 1);
                    names.RemoveAt(names.Count - 1);
                }
                string key = setting.Groups[2].Value;
                string path = string.Join(".", names.Concat(new[] { key }));
                if (setting.Groups[3].Value.Length == 0)
                {
                    names.Add(key);
                    indents.Add(indentation);
                    minimum = maximum = null;
                    continue;
                }
                if (minimum.HasValue && maximum.HasValue)
                {
                    float value = Number(path);
                    if (value < minimum.Value || value > maximum.Value)
                    {
                        throw new FormatException(
                            $"{path} must be from {minimum} to {maximum}.");
                    }
                }
                minimum = maximum = null;
            }
        }

        // Checks one strict lower-before-upper constraint.
        private void Less(string lower, string upper)
        {
            if (Number(lower) >= Number(upper))
            {
                throw new FormatException($"{lower} must be below {upper}.");
            }
        }

    }
}
