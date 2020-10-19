using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using System.Threading;
using Microsoft.Azure.Management.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent;
using System.Configuration;
using System.Linq;
using Microsoft.Azure.Management.ContainerInstance.Fluent;
using Microsoft.Azure.Management.ContainerService.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent.Authentication;

namespace FunctionAppHttp
{
    public static class Function1
    {
        private static readonly string AzureAuthFile = Environment.GetEnvironmentVariable("AzureAuthFileName");
        private static readonly string SubscriptionName = Environment.GetEnvironmentVariable("SubscriptionName");
        private static readonly string ResourceGroupName = Environment.GetEnvironmentVariable("ResourceGroupName");
        private static readonly string ContainerGroupNames = Environment.GetEnvironmentVariable("ContainerGroupNames");
        private static readonly string TenantId = Environment.GetEnvironmentVariable("TenantId");
        private static readonly string ClientId = Environment.GetEnvironmentVariable("ClientId");
        private static readonly string Secret = Environment.GetEnvironmentVariable("Secret");
        private static readonly string InstanceId = Environment.GetEnvironmentVariable("OrchestratorInstanceId");

        [FunctionName("RaiseOrchestratorHttp")]
        public static async Task Raise([HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
                [DurableClient] IDurableOrchestrationClient client,
                Microsoft.Azure.WebJobs.ExecutionContext executionContext,
                ILogger log)
        {
            string instanceId = InstanceId;
            var eventData = req.Query["event"];
            var existingInstance = await client.GetStatusAsync(instanceId);

            if (eventData == "stop")
            {
                log.LogInformation("Start orchestration");

                await client.StartNewAsync("OrchestrationWorkflow", instanceId);
            }
            else if (eventData == "start")
            {
                await Start(GetAzureContext(executionContext), log);

                if (existingInstance == null
                    || existingInstance.RuntimeStatus == OrchestrationRuntimeStatus.Completed
                    || existingInstance.RuntimeStatus == OrchestrationRuntimeStatus.Failed
                    || existingInstance.RuntimeStatus == OrchestrationRuntimeStatus.Terminated)
                {
                    log.LogInformation("There is no timers to interrupt");
                }
                else
                {
                    log.LogInformation("Raise start instance event");

                    bool isStartEvent = true;
                    await client.RaiseEventAsync(instanceId, "StartEvent", isStartEvent);
                }
            }
        }

        [FunctionName("OrchestrationWorkflow")]
        public static async Task Run([OrchestrationTrigger] IDurableOrchestrationContext context,
            Microsoft.Azure.WebJobs.ExecutionContext executionContext, ILogger log)
        {
            log.LogInformation("Enter orchestration workflow");
            using (var timeoutCts = new CancellationTokenSource())
            {
                DateTime dueTime = context.CurrentUtcDateTime.AddSeconds(15);
                Task durableTimeout = context.CreateTimer(dueTime, timeoutCts.Token);

                log.LogInformation("Start timer");

                Task<bool> startEvent = context.WaitForExternalEvent<bool>("StartEvent");

                if (startEvent == await Task.WhenAny(startEvent, durableTimeout))
                {
                    timeoutCts.Cancel();
                    log.LogInformation("Stop timer");
                    return;
                }
                else
                {
                    await Stop(GetAzureContext(executionContext), log);
                    log.LogInformation("Stop instance");
                    return;
                }
            }
        }

        private static IAzure GetAzureContext(Microsoft.Azure.WebJobs.ExecutionContext context)
        {
            //var azureAuthFile = Path.Combine(context.FunctionAppDirectory, AzureAuthFile);
            //var credentials = SdkContext.AzureCredentialsFactory
            //    .FromFile(Environment.GetEnvironmentVariable(azureAuthFile));

            var servicePrinciple = new ServicePrincipalLoginInformation()
            {
                ClientId = ClientId,
                ClientSecret = Secret,
            };

            var azureCredentials = new AzureCredentials(servicePrinciple,TenantId,AzureEnvironment.AzureGlobalCloud);
            try
            {
                return Microsoft.Azure.Management.Fluent.Azure
                    .Configure()
                    .Authenticate(azureCredentials).WithSubscription(SubscriptionName);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }

            return null;
        }

        private static async Task Start(IAzure azure, ILogger log)
        {
            var acis = ContainerGroupNames.Split(",");
            foreach (var aci in acis)
            {
                await azure
                    .ContainerGroups
                    .StartAsync(ResourceGroupName, aci);
                log.LogInformation($"${aci} has been started");
            }
        }

        private static async Task Stop(IAzure azure, ILogger log)
        {
            var containers = await azure
                .ContainerGroups
                .ListAsync();

            var acis = ContainerGroupNames.Split(",");
            foreach (var aci in acis)
            {
                await azure
                    .ContainerGroups
                    .GetById(containers.FirstOrDefault(x => x.Name == aci && x.ResourceGroupName == ResourceGroupName)?.Id)
                    .StopAsync();
                log.LogInformation($"${aci} has been stopped");
            }
        }
    }
}
