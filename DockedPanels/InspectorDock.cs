using System.ComponentModel;
using System.Text.Json;
using WeifenLuo.WinFormsUI.Docking;
using Panel = ReaLTaiizor.Controls.Panel;

namespace SwimEditor
{

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
          // Show parent as "8 (Orbit System)" or blank if none.
          propertyGrid.SelectedObject = TransformInspectorModel.FromJson(
            comp.RawJson,
            comp.OwnerParentId,
            comp.OwnerParentName
          );
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

      // Entity -> use a view model so we can show "8 (Orbit System)" nicely
      if (obj is SceneEntity ent)
      {
        propertyGrid.SelectedObject = EntityInspectorModel.FromEntity(ent);
        return;
      }

      // Fallback
      propertyGrid.SelectedObject = obj;
    }

    // ------------------------------------------------------------------
    // Strongly-typed ENTITY inspector
    // ------------------------------------------------------------------
    public sealed class EntityInspectorModel
    {
      [Category("Entity")]
      [DisplayName("Id")]
      public int Id { get; set; }

      [Category("Entity")]
      [DisplayName("Name")]
      public string Name { get; set; }

      // Example: "8 (Orbit System)" or "" if no parent.
      [Category("Entity")]
      [DisplayName("Parent")]
      public string Parent { get; set; }

      public static EntityInspectorModel FromEntity(SceneEntity ent)
      {
        var model = new EntityInspectorModel
        {
          Id = ent.Id,
          Name = !string.IsNullOrWhiteSpace(ent.TagName)
            ? ent.TagName
            : $"Entity {ent.Id}"
        };

        if (ent.ParentId.HasValue && ent.ParentId.Value != 0)
        {
          string parentBase = !string.IsNullOrWhiteSpace(ent.ParentName)
            ? ent.ParentName
            : $"Entity {ent.ParentId.Value}";

          model.Parent = $"{ent.ParentId.Value} ({parentBase})";
        }
        else
        {
          model.Parent = string.Empty;
        }

        return model;
      }
    }

    // ------------------------------------------------------------------
    // Strongly-typed TRANSFORM inspector
    // ------------------------------------------------------------------
    public sealed class TransformInspectorModel
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
    }

    // ------------------------------------------------------------------
    // MATERIAL inspector: matches SerializeMaterial exactly
    // ------------------------------------------------------------------
    public sealed class MaterialInspectorModel
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
          return model;

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
    }

    // ------------------------------------------------------------------
    // Generic JSON property view (for non-transform/material components)
    // ------------------------------------------------------------------
    private sealed class JsonObjectView : ICustomTypeDescriptor
    {
      private readonly string displayName;
      private readonly JsonDocument doc;
      private readonly JsonElement element;
      private readonly PropertyDescriptorCollection props;

      public JsonObjectView(string displayName, string rawJson)
      {
        this.displayName = displayName;

        if (string.IsNullOrWhiteSpace(rawJson))
        {
          doc = JsonDocument.Parse("{}");
        }
        else
        {
          doc = JsonDocument.Parse(rawJson);
        }

        element = doc.RootElement;
        props = BuildProperties(element);
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

      public string GetClassName() => displayName;

      public string GetComponentName() => displayName;

      public TypeConverter GetConverter() => TypeDescriptor.GetConverter(typeof(object));

      public EventDescriptor GetDefaultEvent() => null;

      public PropertyDescriptor GetDefaultProperty() => null;

      public object GetEditor(Type editorBaseType) => null;

      public EventDescriptorCollection GetEvents() => EventDescriptorCollection.Empty;

      public EventDescriptorCollection GetEvents(Attribute[] attributes) => EventDescriptorCollection.Empty;

      public PropertyDescriptorCollection GetProperties() => props;

      public PropertyDescriptorCollection GetProperties(Attribute[] attributes) => props;

      public object GetPropertyOwner(PropertyDescriptor pd) => this;

      private sealed class JsonPropertyDescriptor : PropertyDescriptor
      {
        private readonly JsonElement value;

        public JsonPropertyDescriptor(string name, JsonElement value)
          : base(name, null)
        {
          this.value = value;
        }

        public override bool CanResetValue(object component) => false;

        public override Type ComponentType => typeof(JsonObjectView);

        public override object GetValue(object component) => ToDisplayString(value);

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
