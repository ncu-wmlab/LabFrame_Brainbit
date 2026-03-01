using System;
using System.Collections.Generic;
using System.Linq;

namespace NeuroSDK
{
    public static class SensorInfoProvider
    {
        public static Dictionary<string, string> GetBrainBitSensorParameters(BrainBitSensor sensor)
        {
            var dictionary = GetGeneralSensorParameter(sensor);
            var parameters = sensor.Parameters;
            if (sensor is not BrainBitBlackSensor black) return dictionary;
            foreach (var parInfo in parameters)
            {
                var paramName = parInfo.Param.ToString().Replace("Parameter", "");
                var paramValue = parInfo.Param switch
                {
                    SensorParameter.ParameterSamplingFrequencyMEMS => black.SamplingFrequencyMEMS.ToString(),
                    SensorParameter.ParameterSamplingFrequencyFPG => black.SamplingFrequencyFPG.ToString(),
                    SensorParameter.ParameterAccelerometerSens => black.AccSens.ToString(),
                    SensorParameter.ParameterGyroscopeSens => black.GyroSens.ToString(),
                    SensorParameter.ParameterIrAmplitude => black.IrAmplitudeFPGSensor.ToString(),
                    SensorParameter.ParameterRedAmplitude=> black.RedAmplitudeFPGSensor.ToString(),
                    _ => null
                };
                if (paramValue != null)
                {
                    dictionary[paramName] = paramValue;
                }
            }

            dictionary["Amp mode"] = black.AmpMode.ToString();

            return dictionary;
        }

        public static List<string> GetSensorCommands(ISensor sensor)
        {
            return sensor.Commands.Select(x => x.ToString().Replace("Command", "")).ToList();
        }

        public static List<string> GetSensorFeatures(ISensor sensor)
        {
            return sensor.Features.Select(x => x.ToString().Replace("Feature", "")).ToList();
        }

        private static Dictionary<string, string> GetGeneralSensorParameter(ISensor sensor)
        {
            var dictionary = new Dictionary<string, string>();
            var parameters = sensor.Parameters;
            foreach (var parInfo in parameters)
            {
                var paramName = parInfo.Param.ToString().Replace("Parameter", "");
                var paramValue = parInfo.Param switch
                {
                    SensorParameter.ParameterName => sensor.Name,
                    SensorParameter.ParameterSensorFamily => sensor.SensFamily.ToString(),
                    SensorParameter.ParameterAddress => sensor.Address,
                    SensorParameter.ParameterSerialNumber => sensor.SerialNumber,
                    SensorParameter.ParameterBattPower => sensor.BattPower.ToString(),
                    SensorParameter.ParameterState => sensor.State.ToString(),
                    SensorParameter.ParameterSamplingFrequency => sensor.SamplingFrequency.ToString(),
                    SensorParameter.ParameterGain => sensor.Gain.ToString(),
                    SensorParameter.ParameterOffset => sensor.DataOffset.ToString(),
                    SensorParameter.ParameterFirmwareMode => sensor.FirmwareMode.ToString(),
                    _ => null
                };
                if (paramValue != null)
                {
                    dictionary[paramName] = paramValue;
                }

                var ver = sensor.Version;
                dictionary["ExtMajor"] = ver.ExtMajor.ToString();
                dictionary["FwMajor"] = ver.FwMajor.ToString();
                dictionary["HwMajor"] = ver.HwMajor.ToString();
                dictionary["FwMinor"] = ver.FwMinor.ToString();
                dictionary["HwMinor"] = ver.HwMinor.ToString();
                dictionary["FwPatch"] = ver.FwPatch.ToString();
                dictionary["HwPatch"] = ver.HwPatch.ToString();
            }

            return dictionary;
        }
    }
}