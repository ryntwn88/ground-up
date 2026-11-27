using Godot;
using GroundUp.Core.Gear;

namespace GroundUp.GodotClient;

public static class GodotMathAdapter
{
    public static Vector3 ToGodot(this System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);

    public static System.Numerics.Vector3 ToCore(this Vector3 v) => new(v.X, v.Y, v.Z);

    /// <summary>
    /// Builds an orthonormal Transform3D whose local +Y axis spans from <paramref name="pointA"/> to <paramref name="pointB"/>.
    /// </summary>
    public static Transform3D CreateCylinderTransform(Vector3 pointA, Vector3 pointB, out float length)
    {
        Vector3 delta = pointB - pointA;
        length = delta.Length();
        if (length < 0.0005f)
        {
            return new Transform3D(Basis.Identity, pointA);
        }

        Vector3 yAxis = delta / length;
        Vector3 reference = Mathf.Abs(yAxis.Dot(Vector3.Forward)) > 0.94f ? Vector3.Right : Vector3.Forward;
        Vector3 xAxis = yAxis.Cross(reference).Normalized();
        Vector3 zAxis = xAxis.Cross(yAxis).Normalized();

        return new Transform3D(new Basis(xAxis, yAxis, zAxis), (pointA + pointB) * 0.5f);
    }

    public static Color GetCamAnodizedColor(string code) => code switch
    {
        "#0.1" => new Color(0.88f, 0.22f, 0.22f),
        "#0.2" => new Color(0.94f, 0.80f, 0.18f),
        "#0.3" => new Color(0.20f, 0.55f, 0.92f),
        "#0.4" => new Color(0.62f, 0.65f, 0.68f),
        "#0.5" => new Color(0.66f, 0.30f, 0.86f),
        "#0.75" => new Color(0.20f, 0.78f, 0.36f),
        "#1" => new Color(0.86f, 0.20f, 0.20f),
        "#2" => new Color(0.95f, 0.82f, 0.16f),
        "#3" => new Color(0.18f, 0.50f, 0.90f),
        "#4" => new Color(0.58f, 0.60f, 0.64f),
        _ => new Color(0.78f, 0.80f, 0.84f)
    };

    public static Color GetGearHardwareColor(GearSpecBase gear) => gear.Kind switch
    {
        GearKind.Cam => GetCamAnodizedColor(gear.Code),
        GearKind.Piton => new Color(0.52f, 0.55f, 0.60f),
        GearKind.Skyhook => new Color(0.76f, 0.82f, 0.88f),
        GearKind.Sling => gear.Code == "SLING-60" ? new Color(0.95f, 0.48f, 0.14f) : new Color(0.18f, 0.68f, 0.85f),
        _ => new Color(0.85f, 0.85f, 0.85f)
    };
}
