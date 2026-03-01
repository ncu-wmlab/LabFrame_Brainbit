using UnityEditor;
using UnityEditor.Callbacks;

#if UNITY_IOS
using UnityEditor.iOS.Xcode;
using System.IO;
#endif

public sealed class BrainBitPostProcessor
{
    [PostProcessBuild(1)]
    public static void OnPostProcessBuild(BuildTarget target, string path)
    {
#if UNITY_IOS
        if (target == BuildTarget.iOS)
        {
            var infoPlistPath = Path.Combine(path, "Info.plist");
            var infoPlist = new PlistDocument();
            infoPlist.ReadFromFile(infoPlistPath);

            PlistElementDict dict = infoPlist.root.AsDict();
            dict.SetString("NSBluetoothAlwaysUsageDescription", 
                "App requires access to Bluetooth to allow you connect to device");
            dict.SetString("NSBluetoothPeripheralUsageDescription", 
                "App uses Bluetooth to connect with your Brainbit device");

            infoPlist.WriteToFile(infoPlistPath);
        }
#endif
    }
}