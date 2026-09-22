using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace DronePilot
{
    // Loads the bundled drone mesh and its Unity materials.
    internal sealed class DroneModel
    {
        private const string BundleResource = "DronePilot.drone";
        private readonly GameObject _instance;
        private readonly List<Material> _materials;
        private readonly Dictionary<Material, Texture> _shellTextures;
        private readonly float _sourceDiameter;
        private readonly Vector3 _centerOffset;
        private readonly Vector3 _sourceScale;
        private DroneShellColor? _shellColor;

        // Stores the imported mesh and its original bounds.
        private DroneModel(
            GameObject instance, List<Material> materials,
            Dictionary<Material, Texture> shellTextures,
            float sourceDiameter, Vector3 centerOffset,
            Vector3 sourceScale)
        {
            _instance = instance;
            _materials = materials;
            _shellTextures = shellTextures;
            _sourceDiameter = sourceDiameter;
            _centerOffset = centerOffset;
            _sourceScale = sourceScale;
        }

        // Loads the embedded prefab without keeping its Unity bundle open afterward.
        internal static DroneModel Load(Transform parent, int layer,
            Action<string> logInfo, Action<string> logError)
        {
            AssetBundle bundle = null;
            try
            {
                bundle = OpenBundle();
                GameObject prefab = bundle?.LoadAsset<GameObject>(
                    "assets/models/sagadrone.prefab");
                if (prefab == null)
                {
                    throw new InvalidDataException(
                        "The drone bundle does not contain SagaDrone.prefab.");
                }
                return Create(prefab, parent, layer, logInfo);
            }
            catch (Exception error)
            {
                logError?.Invoke($"Could not load drone model: {error}");
                return null;
            }
            finally
            {
                bundle?.Unload(false);
            }
        }

        // Opens the Unity bundle embedded in the merged plugin assembly.
        private static AssetBundle OpenBundle()
        {
            using (Stream resource = typeof(DronePilotController).Assembly
                .GetManifestResourceStream(BundleResource))
            {
                if (resource == null)
                {
                    throw new InvalidDataException(
                        $"The drone bundle resource is missing: {BundleResource}.");
                }
                using (var memory = new MemoryStream())
                {
                    resource.CopyTo(memory);
                    return AssetBundle.LoadFromMemory(memory.ToArray());
                }
            }
        }

        // Instantiates the mesh and replaces imported shaders before rendering.
        private static DroneModel Create(
            GameObject prefab, Transform parent, int layer,
            Action<string> logInfo)
        {
            GameObject instance = UnityEngine.Object.Instantiate(
                prefab, parent, false);
            instance.name = "DronePilotModel";
            var materials = new List<Material>();
            var shellTextures = new Dictionary<Material, Texture>();
            try
            {
                Configure(instance, layer, materials, shellTextures);
                logInfo?.Invoke(
                    $"Drone model loaded: {instance.GetComponentsInChildren<Renderer>().Length} " +
                    $"renderers, shader {(materials.Count > 0 ? materials[0].shader.name : "none")}, layer {layer}.");
                Bounds bounds = BoundsOf(instance);
                float diameter = Mathf.Max(bounds.size.x,
                    Mathf.Max(bounds.size.y, bounds.size.z));
                Vector3 offset = parent.InverseTransformPoint(bounds.center);
                instance.SetActive(false);
                return new DroneModel(
                    instance, materials, shellTextures, diameter, -offset,
                    instance.transform.localScale);
            }
            catch
            {
                UnityEngine.Object.Destroy(instance);
                foreach (Material material in materials)
                {
                    UnityEngine.Object.Destroy(material);
                }
                throw;
            }
        }

        // Applies a camera-only layer and enables a two-sided drone shadow.
        private static void Configure(GameObject instance, int layer,
            List<Material> owned, Dictionary<Material, Texture> shellTextures)
        {
            foreach (Transform child in instance.GetComponentsInChildren<Transform>())
            {
                child.gameObject.layer = layer;
            }
            foreach (Collider collider in instance.GetComponentsInChildren<Collider>())
            {
                UnityEngine.Object.Destroy(collider);
            }
            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>())
            {
                renderer.shadowCastingMode = ShadowCastingMode.TwoSided;
                renderer.receiveShadows = true;
                Material[] slots = renderer.sharedMaterials;
                for (int index = 0; index < slots.Length; index++)
                {
                    Material source = slots[index];
                    Material material = MakeMaterial(source, source?.name, owned);
                    if (source != null && source.name.StartsWith(
                        "base", StringComparison.OrdinalIgnoreCase))
                    {
                        shellTextures.Add(material, source.mainTexture);
                    }
                    slots[index] = material;
                }
                renderer.sharedMaterials = slots;
            }
        }

        // Uses the bundled drone shader and recolors one model part.
        private static Material MakeMaterial(Material source, string name,
            List<Material> owned)
        {
            Color color = ColorFor(name);
            Material template = source?.shader != null &&
                source.shader.name == "DronePilot/Drone" &&
                source.shader.isSupported ? source : null;
            if (template == null)
            {
                throw new InvalidOperationException(
                    "The bundled drone shader is unavailable.");
            }
            Material material = new Material(template);
            material.color = color;
            if (material.HasProperty("_Metallic"))
            {
                material.SetFloat("_Metallic", 0.55f);
            }
            if (material.HasProperty("_Glossiness"))
            {
                material.SetFloat("_Glossiness", 0.45f);
            }
            if (material.HasProperty("_EmissionColor") &&
                name != null && name.StartsWith("Light", StringComparison.OrdinalIgnoreCase))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color);
            }
            owned.Add(material);
            return material;
        }

        // Maps the source model's color slots to metal and warm light.
        private static Color ColorFor(string name)
        {
            if (name != null && name.StartsWith("Light", StringComparison.OrdinalIgnoreCase))
            {
                return new Color(1f, 0.58f, 0.2f);
            }
            if (name != null && name.StartsWith("edge", StringComparison.OrdinalIgnoreCase))
            {
                return new Color(0.08f, 0.09f, 0.09f);
            }
            return new Color(0.46f, 0.48f, 0.5f);
        }

        // Combines renderer bounds to measure the imported model once.
        private static Bounds BoundsOf(GameObject instance)
        {
            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                throw new InvalidDataException("The drone prefab has no mesh.");
            }
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers)
            {
                bounds.Encapsulate(renderer.bounds);
            }
            return bounds;
        }

        // Scales and centers the imported model inside the collision sphere.
        internal void SetRadius(float radius)
        {
            float scale = radius * 2f / _sourceDiameter;
            _instance.transform.localScale = _sourceScale * scale;
            _instance.transform.localPosition = _centerOffset * scale;
        }

        // Shows the model only in the caller's selected camera mode.
        internal void SetVisible(bool visible)
        {
            if (_instance.activeSelf != visible)
            {
                _instance.SetActive(visible);
            }
        }

        // Switches shell color without changing the lens or allocating materials.
        internal void SetShellColor(DroneShellColor color)
        {
            if (_shellColor == color)
            {
                return;
            }
            _shellColor = color;
            foreach (var shell in _shellTextures)
            {
                shell.Key.color = color == DroneShellColor.Yellow
                    ? new Color(0.38f, 0.21f, 0.08f)
                    : new Color(0.46f, 0.48f, 0.5f);
                if (shell.Key.HasProperty("_MainTex"))
                {
                    shell.Key.mainTexture = color == DroneShellColor.Yellow
                        ? Texture2D.whiteTexture : shell.Value;
                }
            }
        }

        // Pulses only the model's originally luminous material slots.
        internal void SetGlow(float pulse)
        {
            foreach (Material material in _materials)
            {
                if (material.IsKeywordEnabled("_EMISSION"))
                {
                    material.SetColor("_EmissionColor",
                        material.color * pulse);
                }
            }
        }

        // Destroys the caller-owned instance and temporary material copies.
        internal void Dispose()
        {
            UnityEngine.Object.Destroy(_instance);
            foreach (Material material in _materials)
            {
                UnityEngine.Object.Destroy(material);
            }
        }
    }
}
