using System.ComponentModel;

namespace SwimEditor
{

  public class EntityInspectorModel
  {
    [Category("Entity")]
    [DisplayName("Id")]
    public int Id { get; set; }

    [Category("Entity")]
    [DisplayName("Name")]
    public string Name { get; set; }

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

  } // class EntityInspector

} // namespace SwimEditor
