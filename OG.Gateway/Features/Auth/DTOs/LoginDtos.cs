namespace Application.Features.Auth.DTOs;

/// <summary>Cuerpo del POST /auth/login.</summary>
public sealed record LoginRequest(string Username, string Password);

/// <summary>Respuesta exitosa de login (HTTP 200).</summary>
public sealed record LoginResponse(string AccessToken);

/// <summary>Respuesta de error cuando la cuenta está bloqueada (HTTP 423).</summary>
public sealed record AccountLockedResponse(string Message, int SecondsRemaining);
