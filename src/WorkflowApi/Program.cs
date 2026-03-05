using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.DevUI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using WorkflowApi.Executors;
using WorkflowApi.Frauds;
using WorkflowApi.Incomes;
using WorkflowApi.Kycs;

var builder = WebApplication.CreateBuilder(args);

var endpoint = builder.Configuration["AZURE_OPENAI_ENDPOINT"] ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not set.");
var deploymentName = builder.Configuration["AZURE_OPENAI_DEPLOYMENT_NAME"] ?? "gpt-4.1-mini";
var chatClient = new AzureOpenAIClient(
        new Uri(endpoint), new AzureCliCredential())
    .GetChatClient(deploymentName)
    .AsIChatClient();

builder.Services.AddChatClient(chatClient);

builder.AddAIAgent("KYC", "You validate identity. Use the ValidateCpf to validate the CPF. Return ONLY a JSON object with keys: " +
                          "agent (must be exactly \"KYC\"), status (string: Approved|Rejected|Review), notes (string).")
    .WithAITool(AIFunctionFactory.Create(KycTools.ValidateCpf, name: "validate_cpf"));

builder.AddAIAgent("Fraud", "You assess fraud risk. Use the FraudTool to score the application. Return ONLY a JSON object with keys: " +
                            "agent (must be exactly \"Fraud\"), riskScore (string: Low|Medium|High|Review), notes (string).")
    .WithAITool(AIFunctionFactory.Create(FraudTools.FraudTool, name: "fraud_tool"));

builder.AddAIAgent("Income",  "You assess income capacity. Use the IncomeTool to score the application. Return ONLY a JSON object with keys: " +
                              "agent (must be exactly \"Income\"), status (string: Sufficient|Insufficient|Review), notes (string).")
    .WithAITool(AIFunctionFactory.Create(IncomeTools.IncomeTool, name: "income_tool"));

builder.Services.AddSingleton(new ConcurrentStartAgent());
builder.Services.AddSingleton(new ConcurrentAggregationAgent());

builder.AddWorkflow("credit-workflow", (sp, key) =>
{
    var kycAgent = sp.GetRequiredKeyedService<AIAgent>("KYC");
    var fraudAgent = sp.GetRequiredKeyedService<AIAgent>("Fraud");
    var incomeAgent = sp.GetRequiredKeyedService<AIAgent>("Income");
    var startAgent = sp.GetRequiredService<ConcurrentStartAgent>();
    var aggregationAgent = sp.GetRequiredService<ConcurrentAggregationAgent>();
    
    var workflow = new WorkflowBuilder(startAgent)
        .AddFanOutEdge(startAgent, [kycAgent, fraudAgent, incomeAgent])
        .AddFanInBarrierEdge([kycAgent, fraudAgent, incomeAgent], aggregationAgent)
        .WithOutputFrom(aggregationAgent)
        .WithName(key)
        .Build();

    return workflow;
    
}).AddAsAIAgent();

builder.Services.AddOpenAIResponses();
builder.Services.AddOpenAIConversations();

var app = builder.Build();

app.MapOpenAIResponses();
app.MapOpenAIConversations();

var enableDevUi = app.Environment.IsDevelopment() ||
                  string.Equals(builder.Configuration["ENABLE_DEVUI"], "true", StringComparison.OrdinalIgnoreCase);
if (enableDevUi)
{
    app.MapDevUI();
}

Console.WriteLine("DevUI is available at /devui (when enabled). Check ASPNETCORE_ENVIRONMENT or ENABLE_DEVUI.");
Console.WriteLine("OpenAI Responses API is available at /v1/responses.");

app.Run();
