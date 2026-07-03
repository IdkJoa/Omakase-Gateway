using System.Threading.Channels;

namespace Application.Common.RiskEngine.AnomalyDetection;

/// <summary>
/// Implementación del <see cref="IProfileUpdateChannel"/> con <see cref="System.Threading.Channels"/>.
/// Registrar como <b>Singleton</b>: el canal vive toda la aplicación.
/// <para><b>FullMode</b> = <see cref="BoundedChannelFullMode.DropWrite"/>: nunca bloquea al escritor (la
/// evaluación no espera). <b>SingleReader</b> = true (un solo worker); <b>SingleWriter</b> = false.</para>
/// </summary>
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

    /// <inheritdoc/>
    public bool TryWrite(ProfileUpdate update) => _channel.Writer.TryWrite(update);

    /// <inheritdoc/>
    public IAsyncEnumerable<ProfileUpdate> ReadAllAsync(CancellationToken cancellationToken = default)
        => _channel.Reader.ReadAllAsync(cancellationToken);
}
