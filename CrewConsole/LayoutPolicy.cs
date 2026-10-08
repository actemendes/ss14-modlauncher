namespace SS14LocalMods.CrewConsole;

public readonly record struct ConsoleLayout(bool Portrait, float MapHeight, float ListWidth);

public static class LayoutPolicy
{
    public static ConsoleLayout Calculate(float width, float height)
    {
        var portrait = width < 760 || height > width;
        return new ConsoleLayout(portrait, portrait ? Math.Clamp(height * .45f, 130, 450) : 0,
            portrait ? 0 : Math.Clamp(width * .36f, 280, 480));
    }
}
