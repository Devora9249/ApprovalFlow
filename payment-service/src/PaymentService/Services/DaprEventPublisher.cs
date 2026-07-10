using Dapr.Client;

namespace PaymentService;

public class DaprEventPublisher(DaprClient daprClient) : IEventPublisher
{
    private const string PubSubName = "pubsub";

    public Task PublishAsync<T>(string topicName, T payload) =>
        daprClient.PublishEventAsync(PubSubName, topicName, payload);
}
