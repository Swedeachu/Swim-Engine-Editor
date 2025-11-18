using ReaLTaiizor.Controls;
using ReaLTaiizor.Docking.Crown;
using System;
using System.ComponentModel;
using System.Drawing;
using System.Text.Json;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;
using Panel = ReaLTaiizor.Controls.Panel;

namespace SwimEditor
{
  // THIS IS JUST A PLACE HOLDER, WILL BE CUSTOMIZED LATER FOR EDITING TRANSFORMS, MATERIALS, TAGS, OTHER SERIALIZED COMPONENTS
  public class InspectorDock : DockContent
  {
    private PropertyGrid propertyGrid;
    private Panel container; 

    public InspectorDock()
    {
      // Container panel to provide consistent background and padding if needed
      container = new Panel 
      {
        Dock = DockStyle.Fill,
        BackColor = SwimEditorTheme.Panel
      };

      propertyGrid = new DarkPropertyGrid
      {
        Dock = DockStyle.Fill,
        ToolbarVisible = true,
        HelpVisible = true
      };

      // Unity-like dark theme for PropertyGrid (explicit colors remain necessary)
      var bg = SwimEditorTheme.Panel;
      var viewBg = SwimEditorTheme.Bg;
      var text = SwimEditorTheme.Text;
      var line = SwimEditorTheme.Line;

      propertyGrid.BackColor = bg;
      propertyGrid.ForeColor = text;
      propertyGrid.ViewBackColor = viewBg;
      propertyGrid.ViewForeColor = text;
      propertyGrid.LineColor = line;

      propertyGrid.CategoryForeColor = text;
      propertyGrid.CategorySplitterColor = line;

      propertyGrid.HelpBackColor = viewBg;
      propertyGrid.HelpForeColor = text;

      propertyGrid.CommandsBackColor = bg;
      propertyGrid.CommandsForeColor = text;

      container.Controls.Add(propertyGrid);
      Controls.Add(container);

      BackColor = SwimEditorTheme.PageBg;
    }

    public void SetInspectedObject(object obj)
    {
      if (obj == null)
      {
        propertyGrid.SelectedObject = null;
        return;
      }

      // Component -> custom inspector depending on type/name
      if (obj is SceneComponent comp)
      {
        var name = comp.Name ?? string.Empty;
        var nameLower = name.ToLowerInvariant();

        if (nameLower == "transform")
        {
          propertyGrid.SelectedObject = TransformInspectorModel.FromJson(comp.RawJson);
          return;
        }

        if (nameLower == "material")
        {
          propertyGrid.SelectedObject = MaterialInspectorModel.FromJson(comp.RawJson);
          return;
        }

        // Fallback for any other component: generic JSON viewer (read-only)
        propertyGrid.SelectedObject = new JsonObjectView(name, comp.RawJson);
        return;
      }

      // Entity -> show its C# properties (Id, ParentId, etc.)
      if (obj is SceneEntity ent)
      {
        propertyGrid.SelectedObject = ent;
        return;
      }

      // Fallback
      propertyGrid.SelectedObject = obj;
    }

    // ------------------------------------------------------------------
    // Strongly-typed TRANSFORM inspector
    // ------------------------------------------------------------------
    public sealed class TransformInspectorModel
    {
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

      public static TransformInspectorModel FromJson(string rawJson)
      {
        var model = new TransformInspectorModel();

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
    }

    // ------------------------------------------------------------------
    // Strongly-typed MATERIAL inspector (example layout)
    // ------------------------------------------------------------------
    public sealed class MaterialInspectorModel
    {
      [Category("Material")]
      [DisplayName("Name")]
      public string Name { get; set; }

      [Category("Material - Albedo")]
      [DisplayName("R")]
      public float AlbedoR { get; set; }

      [Category("Material - Albedo")]
      [DisplayName("G")]
      public float AlbedoG { get; set; }

      [Category("Material - Albedo")]
      [DisplayName("B")]
      public float AlbedoB { get; set; }

      [Category("Material - Albedo")]
      [DisplayName("A")]
      public float AlbedoA { get; set; } = 1.0f;

      [Category("Material")]
      [DisplayName("Metallic")]
      public float Metallic { get; set; }

      [Category("Material")]
      [DisplayName("Roughness")]
      public float Roughness { get; set; }

      [Category("Material")]
      [DisplayName("Shader")]
      public string Shader { get; set; }

