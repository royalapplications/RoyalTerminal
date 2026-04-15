namespace RoyalTerminal.Tests;

public static class TestPlatformConditions
{
    public static bool IsLinux => OperatingSystem.IsLinux();

    public static bool IsMacOS => OperatingSystem.IsMacOS();
}
