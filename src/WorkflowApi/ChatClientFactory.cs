using Azure.AI.OpenAI;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.AI;
using OpenAI.Chat;

namespace WorkflowApi;

public static class ChatClientFactory
{
    public static IChatClient Create(IConfiguration configuration)
    {
        var endpoint = configuration["AZURE_OPENAI_ENDPOINT"] ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not set.");
        var deploymentName = configuration["AZURE_OPENAI_DEPLOYMENT_NAME"] ?? "gpt-4.1-mini";
        
        var chatClient = new AzureOpenAIClient(
                new Uri(endpoint), GetCredentials(configuration))
            .GetChatClient(deploymentName)
            .AsIChatClient();

        return chatClient;
    }

    private static TokenCredential GetCredentials(IConfiguration configuration)
    {
        var tenantId = configuration["TENANT_ID"];
        var clientId = configuration["CLIENT_ID"];
        var clientSecret = configuration["CLIENT_SECRET"];

        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
        {
            return new DefaultAzureCredential();
        }
        else
        {
            return new ClientSecretCredential(tenantId, clientId, clientSecret);
        }
    }
}