using UnityEngine;

namespace DronePilot
{
    // Displays the pilot-owned model to one viewer but not its flight camera.
    [DefaultExecutionOrder(10000)]
    internal sealed class DroneVisual : MonoBehaviour
    {
        private Camera _viewerCamera;
        private Camera _droneCamera;
        private DroneModel _model;
        private int _viewerMask;
        private int _droneMask;
        private int _layerMask;
        private bool _visible;
        private float _radius;
        private DroneShellColor _color;

        // Loads the built-in model on a camera-excluded Unity layer.
        internal void Initialize(Camera droneCamera, DroneVisualOptions options)
        {
            _viewerCamera = options.ViewerCamera;
            _droneCamera = droneCamera;
            int layer = FindUnusedLayer();
            if (layer < 0)
            {
                options.LogWarning?.Invoke(
                    "No unused Unity layer is available for the drone model.");
                return;
            }
            _model = DroneModel.Load(transform, layer,
                options.LogInfo, options.LogError);
            if (_model == null)
            {
                return;
            }
            _viewerMask = _viewerCamera.cullingMask;
            _droneMask = _droneCamera.cullingMask;
            _layerMask = 1 << layer;
            _viewerCamera.cullingMask |= _layerMask;
            _droneCamera.cullingMask &= ~_layerMask;
            SetAppearance(options.Visible, options.Color);
        }

        // Keeps the selected layer visible when the host resets camera masks.
        private void LateUpdate()
        {
            if (_model != null)
            {
                _viewerCamera.cullingMask |= _layerMask;
                Refresh();
            }
        }

        // Moves the model with the pilot camera without a separate flight path.
        internal void Synchronize()
        {
            transform.SetPositionAndRotation(
                _droneCamera.transform.position, _droneCamera.transform.rotation);
        }

        // Scales the model to the configured drone collision radius.
        internal void SetRadius(float radius)
        {
            if (_model == null || Mathf.Abs(_radius - radius) < 0.0001f)
            {
                return;
            }
            _radius = radius;
            _model.SetRadius(radius);
        }

        // Applies the caller's visibility and shell color choices.
        internal void SetAppearance(bool visible, DroneShellColor color)
        {
            _visible = visible;
            _color = color;
            Refresh();
        }

        // Updates the model's appearance without allocating materials.
        private void Refresh()
        {
            if (_model == null)
            {
                return;
            }
            _model.SetShellColor(_color);
            _model.SetVisible(_visible);
            if (_visible)
            {
                float pulse = 1.15f + 0.2f * Mathf.Sin(Time.time * 1.8f);
                _model.SetGlow(pulse);
            }
        }

        // Restores camera masks and releases the built-in drone model.
        internal void Dispose()
        {
            if (_model == null)
            {
                return;
            }
            _viewerCamera.cullingMask = _viewerMask;
            _droneCamera.cullingMask = _droneMask;
            _model.Dispose();
            _model = null;
        }

        // Finds an unnamed layer for excluding the visual from its camera.
        private static int FindUnusedLayer()
        {
            for (int layer = 31; layer >= 8; layer--)
            {
                if (string.IsNullOrEmpty(LayerMask.LayerToName(layer)))
                {
                    return layer;
                }
            }
            return -1;
        }
    }
}
