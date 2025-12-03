using System.ComponentModel;
using System.Text.Json;

namespace SwimEditor
{

  public class JsonObjectView : ICustomTypeDescriptor
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

    } // private sealed class JsonPropertyDescriptor : PropertyDescriptor

  } // class JsonObjectView

} // namespace SwimEditor