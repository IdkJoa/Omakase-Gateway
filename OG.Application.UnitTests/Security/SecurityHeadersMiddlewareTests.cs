using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Infrastructure.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Xunit;

namespace OG.Application.UnitTests.Security;

/// <summary>
/// Pruebas del middleware de cabeceras de seguridad (HU-029 / T-061, de Joel): confirma que TODAS
/// las respuestas llevan las 5 cabeceras exigidas por el criterio de aceptación. Las cabeceras se
/// agregan en <c>Response.OnStarting</c>, así que el test las dispara con un feature de respuesta
/// propio (el feature por defecto de <see cref="DefaultHttpContext"/> descarta esos callbacks).
/// </summary>
public class SecurityHeadersMiddlewareTests
{
    [Theory]
    [InlineData("Content-Security-Policy", "default-src 'self'; script-src 'self'")]
    [InlineData("X-Frame-Options", "DENY")]
    [InlineData("X-Content-Type-Options", "nosniff")]
    [InlineData("Strict-Transport-Security", "max-age=31536000; includeSubDomains")]
    [InlineData("Referrer-Policy", "strict-origin-when-cross-origin")]
    public async Task Adds_RequiredSecurityHeader(string header, string expected)
    {
        var context = new DefaultHttpContext();
        var feature = new StartTrackingResponseFeature();
        context.Features.Set<IHttpResponseFeature>(feature);

        await new SecurityHeadersMiddleware(_ => Task.CompletedTask).InvokeAsync(context);
        await feature.FireOnStartingAsync();   // simula el inicio de la respuesta

        Assert.Equal(expected, context.Response.Headers[header].ToString());
    }

    [Fact]
    public async Task DoesNotOverride_ExistingHeader()
    {
        var context = new DefaultHttpContext();
        var feature = new StartTrackingResponseFeature();
        context.Features.Set<IHttpResponseFeature>(feature);
        context.Response.Headers["X-Frame-Options"] = "SAMEORIGIN"; // ya presente aguas arriba

        await new SecurityHeadersMiddleware(_ => Task.CompletedTask).InvokeAsync(context);
        await feature.FireOnStartingAsync();

        Assert.Equal("SAMEORIGIN", context.Response.Headers["X-Frame-Options"].ToString());
    }

    /// <summary>Feature de respuesta que captura los callbacks OnStarting para dispararlos manualmente.</summary>
    private sealed class StartTrackingResponseFeature : IHttpResponseFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> _onStarting = new();

        public int StatusCode { get; set; } = 200;
        public string? ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = Stream.Null;
        public bool HasStarted { get; private set; }

        public void OnStarting(Func<object, Task> callback, object state) => _onStarting.Add((callback, state));
        public void OnCompleted(Func<object, Task> callback, object state) { }

        public async Task FireOnStartingAsync()
        {
            HasStarted = true;
            foreach (var (callback, state) in _onStarting)
                await callback(state);
        }
    }
}
