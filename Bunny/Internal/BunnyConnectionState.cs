using RabbitMQ.Client;

namespace Bunny.Internal;

internal sealed class BunnyConnectionState
{
    private IConnection? _connection;
    private IChannel? _publishChannel;
    private readonly SemaphoreSlim _publishLock = new(1, 1);

    public IConnection Connection => _connection ?? throw NotReady();
    public IChannel PublishChannel => _publishChannel ?? throw NotReady();
    public SemaphoreSlim PublishLock => _publishLock;
    public bool IsReady => _connection is not null && _publishChannel is not null;

    public void SetConnection(IConnection connection) => _connection = connection;
    public void SetPublishChannel(IChannel channel) => _publishChannel = channel;

    public void Reset()
    {
        _connection = null;
        _publishChannel = null;
    }

    private static InvalidOperationException NotReady()
        => new("Bunny connection is not initialized yet - hosted service has not started.");
}
