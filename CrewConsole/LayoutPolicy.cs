using System.Numerics;

namespace SS14LocalMods.CrewConsole;

public readonly record struct ConsoleLayout(bool Portrait, float MapHeight, float ListWidth);

public static class LayoutPolicy
{
    public static ConsoleLayout Calculate(float width, float height)
    {
        var portrait = width < 900 || height > width;
        return new ConsoleLayout(portrait, portrait ? Math.Clamp((height - 480) * .48f, 140, 300) : float.NaN,
            portrait ? float.NaN : Math.Clamp(width * .34f, 320, 440));
    }

    public static float MapScale(Vector2 pixels, float range) => range > 0
        ? Math.Max(1, Math.Min(pixels.X, pixels.Y)) / (2 * range) : 0;

    public static Vector2 Project(Vector2 local, Vector2 offset, Vector2 midpoint, float scale)
    {
        var shifted = local - offset;
        return new Vector2(shifted.X, -shifted.Y) * scale + midpoint;
    }
}
