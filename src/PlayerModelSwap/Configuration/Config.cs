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

    [DisplayName("Temporary-Human Hotkey")]
    [Description("While a Digimon is selected, tap this key to briefly revert to the human model in place, " +
                "then tap again to return to the Digimon. Q Analyze is supported directly while transformed; " +
                "this remains available as a fallback for other human-only interactions or troubleshooting.\n" +
                "'None' disables the hotkey. The swap stays save-safe either way.")]
    [DefaultValue(ToggleHotkey.None)]
    public ToggleHotkey TemporaryHumanKey { get; set; } = ToggleHotkey.None;

    [DisplayName("Allow Digimon Ride")]
    [Description("Let the player start a Digimon ride while rendered as a Digimon (instead of blocking it). " +
                "The mount rig assumes a human player, so riding as some Digimon may misalign the mount or " +
                "crash — if a ride crashes, turn this OFF to restore the crash guard. Human/None ride normally " +
                "either way. Save-safe.")]
    [DefaultValue(true)]
    public bool AllowDigimonRide { get; set; } = true;
}

/// <summary>Optional hotkey to temporarily revert to the human model. Values are Win32 virtual-key codes.</summary>
public enum ToggleHotkey
{
    None = 0,
    F1 = 0x70, F2 = 0x71, F3 = 0x72, F4 = 0x73,
    F5 = 0x74, F6 = 0x75, F7 = 0x76, F8 = 0x77,
    F9 = 0x78, F10 = 0x79, F11 = 0x7A, F12 = 0x7B,
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
