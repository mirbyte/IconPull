using System;
using Avalonia;

namespace IconPull;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
#if FRAMEWORK_DEPENDENT_BUILD
        if (!RuntimeCheck.EnsureSupported())
        {
            return;
        }
#endif

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
