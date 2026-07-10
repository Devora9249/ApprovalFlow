namespace PaymentService;

public interface IEventPublisher
{
    Task PublishAsync<T>(string topicName, T payload);
}
