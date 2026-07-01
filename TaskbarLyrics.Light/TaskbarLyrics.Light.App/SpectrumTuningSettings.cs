namespace TaskbarLyrics.Light.App;

public sealed class SpectrumTuningSettings
{
    public int SampleWindow { get; set; } = 2048;
    public int UpdateIntervalMs { get; set; } = 16;
    public double MinFrequency { get; set; } = 35;
    public double MaxFrequency { get; set; } = 7000;
    public double PeakInitial { get; set; } = 0.035;
    public double PeakDecay { get; set; } = 0.85;
    public double PeakFloor { get; set; } = 0.012;
    public double PeakCeiling { get; set; } = 0.42;
    public double NoiseFloor { get; set; } = 0.035;
    public double OutputCurve { get; set; } = 1.00;
    public double LowBandGain { get; set; } = 1.6;
    public double BandGainStep { get; set; } = 0.01;
    public double FrequencyWeightBase { get; set; } = 0.92;
    public double FrequencyWeightSlope { get; set; } = 0.01;
    public double BackendAttack { get; set; } = 0.86;
    public double BackendRelease { get; set; } = 0.48;
    public double FrontendRise { get; set; } = 0.70;
    public double FrontendFall { get; set; } = 0.35;
    public double MinBarHeight { get; set; } = 4;
    public double BarHeightRange { get; set; } = 24;
    public double BarOpacity { get; set; } = 0.78;

    public static SpectrumTuningSettings CreateDefault() => new();

    public SpectrumTuningSettings Clone()
    {
        return new SpectrumTuningSettings
        {
            SampleWindow = SampleWindow,
            UpdateIntervalMs = UpdateIntervalMs,
            MinFrequency = MinFrequency,
            MaxFrequency = MaxFrequency,
            PeakInitial = PeakInitial,
            PeakDecay = PeakDecay,
            PeakFloor = PeakFloor,
            PeakCeiling = PeakCeiling,
            NoiseFloor = NoiseFloor,
            OutputCurve = OutputCurve,
            LowBandGain = LowBandGain,
            BandGainStep = BandGainStep,
            FrequencyWeightBase = FrequencyWeightBase,
            FrequencyWeightSlope = FrequencyWeightSlope,
            BackendAttack = BackendAttack,
            BackendRelease = BackendRelease,
            FrontendRise = FrontendRise,
            FrontendFall = FrontendFall,
            MinBarHeight = MinBarHeight,
            BarHeightRange = BarHeightRange,
            BarOpacity = BarOpacity
        };
    }
}
