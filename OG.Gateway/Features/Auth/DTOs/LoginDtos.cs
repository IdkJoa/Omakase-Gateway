namespace Application.Features.Auth.DTOs;

public sealed record LoginRequest(string Username, string Password);

public sealed record LoginResponse(string AccessToken);

/// <summary>Cuerpo de respuesta HTTP 423 (cuenta bloqueada).</summary>
public sealed record AccountLockedResponse(string Message, int SecondsRemaining);
