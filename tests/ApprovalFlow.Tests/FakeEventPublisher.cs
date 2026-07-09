using DecisionService;

namespace ApprovalFlow.Tests;

internal class FakeEventPublisher : IEventPublisher
{
    public List<(string Topic, object? Payload)> Published { get; } = [];

    public Task PublishAsync<T>(string topicName, T payload)
    {
        Published.Add((topicName, payload));
        return Task.CompletedTask;
    }
}
