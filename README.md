# DronePilot

DronePilot flies a supplied Unity `Camera` around a target `GameObject` and
keeps the target in view. The host owns both objects and provides terrain,
obstacle, and actor information. DronePilot does not create the camera or
record video.

## Basic use

Call `Update` on Unity's main thread after the target moves. Pass its current
velocity and a positive maximum speed in meters per second. The last pilot
profile is the fallback when no earlier condition matches.

```csharp
using System.IO;
using DronePilot;
using UnityEngine;

public sealed class DroneExample : MonoBehaviour
{
    public Camera droneCamera;
    public Camera gameplayCamera;
    public GameObject target;
    public LayerMask groundMask;
    private DronePilotController _pilot;
    private Vector3 _previousPosition;

    private void Start()
    {
        var world = new DroneWorld
        {
            GroundHeight = position => Physics.Raycast(
                position + Vector3.up * 100f, Vector3.down,
                out RaycastHit hit, 200f, groundMask)
                    ? hit.point.y : (float?)null,
            IgnoreObstacle = collider => collider.isTrigger,
            IsActor = collider =>
                collider.GetComponentInParent<CharacterController>() != null
        };
        var profiles = new[]
        {
            new PilotProfile("Open", 8f, 8f, position => true)
        };
        string configPath = Path.Combine(Application.persistentDataPath,
            "DronePilot", "config.yaml");
        _pilot = new DronePilotController(
            droneCamera, target, configPath, world, profiles,
            aimOffset: Vector3.up * 1.6f);
        _previousPosition = target.transform.position;
    }

    private void LateUpdate()
    {
        Vector3 position = target.transform.position;
        Vector3 velocity = (position - _previousPosition) /
            Mathf.Max(Time.deltaTime, 0.0001f);
        _previousPosition = position;
        _pilot.Update(velocity, 7f); // Replace 7 with the target's maximum speed.
    }

    private void OnDestroy()
    {
        _pilot?.Dispose();
    }
}
```

Use a ground-only layer for `groundMask`; the sample raycast is only an
example terrain provider. The host decides which colliders are ignored or
classified as actors. You can add earlier profiles with host-defined
conditions, for example a dense-area profile before the unconditional `Open`
profile.

## Optional visual and diagnostics

The built-in drone model is embedded in `DronePilot.dll`. It can be shown to a
viewer camera while remaining hidden from the piloted camera. To use it and
telemetry, add `using DronePilot.Telemetry;` and replace the controller
construction in `Start` with this fragment. `gameplayCamera` must differ from
`droneCamera`:

```csharp
var visual = new DroneVisualOptions
{
    ViewerCamera = gameplayCamera,
    Visible = true,
    Color = DroneShellColor.Metal
};
var telemetry = new Options
{
    Enabled = true,
    RootDirectory = Path.Combine(Application.persistentDataPath, "DronePilot"),
    SampleIntervalSeconds = 0.5f,
    FlushIntervalSeconds = 60f,
    OrbitDirectionThreshold = 0.05f
};
_pilot = new DronePilotController(
    droneCamera, target, configPath, world, profiles,
    telemetry: telemetry, droneVisual: visual);
_pilot.SetVisual(true, DroneShellColor.Yellow);
```

Telemetry writes JSON arrays under `<root>/Sessions/<session>/`. If you
already have a visual `GameObject`, pass it through the separate `visual`
parameter instead; DronePilot moves it but does not own it.

## Configuration

`config.yaml` is embedded in the DLL. A missing configuration file is created
at the path supplied to the constructor with documented defaults. Existing
files are validated and checked once per second. An invalid reload keeps the
last valid settings.

## Build

Use .NET SDK 10 or a compatible MSBuild and provide the directory containing
Unity's managed assemblies:

```text
dotnet build DronePilot.csproj -c Release -p:UnityManagedPath=<Unity-managed-directory>
```

The output contains `DronePilot.dll`, `YamlDotNet.dll`, and
`Newtonsoft.Json.dll`. Unity assemblies come from the host game and must not
be shipped with this library. The prebuilt model bundle in `assets/drone` is
embedded in `DronePilot.dll` at build time.
