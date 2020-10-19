// Default URL for triggering event grid function in the local environment.
// http://localhost:7071/runtime/webhooks/EventGrid?functionName={functionname}
using System;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.EventGrid.Models;
using Microsoft.Azure.WebJobs.Extensions.EventGrid;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using AzureDurableFunction1.Models;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;

namespace AzureDurableFunction1
{
    public static class Function1
    {
        [FunctionName("RaiseEventToOrchestration")]
        public static async Task Raise([EventGridTrigger]EventGridEvent eventGridEvent,
            [DurableClient] IDurableOrchestrationClient client,
            ILogger log)
        {
            log.LogInformation(eventGridEvent.Data.ToString());

            if(eventGridEvent.Data is EventModel)
            {
                log.LogInformation("enter if statement");
                var eventData = (EventModel)eventGridEvent.Data;

                if(eventData.EventType == "stop")
                {
                    log.LogInformation("Start orchestration");
                    await client.StartNewAsync("OrchestrationWorkflow");
                }
                else if(eventData.EventType == "start")
                {
                    log.LogInformation("Raise start event");
                    await client.RaiseEventAsync(eventData.Id.ToString(), "StartEvent", eventData.EventType);
                }
            }
            else
            {
                log.LogInformation("enter else statement");
            }

            return;
        }

        [FunctionName("OrchestrationWorkflow")]
        public static async Task Run([OrchestrationTrigger] IDurableOrchestrationContext context, ILogger log)
        {
            log.LogInformation("start orchestrator");
            using (var timeoutCts = new CancellationTokenSource())
            {
                DateTime dueTime = context.CurrentUtcDateTime.AddMinutes(5);
                Task durableTimeout = context.CreateTimer(dueTime, timeoutCts.Token);

                Task<bool> startEvent = context.WaitForExternalEvent<bool>("StartEvent");

                if(startEvent == await Task.WhenAny(startEvent, durableTimeout))
                {
                    timeoutCts.Cancel();
                    log.LogInformation("Stop timer");
                    return;
                }
                else
                {
                    log.LogInformation("Stop instance");
                    return;
                }
            }
        }
    }
}
