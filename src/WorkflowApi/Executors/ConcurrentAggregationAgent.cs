using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using WorkflowApi.Frauds;
using WorkflowApi.Incomes;
using WorkflowApi.Kycs;

namespace WorkflowApi.Executors;

internal sealed class ConcurrentAggregationAgent : AIAgent
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public override string Name => "ConcurrentAggregationAgent";
    public readonly ChatHistoryProvider ChatHistoryProvider = new InMemoryChatHistoryProvider();

    protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default)
        => new(new ConcurrentAggregationAgentSession());

    protected override ValueTask<JsonElement> SerializeSessionCoreAsync(AgentSession session, JsonSerializerOptions? jsonSerializerOptions = null, CancellationToken cancellationToken = default)
    {
        if (session is not ConcurrentAggregationAgentSession typedSession)
        {
            throw new ArgumentException($"The provided session is not of type {nameof(ConcurrentAggregationAgentSession)}.", nameof(session));
        }

        return new(JsonSerializer.SerializeToElement(typedSession, jsonSerializerOptions));
    }

    protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(JsonElement serializedState, JsonSerializerOptions? jsonSerializerOptions = null, CancellationToken cancellationToken = default)
        => new(serializedState.Deserialize<ConcurrentAggregationAgentSession>(jsonSerializerOptions)!);

    protected override async Task<AgentResponse> RunCoreAsync(IEnumerable<ChatMessage> messages, AgentSession? session = null, AgentRunOptions? options = null, CancellationToken cancellationToken = default)
    {
        // Create a session if the user didn't supply one.
        session ??= await this.CreateSessionAsync(cancellationToken);

        if (session is not ConcurrentAggregationAgentSession typedSession)
        {
            throw new ArgumentException($"The provided session is not of type {nameof(ConcurrentAggregationAgentSession)}.", nameof(session));
        }

        // Convert to list to avoid multiple enumeration
        var messagesList = messages.ToList();

        // Get existing messages from the store
        var invokingContext = new ChatHistoryProvider.InvokingContext(this, session, messagesList);
        var userAndChatHistoryMessages = (await this.ChatHistoryProvider.InvokingAsync(invokingContext, cancellationToken)).ToList();

        // Combine all messages (chat history + new messages)
        var allMessages = userAndChatHistoryMessages.Concat(messagesList).ToList();

        // Check if we have a finalized decision already
        var finalDecision = typedSession.GetFinalDecision();
        DecisionResult decisionJson;

        if (finalDecision != null)
        {
            // Return the cached final decision
            decisionJson = finalDecision;
        }
        else
        {
            // Try to aggregate with current messages
            decisionJson = AggregateAndDecide(allMessages);
            
            // If we have a complete decision (not Pending), cache it in the session
            if(decisionJson.Outcome == "Pending")
                typedSession.SetFinalDecision(decisionJson);
            
        }

        // Create response message with the decision
        var responseMessage = new ChatMessage(ChatRole.Assistant, JsonSerializer.Serialize(decisionJson, JsonOptions))
        {
            MessageId = Guid.NewGuid().ToString("N"),
            AuthorName = this.Name
        };

        List<ChatMessage> responseMessages = [responseMessage];

        // Notify the session of the input and output messages.
        var invokedContext = new ChatHistoryProvider.InvokedContext(this, session, userAndChatHistoryMessages, responseMessages);
        await this.ChatHistoryProvider.InvokedAsync(invokedContext, cancellationToken);

        return new AgentResponse
        {
            AgentId = this.Id,
            ResponseId = Guid.NewGuid().ToString("N"),
            Messages = responseMessages
        };
    }

    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(IEnumerable<ChatMessage> messages, AgentSession? session = null, AgentRunOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Create a session if the user didn't supply one.
        session ??= await this.CreateSessionAsync(cancellationToken);

        if (session is not ConcurrentAggregationAgentSession typedSession)
        {
            throw new ArgumentException($"The provided session is not of type {nameof(ConcurrentAggregationAgentSession)}.", nameof(session));
        }

        // Convert to list to avoid multiple enumeration
        var messagesList = messages.ToList();

        // Get existing messages from the store
        var invokingContext = new ChatHistoryProvider.InvokingContext(this, session, messagesList);
        var userAndChatHistoryMessages = (await this.ChatHistoryProvider.InvokingAsync(invokingContext, cancellationToken)).ToList();

        // Combine all messages (chat history + new messages)
        var allMessages = userAndChatHistoryMessages.Concat(messagesList).ToList();

        // Check if we have a finalized decision already
        var finalDecision = typedSession.GetFinalDecision();
        DecisionResult decisionJson;

        if (finalDecision != null)
        {
            // Return the cached final decision
            decisionJson = finalDecision;
        }
        else
        {
            // Try to aggregate with current messages
            decisionJson = AggregateAndDecide(allMessages);
            
            // If we have a complete decision (not Pending), cache it in the session
            if (decisionJson.Outcome == "Pending")
                typedSession.SetFinalDecision(decisionJson);
        }

        // Yield response update with the decision
        yield return new AgentResponseUpdate
        {
            AgentId = this.Id,
            AuthorName = this.Name,
            Role = ChatRole.Assistant,
            Contents = [new TextContent(JsonSerializer.Serialize(decisionJson, JsonOptions))],
            ResponseId = Guid.NewGuid().ToString("N"),
            MessageId = Guid.NewGuid().ToString("N")
        };

        // Notify the session of the input and output messages.
        var responseMessage = new ChatMessage(ChatRole.Assistant, JsonSerializer.Serialize(decisionJson, JsonOptions))
        {
            MessageId = Guid.NewGuid().ToString("N"),
            AuthorName = this.Name
        };

        var invokedContext = new ChatHistoryProvider.InvokedContext(this, session, userAndChatHistoryMessages, [responseMessage]);
        await this.ChatHistoryProvider.InvokedAsync(invokedContext, cancellationToken);
    }

    private DecisionResult AggregateAndDecide(List<ChatMessage> messages)
    {
        // Check if we already have a final decision in the history
        var previousDecision = messages
            .Where(m => string.Equals(m.AuthorName, this.Name, StringComparison.OrdinalIgnoreCase))
            .Select(m => TryParseDecision(m.Text))
            .FirstOrDefault(d => d != null);

        if (previousDecision != null && previousDecision.Outcome != "Pending")
        {
            // Return the existing final decision
            return previousDecision;
        }

        // Check if we have messages from all three agents
        var hasKyc = messages.Any(m => string.Equals(m.AuthorName, "KYC", StringComparison.OrdinalIgnoreCase));
        var hasFraud = messages.Any(m => string.Equals(m.AuthorName, "Fraud", StringComparison.OrdinalIgnoreCase));
        var hasIncome = messages.Any(m => string.Equals(m.AuthorName, "Income", StringComparison.OrdinalIgnoreCase));

        // Only aggregate if we have all three responses
        if (!hasKyc || !hasFraud || !hasIncome)
        {
            // If we're missing any responses, return empty decision
            return new DecisionResult
            {
                Outcome = "Pending",
                Conditions = Array.Empty<string>(),
                Summary = "Waiting for all agents to complete...",
                Details = new DecisionDetails()
            };
        }

        // Parse messages from all three agents
        var kyc = Parse<KycResult>(messages, "KYC");
        var fraud = Parse<FraudResult>(messages, "Fraud");
        var income = Parse<IncomeResult>(messages, "Income");

        var decision = Decide(kyc, fraud, income);
        return decision;
    }

    private DecisionResult? TryParseDecision(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        try
        {
            return JsonSerializer.Deserialize<DecisionResult>(text, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static T Parse<T>(List<ChatMessage> messages, string agentName) where T : class, new()
    {
        var last = messages.LastOrDefault(m => string.Equals(m.AuthorName, agentName, StringComparison.OrdinalIgnoreCase));
        if (last?.Text is null)
        {
            return new T();
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<T>(last.Text, JsonOptions) ?? new T();
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
            return new T();
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

    /// <summary>
    /// A session type for the concurrent aggregation agent.
    /// </summary>
    internal sealed class ConcurrentAggregationAgentSession : AgentSession
    {
        private DecisionResult? _finalDecision;

        internal ConcurrentAggregationAgentSession()
        {
        }

        [JsonConstructor]
        internal ConcurrentAggregationAgentSession(AgentSessionStateBag stateBag) : base(stateBag)
        {
        }

        internal DecisionResult? GetFinalDecision() => _finalDecision;

        internal void SetFinalDecision(DecisionResult decision) => _finalDecision = decision;
    }
}

