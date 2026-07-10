using PaymentService;

namespace ApprovalFlow.Tests;

internal class FakePaymentEventPublisher : IEventPublisher
{
    public List<(string Topic, object? Payload)> Published { get; } = [];

    public Task PublishAsync<T>(string topicName, T payload)
    {
        Published.Add((topicName, payload));
        return Task.CompletedTask;
    }
}
