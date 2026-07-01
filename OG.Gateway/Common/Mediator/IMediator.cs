namespace Application.Common.Mediator;

/// <summary>
/// Bus de commands/queries del Gateway.
/// Desacopla al emisor de la petición de su handler concreto.
/// </summary>
public interface IMediator
{
    /// <summary>
    /// Despacha un request al handler registrado y devuelve la respuesta.
    /// </summary>
    /// <typeparam name="TResponse">Tipo de respuesta producida por el handler.</typeparam>
    /// <param name="request">El request a despachar.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    Task<TResponse> SendAsync<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default);
}
