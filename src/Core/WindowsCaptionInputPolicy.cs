using Locaptio.Core.Audio;

namespace Locaptio.Core;
public static class WindowsCaptionInputPolicy
{
    // Windows controls its own default endpoints; microphone inclusion is its own toggle.
    public static bool Supported(AudioInputSelection input) => input.Kind == "system" && input.DeviceId.Length == 0
        || input.Kind == "mixed" && input.DeviceId.Length == 0;
    public static AudioInputSelection Normalize(AudioInputSelection input) => input.Kind is "mixed" or "microphone" ? new("mixed") : new();
    public static List<AudioInputChoice> Choices() => [new("PC内音声（既定の出力）", new()), new("PC内音声＋マイク（Windowsの既定の機器）", new("mixed"))];
}
