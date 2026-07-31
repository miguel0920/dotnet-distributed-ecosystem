using System.Diagnostics;

namespace EventDriven.Contracts
{
    public static class TelemetryDiagnostics
    {
        public static readonly ActivitySource Source = new("EventDriven.Telemetry", "1.0.0");
    }
}