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
        var handlerType = typeof(IRequestHandler<,>)
            .MakeGenericType(request.GetType(), typeof(TResponse));

        var handler = _serviceProvider.GetRequiredService(handlerType);

        var handleMethod = handlerType.GetMethod(nameof(IRequestHandler<IRequest<TResponse>, TResponse>.HandleAsync))!;
        return (Task<TResponse>)handleMethod.Invoke(handler, [request, cancellationToken])!;
    }
}
