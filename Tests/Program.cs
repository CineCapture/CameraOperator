using System;
using DronePilot;

// Runs targeted YAML loading, serialization, and validation checks.
internal static class Program
{
    // Verifies the embedded template round trips and invalid ranges fail.
    private static void Main()
    {
        DroneConfiguration config = DroneConfiguration.Parse(
            DroneConfiguration.DefaultYaml());
        if (config.Count(
            "flight_modes.trailing_flight.avoidance.detection.route_offsets") != 10 ||
            config.Number(
            "flight_modes.trailing_flight.avoidance.detection.route_offsets[0][0]") != -1f)
        {
            throw new Exception("Route offsets were not parsed.");
        }
        DroneConfiguration.Parse(config.ToYaml());
        string invalid = DroneConfiguration.DefaultYaml().Replace(
            "trailing_entry_distance: 5.0",
            "trailing_entry_distance: 1.0");
        try
        {
            DroneConfiguration.Parse(invalid);
            throw new Exception("Invalid hysteresis was accepted.");
        }
        catch (FormatException)
        {
            string outOfRange = DroneConfiguration.DefaultYaml().Replace(
                "maximum_pitch_angle: 70.0",
                "maximum_pitch_angle: 120.0");
            try
            {
                DroneConfiguration.Parse(outOfRange);
                throw new Exception("Documented maximum was ignored.");
            }
            catch (FormatException)
            {
                Console.WriteLine("Drone YAML smoke checks passed.");
            }
        }
    }
}
