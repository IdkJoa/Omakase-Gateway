using Application.Common.RiskEngine.Rules;

namespace Application.Common.RiskEngine.Scoring;

// Policy Score = max(Score x Weight) entre las reglas evaluadas, NO un promedio ponderado.
// El promedio diluía una violación grave con las reglas que no dispararon (con 3 políticas, una
// violación de 50 caía a 16.67), y un ataque diluido podía caer bajo el umbral -> Allow ->
// alimentar el perfil de comportamiento y envenenar el modelo de anomalías. Con máximo, la
// violación más fuerte manda.
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
