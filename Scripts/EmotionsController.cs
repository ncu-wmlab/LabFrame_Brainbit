using NeuroSDK;
using SignalMath;
using System;
using System.Linq;

public class EmotionsController
{
    private readonly EegEmotionalMath _math;

    public Action<int> progressCalibrationCallback = null;
    public Action<bool> isArtefactedSequenceCallback = null;
    public Action<bool> isBothSidesArtifactedCallback = null;
    public Action<SpectralDataPercents> lastSpectralDataCallback = null;
    public Action<RawSpectVals> rawSpectralDataCallback = null;
    public Action<MindData> lastMindDataCallback = null;

    private bool isCalibrated = false;

    public EmotionsController(BrainBitConfig labConfig = null)
    {
        var config = EmotionalMathConfig.GetDefault(true, labConfig);

        _math = new EegEmotionalMath(
            config.MathLib,
            config.ArtifactDetect,
            config.ShortArtifactDetect,
            config.MentalAndSpectral);

        _math?.SetZeroSpectWaves(config.Active, config.delta, config.theta, config.alpha, config.beta, config.gamma);
        _math?.SetWeightsForSpectra(config.delta_c, config.theta_c, config.alpha_c, config.beta_c, config.gamma_c);
        _math?.SetCallibrationLength(config.CallibrationLength);
        _math?.SetMentalEstimationMode(config.MentalEstimation);
        _math?.SetPrioritySide(config.PrioritySide);
        _math?.SetSkipWinsAfterArtifact(config.SkipWinsAfterArtifact);
        _math?.SetSpectNormalizationByBandsWidth(config.SpectNormalizationByBandsWidth);
        _math?.SetSpectNormalizationByCoeffs(config.SpectNormalizationByCoeffs);
    }

    public void Dispose() { _math.Dispose(); }

    public void StartCalibration()
    {
        isCalibrated = false;
        _math.StartCalibration();
    }

    public void ProcessData(BrainBitSignalData[] samples)
    {
        var bipolarSamples = new RawChannels[samples.Length];

        for (var i = 0; i < samples.Length; i++)
        {
            bipolarSamples[i].LeftBipolar = samples[i].T3 - samples[i].O1;
            bipolarSamples[i].RightBipolar = samples[i].T4 - samples[i].O2;
        }

        try
        {
            _math.PushData(bipolarSamples);
            _math.ProcessDataArr();

            resolveArtefacted();

            if (!isCalibrated)
            {
                processCalibration();
            }
            else
            {
                resolveSpectralData();
                resolveRawSpectralData();
                resolveMindData();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }
    }

    private void resolveArtefacted()
    {
        // sequence artifacts
        bool isArtifactedSequence = _math.IsArtifactedSequence();
        isArtefactedSequenceCallback?.Invoke(isArtifactedSequence);

        // both sides artifacts
        bool isBothSideArtifacted = _math.IsBothSidesArtifacted();
        isBothSidesArtifactedCallback?.Invoke(isBothSideArtifacted);
    }

    private void processCalibration()
    {
        bool wasCalibrated = isCalibrated;
        isCalibrated = _math.CalibrationFinished();

        if (!isCalibrated)
        {
            int progress = _math.GetCallibrationPercents();
            progressCalibrationCallback?.Invoke(progress);
        }
        else if (!wasCalibrated)
        {
            // Transition: just finished calibrating — emit 100% once
            progressCalibrationCallback?.Invoke(100);
        }
    }

    private void resolveSpectralData()
    {
        var spectralValues = _math?.ReadSpectralDataPercentsArr();
        if (spectralValues.Length > 0)
        {
            var spectralVal = spectralValues.Last();
            //if(spectralVal.Delta > 0)
            lastSpectralDataCallback?.Invoke(spectralValues.Last());
        }
    }
    private void resolveRawSpectralData()
    {
        var rawSpectralValues = _math.ReadRawSpectralVals();
        rawSpectralDataCallback?.Invoke(rawSpectralValues);
    }
    private void resolveMindData()
    {
        var mentalValues = _math.ReadMentalDataArr();
        if (mentalValues.Length > 0)
        {
            lastMindDataCallback?.Invoke(mentalValues.Last());
        }
    }

}
