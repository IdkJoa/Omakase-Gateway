using System.Text.Json;
using Domain.Entities;

namespace Application.Common.RiskEngine.Rules.Validation;

// Valida config ANTES de persistir: los evaluadores tratan config malformada como violación
// severa fail-closed, así que una config inválida almacenada convertiría la política en un
// bloqueo permanente silencioso. Tipos sin validador registrado (Fingerprint, ImpossibleTravel)
// no leen config y aceptan cualquier objeto JSON.
public interface IPolicyConfigValidator
{
    PolicyType Type { get; }

    // Debe mantenerse en espejo con lo que parsea el evaluador correspondiente.
    IReadOnlyList<string> Validate(JsonDocument config);
}
