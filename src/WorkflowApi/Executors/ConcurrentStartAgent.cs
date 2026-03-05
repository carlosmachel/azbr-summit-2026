using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace WorkflowApi.Executors;

internal sealed class ConcurrentStartAgent : AIAgent
{
    public readonly ChatHistoryProvider ChatHistoryProvider = new InMemoryChatHistoryProvider();

    public override string Name => "ConcurrentStartAgent";

    protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default)
        => new(new ConcurrentStartAgentSession());

    protected override ValueTask<JsonElement> SerializeSessionCoreAsync(AgentSession session, JsonSerializerOptions? jsonSerializerOptions = null, CancellationToken cancellationToken = default)
    {
        if (session is not ConcurrentStartAgentSession typedSession)
        {
            throw new ArgumentException($"The provided session is not of type {nameof(ConcurrentStartAgentSession)}.", nameof(session));
        }

        return new(JsonSerializer.SerializeToElement(typedSession, jsonSerializerOptions));
    }

    protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(JsonElement serializedState, JsonSerializerOptions? jsonSerializerOptions = null, CancellationToken cancellationToken = default)
        => new(serializedState.Deserialize<ConcurrentStartAgentSession>(jsonSerializerOptions)!);

    protected override async Task<AgentResponse> RunCoreAsync(IEnumerable<ChatMessage> messages, AgentSession? session = null, AgentRunOptions? options = null, CancellationToken cancellationToken = default)
    {
        // Create a session if the user didn't supply one.
        session ??= await this.CreateSessionAsync(cancellationToken);

        if (session is not ConcurrentStartAgentSession)
        {
            throw new ArgumentException($"The provided session is not of type {nameof(ConcurrentStartAgentSession)}.", nameof(session));
        }

        // Convert to list to avoid multiple enumeration
        var messagesList = messages.ToList();

        // Get existing messages from the store
        var invokingContext = new ChatHistoryProvider.InvokingContext(this, session, messagesList);
        var userAndChatHistoryMessages = await this.ChatHistoryProvider.InvokingAsync(invokingContext, cancellationToken);

        // Clone the input messages
        List<ChatMessage> responseMessages = messagesList.Select(x =>
        {
            var messageClone = x.Clone();
            messageClone.Role = ChatRole.Assistant;
            messageClone.MessageId = Guid.NewGuid().ToString("N");
            messageClone.AuthorName = this.Name;
            return messageClone;
        }).ToList();

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

        if (session is not ConcurrentStartAgentSession)
        {
            throw new ArgumentException($"The provided session is not of type {nameof(ConcurrentStartAgentSession)}.", nameof(session));
        }

        // Convert to list to avoid multiple enumeration
        var messagesList = messages.ToList();

        // Get existing messages from the store
        var invokingContext = new ChatHistoryProvider.InvokingContext(this, session, messagesList);
        var userAndChatHistoryMessages = await this.ChatHistoryProvider.InvokingAsync(invokingContext, cancellationToken);

        // Clone the input messages
        List<ChatMessage> responseMessages = messagesList.Select(x =>
        {
            var messageClone = x.Clone();
            messageClone.Role = ChatRole.Assistant;
            messageClone.MessageId = Guid.NewGuid().ToString("N");
            messageClone.AuthorName = this.Name;
            return messageClone;
        }).ToList();

        // Notify the session of the input and output messages.
        var invokedContext = new ChatHistoryProvider.InvokedContext(this, session, userAndChatHistoryMessages, responseMessages);
        await this.ChatHistoryProvider.InvokedAsync(invokedContext, cancellationToken);

        foreach (var message in responseMessages)
        {
            yield return new AgentResponseUpdate
            {
                AgentId = this.Id,
                AuthorName = message.AuthorName,
                Role = ChatRole.Assistant,
                Contents = message.Contents,
                ResponseId = Guid.NewGuid().ToString("N"),
                MessageId = Guid.NewGuid().ToString("N")
            };
        }
    }

    /// <summary>
    /// A session type for the concurrent start agent.
    /// </summary>
    internal sealed class ConcurrentStartAgentSession : AgentSession
    {
        internal ConcurrentStartAgentSession()
        {
        }

        [JsonConstructor]
        internal ConcurrentStartAgentSession(AgentSessionStateBag stateBag) : base(stateBag)
        {
        }
    }
}

