using Microsoft.Extensions.DependencyInjection;

namespace Application.Common.Mediator;

/// <summary>
/// Implementación lightweight del mediador.
/// Resuelve el handler correspondiente desde el contenedor DI en tiempo de ejecución
/// usando el tipo concreto del request para construir el tipo genérico del handler.
/// </summary>
/// <remarks>
/// El despacho se realiza mediante reflexión puntual sobre la interfaz resuelta.
/// Esto evita dependencias externas (MediatR) manteniendo el mismo patrón de despacho.
/// </remarks>
public sealed class Mediator : IMediator
{
    private readonly IServiceProvider _serviceProvider;

    public Mediator(IServiceProvider serviceProvider)
        => _serviceProvider = serviceProvider;

    /// <inheritdoc/>
    public Task<TResponse> SendAsync<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        // Construye IRequestHandler<TConcreteRequest, TResponse> en tiempo de ejecución.
        var handlerType = typeof(IRequestHandler<,>)
            .MakeGenericType(request.GetType(), typeof(TResponse));

        // Lanza InvalidOperationException si no hay un handler registrado — fallo rápido.
        var handler = _serviceProvider.GetRequiredService(handlerType);

        // Reflexión puntual: invoca HandleAsync en la interfaz concreta resuelta.
        var handleMethod = handlerType.GetMethod(nameof(IRequestHandler<IRequest<TResponse>, TResponse>.HandleAsync))!;
        return (Task<TResponse>)handleMethod.Invoke(handler, [request, cancellationToken])!;
    }
}
