using System.Text.Json;
using WeifenLuo.WinFormsUI.Docking;
using ReaLTaiizor.Controls;

namespace SwimEditor
{
  // Backing models for hierarchy + inspector

  public class SceneComponent
  {
    public string Name { get; set; }
    public string RawJson { get; set; }  // <- store raw JSON text instead of JsonElement

    public override string ToString() => Name ?? "Component";
  }

  public class SceneEntity
  {
    public int Id { get; set; }
    public int? ParentId { get; set; }

    public List<SceneComponent> Components { get; } = new();
    public List<SceneEntity> Children { get; } = new();

    public JsonElement RawJson { get; set; } // raw blob

    public override string ToString()
    {
      // Maybe should use object tag compontent name field if available 
      return $"Entity {Id}";
    }
  }

  public class HierarchyDock : DockContent
  {
    private readonly CrownTreeView treeView;
    private readonly CommandManager commandManager = new CommandManager();

    // MainWindow subscribes and uses node.Tag to drive InspectorDock
    public event Action<object> OnSelectionChanged;

    public HierarchyDock()
    {
      treeView = new CrownTreeView
      {
        Dock = System.Windows.Forms.DockStyle.Fill
      };

      treeView.SelectedNodesChanged += (s, e) =>
      {
        var node = treeView.SelectedNodes.LastOrDefault();
        if (node != null)
        {
          // Node.Tag will be SceneEntity or SceneComponent
          OnSelectionChanged?.Invoke(node);
        }
      };

      Controls.Add(treeView);

      RegisterCommands();
    }

    private void RegisterCommands()
    {
      string usage = "scene load: <json>\n  Loads a scene from JSON into the hierarchy panel.";

      commandManager.RegisterCommand(
        name: "scene",
        aliases: Array.Empty<string>(),
        usage: usage,
        handler: args =>
        {
          HandleSceneCommand(args);
        }
      );
    }

    private void HandleSceneCommand(string args)
    {
      if (string.IsNullOrWhiteSpace(args))
      {
        return;
      }

      string trimmed = args.TrimStart();

      const string loadKeyword = "load";

      // Expect things like:
      // "load: { ...json... }"
      // "load { ...json... }"
      if (!trimmed.StartsWith(loadKeyword, StringComparison.OrdinalIgnoreCase))
      {
        return;
      }

      string remainder = trimmed.Substring(loadKeyword.Length);

      if (remainder.StartsWith(":", StringComparison.Ordinal))
      {
        remainder = remainder.Substring(1);
      }

      string json = remainder.TrimStart();
      if (string.IsNullOrWhiteSpace(json))
      {
        return;
      }

      LoadSceneFromJson(json);
    }

    public void Command(string command)
    {
      if (string.IsNullOrEmpty(command))
      {
        return;
      }

      try
      {
        commandManager.TryExecute(command);
      }
      catch (Exception ex)
      {
        MainWindowForm.Instance?.Console.AppendLog(ex.Message);
      }
    }

    private void LoadSceneFromJson(string json)
    {
      if (string.IsNullOrWhiteSpace(json))
        return;

      try
      {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        // Scene name from "scene" property, default to "Scene"
        string sceneName = "Scene";
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("scene", out var sceneProp) &&
            sceneProp.ValueKind == JsonValueKind.String)
        {
          sceneName = sceneProp.GetString() ?? "Scene";
        }

        // Parse entities from "entities" array (SandBox.json layout)
        List<SceneEntity> roots = ParseEntitiesFromRoot(root);

        // Build tree view
        treeView.Nodes.Clear();

        var sceneNode = new CrownTreeNode(sceneName);
        treeView.Nodes.Add(sceneNode);

        foreach (var entity in roots)
        {
          AddEntityNodeRecursive(entity, sceneNode);
        }

        sceneNode.Expanded = true;
      }
      catch (Exception ex)
      {
        treeView.Nodes.Clear();

        var errorRoot = new CrownTreeNode("Scene (Failed to load)");
        errorRoot.Nodes.Add(new CrownTreeNode(ex.Message));
        treeView.Nodes.Add(errorRoot);

        errorRoot.Expanded = true;
      }
    }

