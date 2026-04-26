using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;

namespace Clip;

public static class ClipTheme
{
    public static void ApplyMica(Window window)
    {
        if (MicaController.IsSupported())
        {
            window.SystemBackdrop = new MicaBackdrop
            {
                Kind = MicaKind.BaseAlt
            };
        }
    }
}
