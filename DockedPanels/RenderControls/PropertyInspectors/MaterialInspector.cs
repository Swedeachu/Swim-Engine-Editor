using System.ComponentModel;
using System.Text.Json;

namespace SwimEditor
{

  public class MaterialInspectorModel
  {
    [Category("Material")]
    [DisplayName("Albedo Texture File Path")]
    public string AlbedoTextureFilePath { get; set; } = string.Empty;

    [Category("Material")]
    [DisplayName("Model File Path")]
    public string ModelFilePath { get; set; } = string.Empty;

    public static MaterialInspectorModel FromJson(string rawJson)
    {
      var model = new MaterialInspectorModel();

      if (string.IsNullOrWhiteSpace(rawJson))
      {
        return model;
      }

      try
      {
        using var doc = JsonDocument.Parse(rawJson);
        var root = doc.RootElement;

        if (root.ValueKind == JsonValueKind.Object)
        {
          if (root.TryGetProperty("albedoTextureFilePath", out var albedoProp) &&
              albedoProp.ValueKind == JsonValueKind.String)
          {
            model.AlbedoTextureFilePath = albedoProp.GetString() ?? string.Empty;
          }

          if (root.TryGetProperty("modelFilePath", out var modelProp) &&
              modelProp.ValueKind == JsonValueKind.String)
          {
            model.ModelFilePath = modelProp.GetString() ?? string.Empty;
          }
        }
      }
      catch
      {
        // keep defaults
      }

      return model;
    }

  } // class MaterialInspector

} // namespace SwimEditor