    /// <summary>
    /// Parse SandBox-style scene JSON into a list of root entities.
    /// Expected root shape:
    /// {
    ///   "scene": "SandBox",
    ///   "entities": [
    ///     { "id": 33, "parent": null, "transform":{...}, "material":{...} },
    ///     { "id": 32, "parent": 33,  "transform":{...} },
    ///     ...
    ///   ]
    /// }
    /// </summary>
    private List<SceneEntity> ParseEntitiesFromRoot(JsonElement root)
    {
      var entitiesById = new Dictionary<int, SceneEntity>();

      // Get entities array
      JsonElement entitiesArray;

      if (root.ValueKind == JsonValueKind.Object &&
          root.TryGetProperty("entities", out var eProp) &&
          eProp.ValueKind == JsonValueKind.Array)
      {
        entitiesArray = eProp;
      }
      else if (root.ValueKind == JsonValueKind.Array)
      {
        // Fallback: treat the root itself as an array of entities
        entitiesArray = root;
      }
      else
      {
        // Unknown shape, return empty
        return new List<SceneEntity>();
      }

      // First pass: create all entities and collect components (transform, material, etc.)
      foreach (var elem in entitiesArray.EnumerateArray())
      {
        if (elem.ValueKind != JsonValueKind.Object)
        {
          continue;
        }

        if (!elem.TryGetProperty("id", out var idProp) ||
            idProp.ValueKind != JsonValueKind.Number ||
            !idProp.TryGetInt32(out var id))
        {
          continue; // invalid entity, skip
        }

        int? parentId = null;
        if (elem.TryGetProperty("parent", out var parentProp) &&
            parentProp.ValueKind == JsonValueKind.Number &&
            parentProp.TryGetInt32(out var pid))
        {
          parentId = pid;
        }

        var entity = new SceneEntity
        {
          Id = id,
          ParentId = parentId,
          RawJson = elem
        };

        // Treat any object-valued property (except "id" / "parent") as a component
        foreach (var prop in elem.EnumerateObject())
        {
          if (prop.NameEquals("id") || prop.NameEquals("parent"))
            continue;

          if (prop.Value.ValueKind == JsonValueKind.Object)
          {
            // Name like "Transform", "Material" etc.
            string friendlyName = char.ToUpperInvariant(prop.Name[0]) + prop.Name.Substring(1);
            var comp = new SceneComponent
            {
              Name = friendlyName,
              RawJson = prop.Value.GetRawText()
            };
            entity.Components.Add(comp);
          }
        }

        entitiesById[id] = entity;
      }

      // Second pass: hook up children by ParentId
      var roots = new List<SceneEntity>();

      foreach (var ent in entitiesById.Values)
      {
        if (ent.ParentId.HasValue && entitiesById.TryGetValue(ent.ParentId.Value, out var parent))
        {
          parent.Children.Add(ent);
        }
        else
        {
          // Parent null or missing -> root-level entity under scene node
          roots.Add(ent);
        }
      }

      return roots;
    }

    /// <summary>
    /// Unity-like placement:
    /// SandBox
    ///   Entity 33
    ///     Transform
    ///     Material
    ///     Entity 32
    ///       Transform
    /// </summary>
    private void AddEntityNodeRecursive(SceneEntity entity, CrownTreeNode parentNode)
    {
      string label = $"Entity {entity.Id}";

      var entityNode = new CrownTreeNode(label)
      {
        Tag = entity
      };
      parentNode.Nodes.Add(entityNode);

      // Components directly under the entity
      foreach (var comp in entity.Components)
      {
        var compNode = new CrownTreeNode(comp.Name ?? "Component")
        {
          Tag = comp
        };
        entityNode.Nodes.Add(compNode);
      }

      // Then child entities
      foreach (var child in entity.Children)
      {
        AddEntityNodeRecursive(child, entityNode);
      }

      entityNode.Expanded = true;
    }

  } // class HierarchyDock

} // namespace SwimEditor
