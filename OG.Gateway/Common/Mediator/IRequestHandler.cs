namespace Application.Common.Mediator;

/// <summary>
/// Contrato que todo handler debe implementar para procesar un request del mediador.
/// </summary>
public interface IRequestHandler<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken = default);
}
