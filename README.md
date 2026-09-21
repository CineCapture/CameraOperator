# DronePilot

DronePilot flies a supplied Unity `Camera` around a target `GameObject`. It does
not create the camera, record video, or depend on a particular game.

- Supply a `DroneWorld` for ground height, collider filtering, actor detection,
  and optional logging.
- Supply ordered `PilotProfile` instances. The first active condition wins;
  the last profile acts as the fallback.
- Create a `DronePilotController` after camera warmup. Pass target velocity and
  maximum speed to `Update` from the Unity main thread after target movement.
- Optionally pass a caller-owned visual `GameObject`. DronePilot moves it with
  the camera but does not create, render, or destroy it.
- Dispose the controller when it releases camera control. The camera remains
  owned by the caller.

`drone-config.yaml` is embedded in the DLL. A missing configuration file is
created with all documented defaults. Existing files are validated, then polled
once per second for changes. Invalid reloads keep the last valid settings.

Optional telemetry writes sampled flight data and events as JSON arrays under
`<root>/Sessions/<session>/`. The caller chooses telemetry options for each
session.

## Build

Use .NET SDK 10 or a compatible MSBuild and provide the directory containing
Unity's managed `UnityEngine.CoreModule.dll` and
`UnityEngine.PhysicsModule.dll`:

```text
dotnet build DronePilot.csproj -c Release -p:UnityManagedPath=<Unity-managed-directory>
```

The output contains `DronePilot.dll`, `YamlDotNet.dll`, and
`Newtonsoft.Json.dll`. The Unity assemblies are provided by the host game and
must not be shipped with this library.

Run the targeted YAML checks with:

```text
dotnet run --project Tests/DroneConfigurationSmoke.csproj -c Release -p:UnityManagedPath=<Unity-managed-directory>
```
