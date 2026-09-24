namespace CameraOperator
{
    // Centralizes numerical safeguards that are not user-facing camera tuning.
    internal static class CameraConstants
    {
        internal const float MeaningfulTargetVelocitySquared = 0.04f;
        internal const float DestinationOffsetSquared = 0.0001f;
        internal const float DirectionSquared = 0.001f;
        internal const float MinimumSegmentLength = 0.001f;
        internal const float MinimumDeltaTime = 0.0001f;
        internal const float MinimumRefreshElapsed = 0.001f;
    }
}
