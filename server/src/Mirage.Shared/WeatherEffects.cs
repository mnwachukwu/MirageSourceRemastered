namespace Mirage.Shared;

/// <summary>Small shared predicates describing which weathers gate which effects, so the server's
/// effect sites stay in lockstep. Pure — reads only <see cref="Constants"/> and <see cref="WeatherType"/>.</summary>
public static class WeatherEffects
{
    /// <summary>Heat Wave and Snow both halve vital regen.</summary>
    public static bool ReducesRegen(WeatherType weather) =>
        weather is WeatherType.HeatWave or WeatherType.Snow;

    /// <summary>The vital-regen magnitude multiplier for the given weather (0.5 for Heat Wave / Snow,
    /// else 1.0). Fold into the regen formula BEFORE its round/floor.</summary>
    public static double RegenMultiplier(WeatherType weather) =>
        ReducesRegen(weather) ? Constants.WeatherReducedRegenMultiplier : 1.0;
}
