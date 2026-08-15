using System.Threading.Channels;

namespace Application.Common.RiskEngine.AnomalyDetection;

// Registrar como Singleton: el canal vive toda la aplicación. DropWrite para que nunca bloquee al
// escritor (la evaluación no espera); SingleReader=true (un solo worker); SingleWriter=false.
public sealed class InMemoryProfileUpdateChannel : IProfileUpdateChannel
{
    private readonly Channel<ProfileUpdate> _channel;

    public InMemoryProfileUpdateChannel(AnomalyDetectionOptions options)
    {
        _channel = Channel.CreateBounded<ProfileUpdate>(new BoundedChannelOptions(options.ProfileUpdateChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
    }

    public bool TryWrite(ProfileUpdate update) => _channel.Writer.TryWrite(update);

    public IAsyncEnumerable<ProfileUpdate> ReadAllAsync(CancellationToken cancellationToken = default)
        => _channel.Reader.ReadAllAsync(cancellationToken);
}
