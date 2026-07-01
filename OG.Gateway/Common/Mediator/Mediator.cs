using Microsoft.Extensions.DependencyInjection;

namespace Application.Common.Mediator;

public sealed class Mediator : IMediator
{
    private readonly IServiceProvider _serviceProvider;

    public Mediator(IServiceProvider serviceProvider)
        => _serviceProvider = serviceProvider;

    public Task<TResponse> SendAsync<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        // Construye IRequestHandler<TConcreteRequest, TResponse> en tiempo de ejecución
        var handlerType = typeof(IRequestHandler<,>)
            .MakeGenericType(request.GetType(), typeof(TResponse));

        // Lanza InvalidOperationException si no hay un handler registrado
        var handler = _serviceProvider.GetRequiredService(handlerType);

        // invoca HandleAsync en la interfaz concreta resuelta.
        var handleMethod = handlerType.GetMethod(nameof(IRequestHandler<IRequest<TResponse>, TResponse>.HandleAsync))!;
        return (Task<TResponse>)handleMethod.Invoke(handler, [request, cancellationToken])!;
    }
}
