using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Text;

namespace EventDriven.Consumer.Worker
{
    public class WorkerMetrics
    {
        private readonly Counter<long> _messageCounter;

        public WorkerMetrics(IMeterFactory meterFactory)
        {
            var meter = meterFactory.Create("EventDriven.Worker");
            _messageCounter = meter.CreateCounter<long>("total_processed_events");
        }

        public void IncrementProcessedEvents(string eventType, string status)
        {
            _messageCounter.Add(1,
                new KeyValuePair<string, object?>("tipo_evento", eventType),
                new KeyValuePair<string, object?>("estado", status));
        }
    }
}