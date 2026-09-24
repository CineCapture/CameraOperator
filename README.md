# CameraOperator

CameraOperator moves a Unity camera smoothly around a target.

It keeps the target in view, follows terrain, and avoids obstacles. The host
application owns the camera and decides when to move it to a new position.

## What it does

- After each `Reposition` call, follows the target from the requested world
  direction and distance.
- Aims at a configurable height on the target.
- Smooths movement, acceleration, and braking.
- Follows terrain and avoids obstacles.
- Reloads valid `config.yaml` changes while running.

## Host responsibilities

The host application acts as the director. It is expected to:

- create and own the camera;
- decide whether to show this camera or the gameplay camera;
- choose a starting position and call `Reposition` for each new view;
- decide how long each view lasts and when to switch to another one;
- start and stop video recording when needed.

CameraOperator only moves and aims the camera between reposition requests.

## Create an operator

```csharp
using CameraOperator;
using UnityEngine;

CameraWorld world = new CameraWorld
{
    GroundHeight = position => GetGroundHeight(position),
    IgnoreObstacle = collider => collider.isTrigger,
    IsActor = collider => collider.CompareTag("Player"),
    LogInfo = Debug.Log,
    LogWarning = Debug.LogWarning
};

CameraProfile[] profiles =
{
    new CameraProfile("Forest", 4f, position => IsForest(position)),
    new CameraProfile("Open area", 8f, position => true)
};

CameraOperatorController cameraOperator = new CameraOperatorController(
    camera, player, configPath, world, profiles);
```

The first matching profile is used. Keep a final profile that always matches as
the fallback.

## Update the camera

Call `Update` after the target has moved:

```csharp
private void LateUpdate()
{
    cameraOperator.Update(playerVelocity, playerMaximumSpeed);
}
```

## Start from a new position

Use `Reposition` when another system chooses a new camera position:

```csharp
Vector3 newPosition = player.transform.position +
    new Vector3(-5f, 3f, 0f);

cameraOperator.Reposition(newPosition, playerVelocity);
```

CameraOperator then follows the target from this new direction and distance.

## Clean up

```csharp
cameraOperator.Dispose();
```

CameraOperator does not destroy the camera or target.

## Configuration

Pass the path to `config.yaml` when creating the operator. If the file is
missing, CameraOperator creates it with default values.

The file controls placement, aiming, movement, terrain following, obstacle
avoidance, and recovery behavior.
