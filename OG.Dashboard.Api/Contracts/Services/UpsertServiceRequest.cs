using System.ComponentModel.DataAnnotations;

namespace OG.Dashboard.Api.Contracts.Services;

public sealed record UpsertServiceRequest(
    /// <summary>Nombre lógico único, usado como clusterId en YARP.</summary>
    [Required, MinLength(2), MaxLength(100)]
    string Name,

    [Required, Url]
    string UpstreamUrl,

    bool RequiresAuth,
    bool IsActive = true
);