      public static MaterialInspectorModel FromJson(string rawJson)
      {
        var model = new MaterialInspectorModel();

        if (string.IsNullOrWhiteSpace(rawJson))
          return model;

        try
        {
          using var doc = JsonDocument.Parse(rawJson);
          var root = doc.RootElement;

          // name / shader
          if (root.TryGetProperty("name", out var nameProp) &&
              nameProp.ValueKind == JsonValueKind.String)
          {
            model.Name = nameProp.GetString();
          }

          if (root.TryGetProperty("shader", out var shaderProp) &&
              shaderProp.ValueKind == JsonValueKind.String)
          {
            model.Shader = shaderProp.GetString();
          }

          // albedo { r, g, b, a }
          if (root.TryGetProperty("albedo", out var albedoElem) &&
              albedoElem.ValueKind == JsonValueKind.Object)
          {
            model.AlbedoR = ReadFloat(albedoElem, "r");
            model.AlbedoG = ReadFloat(albedoElem, "g");
            model.AlbedoB = ReadFloat(albedoElem, "b");
            model.AlbedoA = ReadFloat(albedoElem, "a");
            if (model.AlbedoA == 0f) model.AlbedoA = 1.0f; // sane default
          }

          // metallic / roughness
          model.Metallic = ReadFloat(root, "metallic");
          model.Roughness = ReadFloat(root, "roughness");
        }
        catch
        {
          // ignore parse errors; keep defaults
        }

        return model;
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
    }

    // ------------------------------------------------------------------
    // Generic JSON property view (for non-transform/material components)
    // ------------------------------------------------------------------
    private sealed class JsonObjectView : ICustomTypeDescriptor
    {
      private readonly string _displayName;
      private readonly JsonDocument _doc;
      private readonly JsonElement _element;
      private readonly PropertyDescriptorCollection _props;

      public JsonObjectView(string displayName, string rawJson)
      {
        _displayName = displayName;

        if (string.IsNullOrWhiteSpace(rawJson))
        {
          _doc = JsonDocument.Parse("{}");
        }
        else
        {
          _doc = JsonDocument.Parse(rawJson);
        }

        _element = _doc.RootElement;
        _props = BuildProperties(_element);
      }

      private static PropertyDescriptorCollection BuildProperties(JsonElement elem)
      {
        if (elem.ValueKind != JsonValueKind.Object)
          return new PropertyDescriptorCollection(Array.Empty<PropertyDescriptor>(), true);

        var list = new System.Collections.Generic.List<PropertyDescriptor>();

        foreach (var prop in elem.EnumerateObject())
        {
          list.Add(new JsonPropertyDescriptor(prop.Name, prop.Value));
        }

        return new PropertyDescriptorCollection(list.ToArray(), true);
      }

      // --- ICustomTypeDescriptor implementation ---

      public AttributeCollection GetAttributes() => AttributeCollection.Empty;

      public string GetClassName() => _displayName;

      public string GetComponentName() => _displayName;

      public TypeConverter GetConverter() => TypeDescriptor.GetConverter(typeof(object));

      public EventDescriptor GetDefaultEvent() => null;

      public PropertyDescriptor GetDefaultProperty() => null;

      public object GetEditor(Type editorBaseType) => null;

      public EventDescriptorCollection GetEvents() => EventDescriptorCollection.Empty;

      public EventDescriptorCollection GetEvents(Attribute[] attributes) => EventDescriptorCollection.Empty;

      public PropertyDescriptorCollection GetProperties() => _props;

      public PropertyDescriptorCollection GetProperties(Attribute[] attributes) => _props;

      public object GetPropertyOwner(PropertyDescriptor pd) => this;

      private sealed class JsonPropertyDescriptor : PropertyDescriptor
      {
        private readonly JsonElement _value;

        public JsonPropertyDescriptor(string name, JsonElement value)
          : base(name, null)
        {
          _value = value;
        }

        public override bool CanResetValue(object component) => false;

        public override Type ComponentType => typeof(JsonObjectView);

        public override object GetValue(object component) => ToDisplayString(_value);

        public override bool IsReadOnly => true;

        public override Type PropertyType => typeof(string);

        public override void ResetValue(object component) { }

        public override void SetValue(object component, object value) { }

        public override bool ShouldSerializeValue(object component) => false;

        private static string ToDisplayString(JsonElement elem)
        {
          switch (elem.ValueKind)
          {
            case JsonValueKind.String:
              return elem.GetString() ?? string.Empty;

            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
            case JsonValueKind.Null:
              return elem.ToString();

            case JsonValueKind.Object:
            case JsonValueKind.Array:
              return elem.GetRawText();

            default:
              return elem.ToString();
          }
        }
      }
    }

  } // class InspectorDock

} // Namespace SwimEditor
