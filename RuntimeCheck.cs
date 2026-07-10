#if FRAMEWORK_DEPENDENT_BUILD
using System;
using System.Runtime.InteropServices;

namespace IconPull;

internal static class RuntimeCheck
{
    private const uint MbOk = 0;
    private const uint MbIconError = 0x10;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    public static bool EnsureSupported()
    {
        if (Environment.Version.Major >= 10)
        {
            return true;
        }

        MessageBoxW(
            IntPtr.Zero,
            "IconPull requires the .NET 10 Desktop Runtime.\n\n" +
            "Download it from:\nhttps://dotnet.microsoft.com/download/dotnet/10.0\n\n" +
            "Or use the self-contained build from the self-contained folder.",
            "IconPull - .NET Runtime Required",
            MbOk | MbIconError);

        return false;
    }
}
#endif
