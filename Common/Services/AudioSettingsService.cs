using KyappuTone.Core.Audio;

namespace KyappuTone.Common.Services;

/// <summary>
/// Loads, persists and broadcasts equalizer settings. The UI edits this service;
/// the audio backend applies the state to its engine on every change.
/// </summary>
public sealed class AudioSettingsService
{
    private static readonly Lazy<AudioSettingsService> LazyInstance = new(() => new AudioSettingsService());

    private readonly EqSettings _settings;

    private AudioSettingsService()
    {
        var stored = Windows.Storage.ApplicationData.Current.LocalSettings
            .Values[nameof(LocalSave.EqualizerSettings)]?.ToString();
        _settings = EqSettings.Parse(stored);
    }

    /// <summary>Shared instance bound to the app's local settings.</summary>
    public static AudioSettingsService Instance => LazyInstance.Value;

    /// <summary>Raised after any setting is persisted.</summary>
    public event EventHandler? Changed;

    /// <summary>Whether the equalizer is active.</summary>
    public bool Enabled
    {
        get => _settings.Enabled;
        set
        {
            if (_settings.Enabled == value) return;
            _settings.Enabled = value;
            Save();
        }
    }

    /// <summary>Name of the active preset, or "Custom".</summary>
    public string ActivePreset => _settings.ActivePreset;

    /// <summary>Preamp gain in dB.</summary>
    public float PreampDb
    {
        get => _settings.PreampDb;
        set
        {
            if (_settings.PreampDb == value) return;
            _settings.PreampDb = value;
            Save();
        }
    }

    /// <summary>Copy of the ten band gains in dB.</summary>
    public IReadOnlyList<float> BandGainsDb => _settings.BandGainsDb;

    /// <summary>All preset names in menu order.</summary>
    public static IReadOnlyList<string> PresetNames => EqSettings.PresetNames;

    /// <summary>Applies a named preset and persists the result.</summary>
    public void LoadPreset(string name)
    {
        _settings.LoadPreset(name);
        Save();
    }

    /// <summary>Sets one band gain and persists the result.</summary>
    public void SetBandGain(int index, float gainDb)
    {
        _settings.SetBandGain(index, gainDb);
        Save();
    }

    /// <summary>Copies the current state into an equalizer.</summary>
    public void ApplyTo(TenBandEqualizer equalizer) => _settings.ApplyTo(equalizer);

    private void Save()
    {
        Windows.Storage.ApplicationData.Current.LocalSettings
            .Values[nameof(LocalSave.EqualizerSettings)] = _settings.Serialize();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
