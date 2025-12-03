using System.ComponentModel;
using System.Text.Json;

namespace SwimEditor
{

  public class TransformInspectorModel
  {
    [Category("Transform")]
    [DisplayName("Parent")]
    public string Parent { get; set; }

    [Category("Transform")]
    [DisplayName("Position X")]
    public float PositionX { get; set; }

    [Category("Transform")]
    [DisplayName("Position Y")]
    public float PositionY { get; set; }

    [Category("Transform")]
    [DisplayName("Position Z")]
    public float PositionZ { get; set; }

    [Category("Transform")]
    [DisplayName("Rotation X")]
    public float RotationX { get; set; }

    [Category("Transform")]
    [DisplayName("Rotation Y")]
    public float RotationY { get; set; }

    [Category("Transform")]
    [DisplayName("Rotation Z")]
    public float RotationZ { get; set; }

    [Category("Transform")]
    [DisplayName("Scale X")]
    public float ScaleX { get; set; } = 1.0f;

    [Category("Transform")]
    [DisplayName("Scale Y")]
    public float ScaleY { get; set; } = 1.0f;

    [Category("Transform")]
    [DisplayName("Scale Z")]
    public float ScaleZ { get; set; } = 1.0f;

    public static TransformInspectorModel FromJson(string rawJson, int? parentId = null, string parentName = null)
    {
      var model = new TransformInspectorModel();

      // Parent as "8 (Orbit System)" or blank if none.
      if (parentId.HasValue && parentId.Value != 0)
      {
        string parentBase = !string.IsNullOrWhiteSpace(parentName)
          ? parentName
          : $"Entity {parentId.Value}";

        model.Parent = $"{parentId.Value} ({parentBase})";
      }
      else
      {
        model.Parent = string.Empty;
      }

      if (string.IsNullOrWhiteSpace(rawJson))
        return model;

      try
      {
        using var doc = JsonDocument.Parse(rawJson);
        var root = doc.RootElement;

        // position { x, y, z }
        if (ReadVec3(root, "position", out float px, out float py, out float pz))
        {
          model.PositionX = px;
          model.PositionY = py;
          model.PositionZ = pz;
        }

        // rotationEuler { x, y, z } OR rotation { x, y, z } (fallback)
        if (ReadVec3(root, "rotationEuler", out float rx, out float ry, out float rz) ||
            ReadVec3(root, "rotation", out rx, out ry, out rz))
        {
          model.RotationX = rx;
          model.RotationY = ry;
          model.RotationZ = rz;
        }

        // scale { x, y, z }
        if (ReadVec3(root, "scale", out float sx, out float sy, out float sz))
        {
          model.ScaleX = sx;
          model.ScaleY = sy;
          model.ScaleZ = sz;
        }
        else
        {
          // default scale to 1 if no scale vector found
          model.ScaleX = model.ScaleY = model.ScaleZ = 1.0f;
        }
      }
      catch
      {
        // ignore parse errors; just keep defaults
      }

      return model;
    }

    private static bool ReadVec3(JsonElement parent, string name,
                                out float x, out float y, out float z)
    {
      x = y = z = 0f;

      if (parent.ValueKind != JsonValueKind.Object ||
          !parent.TryGetProperty(name, out var vecElem) ||
          vecElem.ValueKind != JsonValueKind.Object)
      {
        return false;
      }

      x = ReadFloat(vecElem, "x");
      y = ReadFloat(vecElem, "y");
      z = ReadFloat(vecElem, "z");
      return true;
    }

    private static float ReadFloat(JsonElement parent, string name)
    {
      if (parent.TryGetProperty(name, out var prop) &&
          prop.ValueKind == JsonValueKind.Number)
      {
        if (prop.TryGetSingle(out var f))
          return f;

        if (prop.TryGetDouble(out var d))
          return (float)d;
      }

      return 0f;
    }

  } // class TransformInspectorModel

} // namespace SwimEditor