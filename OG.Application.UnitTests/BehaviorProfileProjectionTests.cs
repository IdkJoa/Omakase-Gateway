using System;
using System.Collections.Generic;
using Application.Common.RiskEngine.AnomalyDetection;
using Xunit;

namespace OG.Application.UnitTests;

// HU-026 T-054: proyección del feature_vector a un resumen visualizable, respetando la continuidad cíclica seno/coseno de la hora.
public class BehaviorProfileProjectionTests
{
    /// <summary>Codifica una hora igual que FeatureExtractor: (sin/cos + 1) / 2, en [0,1].</summary>
    private static AnomalyFeatureVector AtHour(double hour, float freq = 0.5f, float div = 0.5f)
    {
        var rad = 2.0 * Math.PI * hour / 24.0;
        return new AnomalyFeatureVector(
            (float)((Math.Sin(rad) + 1.0) / 2.0),
            (float)((Math.Cos(rad) + 1.0) / 2.0),
            freq, div);
    }

    private static void AssertHour(double expected, double actual, double tol = 0.05) =>
        Assert.True(Math.Abs(expected - actual) < tol,
            $"Hora esperada ≈{expected}, obtenida {actual}");

    [Fact]
    public void VentanaVacia_DevuelveNull_ParaColdStart()
    {
        Assert.Null(BehaviorProfileProjection.Summarize(Array.Empty<AnomalyFeatureVector>()));
        Assert.Null(BehaviorProfileProjection.Summarize(new List<AnomalyFeatureVector>()));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(6.0)]
    [InlineData(12.0)]
    [InlineData(18.0)]
    [InlineData(23.0)]
    public void HoraHabitual_SeDecodificaCorrecta(double hour)
    {
        var result = BehaviorProfileProjection.Summarize(new[] { AtHour(hour) });

        Assert.NotNull(result);
        // hora 0 puede decodificar como ~0 o ~24 (mismo punto del círculo).
        var got = result!.Value.TypicalHour;
        var wrapped = hour == 0.0 && got > 23.0 ? got - 24.0 : got;
        AssertHour(hour, wrapped);
    }

    [Fact]
    public void MediaCircular_23y1_PromedianAMedianoche_NoAlMediodia()
    {
        var result = BehaviorProfileProjection.Summarize(new[] { AtHour(23.0), AtHour(1.0) });

        Assert.NotNull(result);
        var got = result!.Value.TypicalHour;
        var wrapped = got > 23.0 ? got - 24.0 : got; // ~0 o ~24 -> medianoche
        AssertHour(0.0, wrapped);
    }

    [Fact]
    public void PromediaFrecuenciaYDiversidad()
    {
        var result = BehaviorProfileProjection.Summarize(new[]
        {
            AtHour(12.0, freq: 0.2f, div: 0.4f),
            AtHour(12.0, freq: 0.8f, div: 0.6f),
        });

        Assert.NotNull(result);
        Assert.Equal(0.5f, result!.Value.Frequency, 3);
        Assert.Equal(0.5f, result.Value.Diversity, 3);
        AssertHour(12.0, result.Value.TypicalHour);
    }
}
