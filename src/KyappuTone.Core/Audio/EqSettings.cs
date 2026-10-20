using System.Globalization;
using System.Text;

namespace KyappuTone.Core.Audio;

/// <summary>
/// Serializable equalizer state: enable flag, preamp, ten band gains and the
/// name of the last applied preset. Kept UI-agnostic so it can be persisted by
/// the app and verified by Core tests.
/// </summary>
public sealed class EqSettings
{
    /// <summary>Number of bands in the equalizer.</summary>
    public const int BandCount = 10;

    private const string FormatVersion = "v1";
    private readonly float[] _bandGainsDb = new float[BandCount];
    private float _preampDb;

    /// <summary>Whether the equalizer is active. Disabled means a flat bypass.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Name of the active preset, or "Custom" after manual edits.</summary>
    public string ActivePreset { get; private set; } = "Flat";

    /// <summary>Preamp gain in dB, clamped to ±12 dB.</summary>
    public float PreampDb
    {
        get => _preampDb;
        set => _preampDb = Math.Clamp(value, -12f, 12f);
    }

    /// <summary>Copy of the ten band gains in dB.</summary>
    public IReadOnlyList<float> BandGainsDb => (float[])_bandGainsDb.Clone();

    /// <summary>True when the preamp and every band are at 0 dB.</summary>
    public bool IsFlat => PreampDb == 0f && Array.TrueForAll(_bandGainsDb, gain => gain == 0f);

    /// <summary>All preset names in menu order, ending with "Custom".</summary>
    public static IReadOnlyList<string> PresetNames { get; } =
        EqPreset.All.Select(preset => preset.Name).ToArray();

    /// <summary>Sets one band gain in dB and marks the state as custom.</summary>
    public void SetBandGain(int index, float gainDb)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, BandCount);

        _bandGainsDb[index] = Math.Clamp(gainDb, -12f, 12f);
        ActivePreset = "Custom";
    }

    /// <summary>Sets the preamp gain in dB.</summary>
    public void SetPreamp(float preampDb) => PreampDb = Math.Clamp(preampDb, -12f, 12f);

    /// <summary>Copies the gains of a named preset. Unknown names are ignored.</summary>
    public void LoadPreset(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;

        var preset = EqPreset.All.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));

        if (preset.Gains is null) return;

        Array.Copy(preset.Gains, _bandGainsDb, BandCount);
        ActivePreset = preset.Name;
    }

    /// <summary>Applies the state to an equalizer. Disabled or flat means bypass.</summary>
    public void ApplyTo(TenBandEqualizer equalizer)
    {
        ArgumentNullException.ThrowIfNull(equalizer);

        equalizer.PreampDb = Enabled ? PreampDb : 0f;
        equalizer.ApplyPreset(Enabled ? _bandGainsDb : new float[BandCount]);
    }

    /// <summary>Serializes to a compact, invariant string.</summary>
    public string Serialize()
    {
        var builder = new StringBuilder();
        builder.Append(FormatVersion);
        builder.Append(";enabled=").Append(Enabled ? '1' : '0');
        builder.Append(";preset=").Append(ActivePreset.Replace(';', ' ').Replace('=', ' '));
        builder.Append(";preamp=").Append(PreampDb.ToString("R", CultureInfo.InvariantCulture));
        builder.Append(";bands=");

        for (var index = 0; index < BandCount; index++)
        {
            if (index > 0) builder.Append(',');
            builder.Append(_bandGainsDb[index].ToString("R", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    /// <summary>Parses a serialized payload, falling back to flat defaults.</summary>
    public static EqSettings Parse(string? serialized)
    {
        var settings = new EqSettings();
        if (string.IsNullOrWhiteSpace(serialized)) return settings;

        var parts = serialized.Split(';', StringSplitOptions.RemoveEmptyEntries);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var part in parts)
        {
            var separator = part.IndexOf('=');
            if (separator <= 0) continue;
            values[part[..separator]] = part[(separator + 1)..];
        }

        if (values.TryGetValue("enabled", out var enabled))
        {
            settings.Enabled = enabled != "0";
        }

        if (values.TryGetValue("preamp", out var preamp) &&
            float.TryParse(preamp, NumberStyles.Float, CultureInfo.InvariantCulture, out var preampDb))
        {
            settings.SetPreamp(preampDb);
        }

        float[]? parsedBands = null;
        if (values.TryGetValue("bands", out var bands))
        {
            var gains = bands.Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (gains.Length == BandCount)
            {
                var parsed = new float[BandCount];
                var valid = true;

                for (var index = 0; index < BandCount; index++)
                {
                    if (!float.TryParse(gains[index], NumberStyles.Float, CultureInfo.InvariantCulture, out parsed[index]))
                    {
                        valid = false;
                        break;
                    }
                }

                if (valid) parsedBands = parsed;
            }
        }

        var presetName = values.TryGetValue("preset", out var storedPreset) ? storedPreset : null;
        if (!string.IsNullOrWhiteSpace(presetName)) settings.LoadPreset(presetName);

        if (parsedBands is not null)
        {
            for (var index = 0; index < BandCount; index++) settings.SetBandGain(index, parsedBands[index]);

            // SetBandGain marks the state custom. Restore the preset name when the
            // persisted bands are identical to that preset.
            if (presetName is not null &&
                !string.Equals(presetName, "Custom", StringComparison.OrdinalIgnoreCase))
            {
                var preset = EqPreset.All.FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, presetName, StringComparison.OrdinalIgnoreCase));

                if (preset.Gains is not null && parsedBands.SequenceEqual(preset.Gains))
                {
                    settings.ActivePreset = preset.Name;
                }
            }
        }

        return settings;
    }
}
