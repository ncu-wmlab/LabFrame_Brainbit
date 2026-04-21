using SignalMath;


public class EmotionalMathConfig
{
    public static EmotionalMathConfig GetDefault(bool isBipolar, BrainBitConfig labConfig = null)
    {
        int samplingFrequencyHz = 250;

        var mathLib = new MathLibSetting
        {
            sampling_rate = (uint)samplingFrequencyHz,
            process_win_freq = 25,
            fft_window = (uint)samplingFrequencyHz * 2,
            n_first_sec_skipped = 4,
            bipolar_mode = isBipolar,
            squared_spectrum = true,
            channels_number = (uint)1,
            channel_for_analysis = 0
        };

        var artsDetect = new ArtifactDetectSetting
        {
            art_bord = 110,
            allowed_percent_artpoints = 70,
            raw_betap_limit = 800_000,
            total_pow_border = (uint)(8 * 1e7),
            global_artwin_sec = 4,
            spect_art_by_totalp = false,
            hanning_win_spectrum = false,
            hamming_win_spectrum = true,
            num_wins_for_quality_avg = 100
        };

        var shortArtsDetect = new ShortArtifactDetectSetting
        {
            ampl_art_detect_win_size = 200,
            ampl_art_zerod_area = 200,
            ampl_art_extremum_border = 25
        };

        var mentalAndSpectralSettings = new MentalAndSpectralSetting
        {
            n_sec_for_instant_estimation = 2,
            n_sec_for_averaging = 4
        };

        var emConfigs = new EmotionalMathConfig(samplingFrequencyHz, mathLib, artsDetect, shortArtsDetect, mentalAndSpectralSettings);

        if (labConfig != null)
        {
            emConfigs.CallibrationLength   = labConfig.EmotionsCalibrationLength;
            emConfigs.MentalEstimation     = labConfig.EmotionsMentalEstimation;
            emConfigs.PrioritySide         = labConfig.EmotionsPrioritySide;
        }

        return emConfigs;
    }

    public int SamplingFrequencyHz
    {
        get; set;
    }

    public MathLibSetting MathLib;

    public ArtifactDetectSetting ArtifactDetect;

    public ShortArtifactDetectSetting ShortArtifactDetect;

    public MentalAndSpectralSetting MentalAndSpectral;

    public bool MentalEstimation { get; set; } = false;

    public SideType PrioritySide { get; set; } = SideType.NONE;

    public int CallibrationLength { get; set; } = 6;

    public int SkipWinsAfterArtifact { get; set; } = 5;

    // ZeroSpectWaves
    public bool Active { get; set; } = true;
    public int alpha { get; set; } = 1;
    public int beta { get; set; } = 1;
    public int theta { get; set; } = 1;
    public int delta { get; set; } = 0;
    public int gamma { get; set; } = 0;

    // WeightsForSpectra
    public double delta_c { get; set; } = 1;
    public double theta_c { get; set; } = 1;
    public double alpha_c { get; set; } = 1;
    public double beta_c { get; set; } = 1;
    public double gamma_c { get; set; } = 1;

    public bool SpectNormalizationByBandsWidth { get; set; }

    public bool SpectNormalizationByCoeffs { get; set; }

    public EmotionalMathConfig(
        int samplingFrequencyHz,
        MathLibSetting mathLib,
        ArtifactDetectSetting artifactDetect,
        ShortArtifactDetectSetting shortArtifactDetect,
        MentalAndSpectralSetting mentalAndSpectral
        )
    {
        SamplingFrequencyHz = samplingFrequencyHz;
        MathLib = mathLib;
        ArtifactDetect = artifactDetect;
        ShortArtifactDetect = shortArtifactDetect;
        MentalAndSpectral = mentalAndSpectral;
    }

}
