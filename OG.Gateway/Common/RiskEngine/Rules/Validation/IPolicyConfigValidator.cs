using System.Text.Json;
using Domain.Entities;

namespace Application.Common.RiskEngine.Rules.Validation;

/// <summary>
/// Valida el JSONB <c>config</c> de una <see cref="AccessPolicy"/> ANTES de persistirla
/// (HU-023 T-047): el CRUD administrativo rechaza en el POST/PUT lo que el evaluador
/// del motor no sabría interpretar después (los evaluadores tratan la config malformada
/// como violación severa fail-closed, así que una config inválida almacenada convertiría
/// la política en un bloqueo permanente silencioso).
/// <para>
/// Seam Open/Closed: cada tipo de regla aporta su validador vía DI; los tipos sin
/// validador registrado (Fingerprint, ImpossibleTravel) no leen config y aceptan
/// cualquier objeto JSON. Una regla nueva mañana = un validador nuevo, cero cambios
/// en el controller.
/// </para>
/// </summary>
public interface IPolicyConfigValidator
{
    /// <summary>Tipo de regla cuya config valida este componente.</summary>
    PolicyType Type { get; }

    /// <summary>
    /// Devuelve la lista de errores de la config (vacía si es válida).
    /// Debe mantenerse en espejo con lo que parsea el evaluador correspondiente.
    /// </summary>
    IReadOnlyList<string> Validate(JsonDocument config);
}
