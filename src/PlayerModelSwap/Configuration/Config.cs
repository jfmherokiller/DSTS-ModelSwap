using System;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using TimeStranger.PlayerModelSwap.Template.Configuration;

namespace TimeStranger.PlayerModelSwap.Configuration;

public class Config : Configurable<Config>
{
    [DisplayName("Player Digimon")]
    [Description("The Digimon the field player character is swapped to. 'None' = no swap.\n" +
                "SAVE-SAFE: this hook overrides the model at render time only and never writes the " +
                "in-game change-model flag, so it is safe to change or disable at any time.")]
    [DefaultValue(PlayerDigimon.None)]
    [JsonConverter(typeof(TolerantPlayerDigimonConverter))]
    public PlayerDigimon PlayerDigimon { get; set; } = PlayerDigimon.None;
}

/// <summary>
/// Deserializes <see cref="PlayerDigimon"/> tolerantly: an unknown/renamed value (which happens when
/// the compatibility-tiered enum is regenerated) falls back to <see cref="PlayerDigimon.None"/> instead
/// of throwing and breaking the config UI.
/// </summary>
public class TolerantPlayerDigimonConverter : JsonConverter<PlayerDigimon>
{
    public override PlayerDigimon Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        try
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                var s = reader.GetString();
                if (s != null && Enum.TryParse<PlayerDigimon>(s, ignoreCase: false, out var v))
                    return v;
            }
            else if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var i)
                     && Enum.IsDefined(typeof(PlayerDigimon), i))
            {
                return (PlayerDigimon)i;
            }
        }
        catch { /* ignore and fall back */ }

        return PlayerDigimon.None;
    }

    public override void Write(Utf8JsonWriter writer, PlayerDigimon value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString());
}

/// <summary>
/// Allows overriding aspects of configuration creation. Left default.
/// </summary>
public class ConfiguratorMixin : ConfiguratorMixinBase
{
}
