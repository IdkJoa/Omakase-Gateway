using Application.Common.RiskEngine.Rules;

namespace Application.Common.RiskEngine.Scoring;

/// <summary>
/// Max-aggregation Policy Score calculator (T-028, revisado).
/// El Policy Score es la <b>peor violación ponderada</b> entre las reglas evaluadas:
/// <c>max(Score × Weight)</c>. Cada regla determinista devuelve 0 (coherente) o una
/// severidad; el peso [0,1] acota cuánto puede aportar una regla (una regla dura como
/// Viaje Imposible se pesa 1.0; una advisory, menos).
/// <para>
/// Decisión de diseño (Zero Trust): NO se promedia. El promedio ponderado <b>diluía</b>
/// una violación grave con las reglas que no dispararon (p. ej. con 3 políticas, una
/// violación de 50 caía a 16.67), debilitando la detección justo al añadir más cobertura.
/// Peor aún, un ataque diluido podía caer bajo el umbral → Allow → alimentar el perfil de
/// comportamiento (solo los Allow lo hacen, HU-017) y <b>envenenar el modelo de anomalías</b>,
/// enseñándole que el patrón malicioso es normal. Con agregación por máximo, la violación
/// más fuerte manda: no se diluye la detección ni se contamina el entrenamiento. Escala
/// 0–100 preservada (Score∈[0,100], Weight∈[0,1]).
/// </para>
/// </summary>
public sealed class PolicyScoreCalculator : IPolicyScoreCalculator
{
    public decimal Calculate(IReadOnlyList<RuleEvaluationResult> ruleResults)
    {
        if (ruleResults is null || ruleResults.Count == 0)
            return 0m;

        decimal max = 0m;
        foreach (var rule in ruleResults)
        {
            var contribution = rule.Score * rule.Weight;
            if (contribution > max)
                max = contribution;
        }

        return Math.Clamp(max, 0m, 100m);
    }
}
