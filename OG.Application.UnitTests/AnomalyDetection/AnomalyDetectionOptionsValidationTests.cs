using System;
using System.Collections.Generic;
using Application.Common.RiskEngine.AnomalyDetection;
using Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace OG.Application.UnitTests.AnomalyDetection;

/// <summary>
/// Verifica que la sección <c>AnomalyDetection</c> de configuración se ENLAZA de verdad y que sus
/// invariantes se validan al arrancar. Antes, <c>AddOptions&lt;AnomalyDetectionOptions&gt;()</c> se
/// registraba sin <c>Bind</c>: los valores quedaban clavados a los defaults del código y calibrar el
/// motor desde appsettings (necesario para HU-035) no tenía ningún efecto, en silencio.
/// </summary>
public class AnomalyDetectionOptionsValidationTests
{
    private static ServiceProvider Build(params (string Key, string Value)[] settings)
    {
        var dict = new Dictionary<string, string?>();
        foreach (var (key, value) in settings)
            dict[key] = value;

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddInfrastructure();

        return services.BuildServiceProvider();
    }

    [Fact]
    public void SeccionDeConfiguracion_SeEnlaza_NoSeQuedaEnLosDefaults()
    {
        using var sp = Build(
            ("AnomalyDetection:MinTrainingSamples", "37"),
            ("AnomalyDetection:FrequencyWindowMinutes", "15"));

        var options = sp.GetRequiredService<IOptions<AnomalyDetectionOptions>>().Value;

        Assert.Equal(37, options.MinTrainingSamples);
        Assert.Equal(15, options.FrequencyWindowMinutes);
    }

    [Fact]
    public void SinConfiguracion_UsaLosDefaultsDelCodigo()
    {
        using var sp = Build();

        var options = sp.GetRequiredService<IOptions<AnomalyDetectionOptions>>().Value;

        Assert.Equal(20, options.MinTrainingSamples);
        Assert.Equal(3, options.PcaRank);
    }

    /// <summary>
    /// El rango del PCA debe ser menor que la dimensión del vector de features. Con un valor
    /// inválido el arranque tiene que ABORTAR, no degradar en silencio a un modelo mal formado.
    /// </summary>
    [Theory]
    [InlineData("4")]   // == Dimension
    [InlineData("9")]   // > Dimension
    [InlineData("0")]   // sin componentes
    [InlineData("-1")]
    public void PcaRankInvalido_AbortaElArranque(string pcaRank)
    {
        using var sp = Build(("AnomalyDetection:PcaRank", pcaRank));
        var validator = sp.GetRequiredService<IStartupValidator>();

        var ex = Assert.Throws<OptionsValidationException>(() => validator.Validate());

        Assert.Contains("PcaRank", string.Join(" ", ex.Failures), StringComparison.Ordinal);
    }

    [Fact]
    public void TrainingWindowMenorQueElMinimo_AbortaElArranque()
    {
        using var sp = Build(
            ("AnomalyDetection:MinTrainingSamples", "50"),
            ("AnomalyDetection:TrainingWindowMax", "10"));

        var validator = sp.GetRequiredService<IStartupValidator>();

        var ex = Assert.Throws<OptionsValidationException>(() => validator.Validate());
        Assert.Contains("TrainingWindowMax", string.Join(" ", ex.Failures), StringComparison.Ordinal);
    }

    [Fact]
    public void ConfiguracionValida_ArrancaSinErrores()
    {
        using var sp = Build(
            ("AnomalyDetection:PcaRank", "2"),
            ("AnomalyDetection:MinTrainingSamples", "25"),
            ("AnomalyDetection:TrainingWindowMax", "300"));

        var validator = sp.GetRequiredService<IStartupValidator>();

        validator.Validate();   // no debe lanzar
    }
}
