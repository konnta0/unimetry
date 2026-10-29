using System;
using System.Collections.Generic;

namespace Unimetry.Internal
{
    [Serializable]
    internal sealed class ResourceAttribute
    {
        public string Key;
        public string Value;
    }

    [Serializable]
    internal sealed class ResourceSnapshot
    {
        public ResourceAttribute[] Attributes = Array.Empty<ResourceAttribute>();

        public static ResourceSnapshot FromOptions(UnimetryOptions options)
        {
            var values = CreateBase(options);

            if (options.ResourceAttributes != null)
            {
                foreach (var pair in options.ResourceAttributes)
                {
                    if (!string.IsNullOrWhiteSpace(pair.Key))
                    {
                        values[pair.Key] = pair.Value ?? string.Empty;
                    }
                }
            }

            return FromDictionary(values);
        }

        public static ResourceSnapshot ForLegacyRecord(UnimetryOptions options)
        {
            var values = CreateBase(options);
            values["unimetry.resource_snapshot.missing"] = "true";
            return FromDictionary(values);
        }

        public static ResourceSnapshot FromDictionary(IReadOnlyDictionary<string, string> values)
        {
            var keys = new List<string>(values.Keys);
            keys.Sort(StringComparer.Ordinal);
            var snapshot = new ResourceSnapshot
            {
                Attributes = new ResourceAttribute[keys.Count],
            };
            for (var index = 0; index < keys.Count; index++)
            {
                var key = keys[index];
                snapshot.Attributes[index] = new ResourceAttribute
                {
                    Key = key,
                    Value = values[key] ?? string.Empty,
                };
            }

            return snapshot;
        }

        public static bool Equivalent(ResourceSnapshot left, ResourceSnapshot right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null || left.Attributes == null || right.Attributes == null ||
                left.Attributes.Length != right.Attributes.Length)
            {
                return false;
            }

            for (var index = 0; index < left.Attributes.Length; index++)
            {
                var a = left.Attributes[index];
                var b = right.Attributes[index];
                if (a == null || b == null ||
                    !string.Equals(a.Key, b.Key, StringComparison.Ordinal) ||
                    !string.Equals(a.Value, b.Value, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static Dictionary<string, string> CreateBase(UnimetryOptions options)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["service.name"] = options.ServiceName ?? string.Empty,
                ["service.version"] = options.ServiceVersion ?? string.Empty,
                ["deployment.environment"] = options.DeploymentEnvironment ?? string.Empty,
                ["telemetry.sdk.name"] = "unimetry",
                ["telemetry.sdk.language"] = "csharp",
                ["telemetry.sdk.version"] = "0.4.2",
            };
        }
    }

    internal sealed class ResourceState
    {
        private readonly object gate = new object();
        private readonly Dictionary<string, string> attributes = new Dictionary<string, string>(StringComparer.Ordinal);

        public ResourceState(UnimetryOptions options)
        {
            var initial = ResourceSnapshot.FromOptions(options);
            for (var index = 0; index < initial.Attributes.Length; index++)
            {
                attributes[initial.Attributes[index].Key] = initial.Attributes[index].Value;
            }
        }

        public ResourceSnapshot Capture()
        {
            lock (gate)
            {
                return ResourceSnapshot.FromDictionary(attributes);
            }
        }

        public void Set(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Resource attribute key must not be empty.", nameof(key));
            }

            lock (gate)
            {
                if (value == null)
                {
                    attributes.Remove(key);
                }
                else
                {
                    attributes[key] = value;
                }
            }
        }
    }
}
