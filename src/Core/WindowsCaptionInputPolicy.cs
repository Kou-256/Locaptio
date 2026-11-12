using Locaptio.Core.Audio;

namespace Locaptio.Core;
public static class WindowsCaptionInputPolicy
{
    // Windows controls its own default endpoints; it cannot consume our selected PCM.
    public static bool Supported(AudioInputSelection input) => input.Kind == "system" && input.DeviceId.Length == 0
        || input.Kind == "microphone" && input.DeviceId.Length == 0;
}
