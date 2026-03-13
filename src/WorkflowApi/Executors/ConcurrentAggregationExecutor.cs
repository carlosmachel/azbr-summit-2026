using System.Text.Json;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using WorkflowApi.Frauds;
using WorkflowApi.Incomes;
using WorkflowApi.Kycs;

namespace WorkflowApi.Executors;

/// <summary>
/// Executor that aggregates the results from the concurrent agents.
/// </summary>
internal sealed class ConcurrentAggregationExecutor() :
    Executor<List<ChatMessage>, string?>("ConcurrentAggregationExecutor")
{
    private const string MessagesStateKey = "aggregation_messages";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Handles incoming messages from the agents and aggregates their responses.
    /// </summary>
    /// <param name="message">The messages from the agent</param>
    /// <param name="context">Workflow context for accessing workflow services and adding events</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.
    /// The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task representing the asynchronous operation</returns>
    public override async ValueTask<string?> HandleAsync(List<ChatMessage> message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        // Read or initialize per-execution message list from workflow state store
        var messages = await context.ReadOrInitStateAsync(
            MessagesStateKey,
            () => new List<ChatMessage>(),
            cancellationToken);

        foreach (var msg in message)
        {
            System.Console.WriteLine($"Agent={msg.AuthorName} Text={msg.Text}");
        }

        foreach (var msg in message)
        {
            if (!string.IsNullOrWhiteSpace(msg.Text))
            {
                messages.Add(msg);
            }
        }

        // Persist updated messages back to workflow state
        await context.QueueStateUpdateAsync(MessagesStateKey, messages, cancellationToken);

        if (messages.Count >= 3)
        {
            var kyc = Parse<KycResult>(messages, "KYC");
            var fraud = Parse<FraudResult>(messages, "Fraud");
            var income = Parse<IncomeResult>(messages, "Income");

            if (kyc != null && fraud != null && income != null)
            {
                var decision = Decide(kyc, fraud, income);
                return JsonSerializer.Serialize(decision, JsonOptions);
            }
        }

        return null;
    }

    private static T? Parse<T>(List<ChatMessage> messages, string agentName) where T : class, new()
    {
        var message = messages.LastOrDefault(m => string.Equals(m.AuthorName, agentName, StringComparison.OrdinalIgnoreCase));
        if (message?.Text is null)
        {
            return null;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<T>(message.Text, JsonOptions) ?? new T();
            var agentProp = typeof(T).GetProperty("Agent");
            if (agentProp is not null)
            {
                var current = agentProp.GetValue(parsed) as string;
                if (string.IsNullOrWhiteSpace(current) || current.StartsWith("functions.", StringComparison.OrdinalIgnoreCase))
                {
                    agentProp.SetValue(parsed, agentName);
                }
            }

            return parsed;
        }
        catch
        {
            return null;
        }
    }

    private static DecisionResult Decide(KycResult kyc, FraudResult fraud, IncomeResult income)
    {
        var approved = string.Equals(kyc.Status, "Approved", StringComparison.OrdinalIgnoreCase) &&
                       string.Equals(income.Status, "Sufficient", StringComparison.OrdinalIgnoreCase);

        var conditions = new List<string>();
        if (string.Equals(fraud.RiskScore, "Medium", StringComparison.OrdinalIgnoreCase))
        {
            conditions.Add("Require manual fraud review");
        }
        else if (string.Equals(fraud.RiskScore, "High", StringComparison.OrdinalIgnoreCase))
        {
            approved = false;
        }

        var outcome = approved ? "Approved" : "Rejected";
        var reason = approved
            ? "KYC approved and income sufficient; fraud risk acceptable."
            : "One or more checks failed or require manual review.";

        return new DecisionResult
        {
            Outcome = outcome,
            Conditions = conditions.ToArray(),
            Summary = reason,
            Details = new DecisionDetails
            {
                Kyc = kyc,
                Fraud = fraud,
                Income = income
            }
        };
    }
}
