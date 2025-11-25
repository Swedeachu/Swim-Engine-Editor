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

    // Optional display name from objectTag.name
    public string TagName { get; set; }

    public override string ToString()
    {
      // Prefer object tag "name" if available
      if (!string.IsNullOrWhiteSpace(TagName))
      {
        return TagName;
      }

      return $"Entity {Id}";
    }
  }

  public class HierarchyDock : DockContent
  {
    private readonly CrownTreeView treeView;

    private CommandManager CommandManager = new CommandManager();

    private CrownTreeNode sceneRootNode;
    private string sceneName = "Scene";

    private readonly Dictionary<int, SceneEntity> entitiesById = new Dictionary<int, SceneEntity>();
    private readonly Dictionary<int, CrownTreeNode> entityNodesById = new Dictionary<int, CrownTreeNode>();

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
      if (CommandManager == null)
      {
        return;
      }

      string usage =
        "scene load: <json>\n" +
        "  Loads a scene from JSON into the hierarchy panel.\n" +
        "scene sync: <json>\n" +
        "  Applies a batched diff (created/updated/destroyed entities) to the hierarchy.\n" +
        "scene entityCreate: <json>\n" +
        "  Creates/initializes a single entity in the hierarchy.\n" +
        "scene entityUpdate: <json>\n" +
        "  Updates an existing entity’s parent/components.\n" +
        "scene entityDestroy: <json>\n" +
        "  Destroys an entity from the hierarchy.";

      CommandManager.RegisterCommand(
        name: "scene",
        aliases: System.Array.Empty<string>(),
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
      if (trimmed.Length == 0)
      {
        return;
      }

      // Parse first token up to whitespace or ':' as the subcommand keyword.
      int i = 0;
      while (i < trimmed.Length && !char.IsWhiteSpace(trimmed[i]) && trimmed[i] != ':')
      {
        i++;
      }

      string keyword = trimmed.Substring(0, i);
      string remainder = trimmed.Substring(i);

      if (remainder.StartsWith(":", StringComparison.Ordinal))
      {
        remainder = remainder.Substring(1);
      }

      string payload = remainder.TrimStart();
      if (string.IsNullOrWhiteSpace(keyword))
      {
        return;
      }

      switch (keyword.ToLowerInvariant())
      {
        case "load":
        {
          if (!string.IsNullOrWhiteSpace(payload))
          {
            LoadSceneFromJson(payload);
          }
          break;
        }

        case "sync":
        {
          if (!string.IsNullOrWhiteSpace(payload))
          {
            ApplySceneSyncFromJson(payload);
          }
          break;
        }

        case "entitycreate":
        {
          if (!string.IsNullOrWhiteSpace(payload))
          {
            UpsertEntityFromJson(payload);
          }
          break;
        }

        case "entityupdate":
        {
          if (!string.IsNullOrWhiteSpace(payload))
          {
            UpsertEntityFromJson(payload);
          }
          break;
        }

        case "entitydestroy":
        {
          if (!string.IsNullOrWhiteSpace(payload))
          {
            DestroyEntityFromJson(payload);
          }
          break;
        }
      }
    }

    public void Command(string command)
    {
      if (string.IsNullOrEmpty(command))
      {
        return;
      }

      if (CommandManager == null)
      {
        return;
      }

      try
      {
        CommandManager.TryExecute(command);
      }
      catch (Exception e)
      {
        MainWindowForm.Instance?.Console.AppendLog(e.Message);
      }
    }

    private void LoadSceneFromJson(string json)
    {
      if (string.IsNullOrWhiteSpace(json))
      {
        return;
      }

      try
      {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        // Scene name from "scene" property, default to "Scene"
        string newSceneName = "Scene";
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("scene", out var sceneProp) &&
            sceneProp.ValueKind == JsonValueKind.String)
        {
          newSceneName = sceneProp.GetString() ?? "Scene";
        }

        List<SceneEntity> roots = ParseEntitiesFromRoot(root);

        // Reset model + tree
        entitiesById.Clear();
        entityNodesById.Clear();

        foreach (var e in roots)
        {
          CollectEntitiesRecursive(e);
        }

        treeView.Nodes.Clear();

        sceneName = newSceneName;
        sceneRootNode = new CrownTreeNode(sceneName);
        treeView.Nodes.Add(sceneRootNode);

        foreach (var entity in roots)
        {
          AddEntityHierarchy(entity, sceneRootNode);
        }

        sceneRootNode.Expanded = true;
      }
      catch (Exception e)
      {
        entitiesById.Clear();
        entityNodesById.Clear();
        treeView.Nodes.Clear();

        var errorRoot = new CrownTreeNode("Scene (Failed to load)");
        errorRoot.Nodes.Add(new CrownTreeNode(e.Message));
        treeView.Nodes.Add(errorRoot);

        errorRoot.Expanded = true;
      }
    }

    private void ApplySceneSyncFromJson(string json)
    {
      if (string.IsNullOrWhiteSpace(json))
      {
        return;
      }

      try
      {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
        {
          return;
        }

        // Optional scene name update
        if (root.TryGetProperty("scene", out var sceneProp) &&
            sceneProp.ValueKind == JsonValueKind.String)
        {
          sceneName = sceneProp.GetString() ?? sceneName;
          if (sceneRootNode != null)
          {
            sceneRootNode.Text = sceneName;
          }
        }

        // Created entities
        if (root.TryGetProperty("created", out var createdProp) &&
            createdProp.ValueKind == JsonValueKind.Array)
        {
          foreach (var elem in createdProp.EnumerateArray())
          {
            UpsertEntityFromElement(elem);
          }
        }

        // Updated entities
        if (root.TryGetProperty("updated", out var updatedProp) &&
            updatedProp.ValueKind == JsonValueKind.Array)
        {
          foreach (var elem in updatedProp.EnumerateArray())
          {
            UpsertEntityFromElement(elem);
          }
        }

        // Destroyed entities
        if (root.TryGetProperty("destroyed", out var destroyedProp) &&
            destroyedProp.ValueKind == JsonValueKind.Array)
        {
          foreach (var elem in destroyedProp.EnumerateArray())
          {
            int id;

            if (elem.ValueKind == JsonValueKind.Number && elem.TryGetInt32(out id))
            {
              DestroyEntityById(id);
            }
            else if (elem.ValueKind == JsonValueKind.Object &&
                     elem.TryGetProperty("id", out var idProp) &&
                     idProp.ValueKind == JsonValueKind.Number &&
                     idProp.TryGetInt32(out id))
            {
              DestroyEntityById(id);
            }
          }
        }
      }
      catch (Exception e)
      {
        MainWindowForm.Instance?.Console.AppendLog(e.Message);
      }
    }

    private void UpsertEntityFromJson(string json)
    {
      if (string.IsNullOrWhiteSpace(json))
      {
        return;
      }

      try
      {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement elem = doc.RootElement;
        UpsertEntityFromElement(elem);
      }
      catch (Exception e)
      {
        MainWindowForm.Instance?.Console.AppendLog(e.Message);
      }
    }

    private void UpsertEntityFromElement(JsonElement elem)
    {
      if (elem.ValueKind != JsonValueKind.Object)
      {
        return;
      }

      SceneEntity incoming = BuildSceneEntityFromJson(elem);
      if (incoming == null)
      {
        return;
      }

      if (!entitiesById.TryGetValue(incoming.Id, out SceneEntity existing))
      {
        // New entity
        existing = incoming;
        entitiesById[incoming.Id] = existing;
      }
      else
      {
        // Update existing entity's basic metadata + components + tag name
        existing.ParentId = incoming.ParentId;
        existing.RawJson = incoming.RawJson;
        existing.TagName = incoming.TagName;

        existing.Components.Clear();
        foreach (var c in incoming.Components)
        {
          existing.Components.Add(c);
        }
      }

      // Ensure we have a node
      if (!entityNodesById.TryGetValue(existing.Id, out CrownTreeNode node))
      {
        node = new CrownTreeNode(existing.ToString())
        {
          Tag = existing
        };
        entityNodesById[existing.Id] = node;
        AttachEntityNodeToParent(existing, node);
      }
      else
      {
        node.Text = existing.ToString();
        node.Tag = existing;

        // Re-parent if needed
        AttachEntityNodeToParent(existing, node);
      }

      // Replace component nodes under this entity node
      for (int i = node.Nodes.Count - 1; i >= 0; i--)
      {
        if (node.Nodes[i].Tag is SceneComponent)
        {
          node.Nodes.RemoveAt(i);
        }
      }

      foreach (var comp in existing.Components)
      {
        var compNode = new CrownTreeNode(comp.Name ?? "Component")
        {
          Tag = comp
        };
        node.Nodes.Add(compNode);
      }

      node.Expanded = true;
    }

    private void DestroyEntityFromJson(string json)
    {
      if (string.IsNullOrWhiteSpace(json))
      {
        return;
      }

      try
      {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement elem = doc.RootElement;

        if (elem.ValueKind != JsonValueKind.Object)
        {
          return;
        }

        if (!elem.TryGetProperty("id", out var idProp) ||
            idProp.ValueKind != JsonValueKind.Number ||
            !idProp.TryGetInt32(out int id))
        {
          return;
        }

        DestroyEntityById(id);
      }
      catch (Exception e)
      {
        MainWindowForm.Instance?.Console.AppendLog(e.Message);
      }
    }

    private void DestroyEntityById(int id)
    {
      entitiesById.Remove(id);

      if (entityNodesById.TryGetValue(id, out CrownTreeNode node))
      {
        entityNodesById.Remove(id);
        var parent = node.ParentNode;
        if (parent != null)
        {
          parent.Nodes.Remove(node);
        }
        else
        {
          treeView.Nodes.Remove(node);
        }
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
      var entitiesByTempId = new Dictionary<int, SceneEntity>();

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

        SceneEntity entity = BuildSceneEntityFromJson(elem);
        if (entity == null)
        {
          continue;
        }

        entitiesByTempId[entity.Id] = entity;
      }

      // Second pass: hook up children by ParentId
      var roots = new List<SceneEntity>();

      foreach (var ent in entitiesByTempId.Values)
      {
        if (ent.ParentId.HasValue && entitiesByTempId.TryGetValue(ent.ParentId.Value, out var parent))
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

    private SceneEntity BuildSceneEntityFromJson(JsonElement elem)
    {
      if (elem.ValueKind != JsonValueKind.Object)
      {
        return null;
      }

      if (!elem.TryGetProperty("id", out var idProp) ||
          idProp.ValueKind != JsonValueKind.Number ||
          !idProp.TryGetInt32(out var id))
      {
        return null; // invalid entity
      }

      int? parentId = null;
      if (elem.TryGetProperty("parent", out var parentProp))
      {
        if (parentProp.ValueKind == JsonValueKind.Number &&
            parentProp.TryGetInt32(out var pid))
        {
          parentId = pid;
        }
        else if (parentProp.ValueKind == JsonValueKind.Null)
        {
          parentId = null;
        }
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
        {
          continue;
        }

        if (prop.Value.ValueKind == JsonValueKind.Object)
        {
          // Special-case objectTag for label
          if (prop.NameEquals("objectTag"))
          {
            try
            {
              if (prop.Value.TryGetProperty("name", out var nameProp) &&
                  nameProp.ValueKind == JsonValueKind.String)
              {
                entity.TagName = nameProp.GetString();
              }
            }
            catch
            {
              // If it fails, we just fall back to default label.
            }
          }

          // Name like "Transform", "Material", "ObjectTag" etc.
          string friendlyName = char.ToUpperInvariant(prop.Name[0]) + prop.Name.Substring(1);
          var comp = new SceneComponent
          {
            Name = friendlyName,
            RawJson = prop.Value.GetRawText()
          };
          entity.Components.Add(comp);
        }
      }

      return entity;
    }

    private void CollectEntitiesRecursive(SceneEntity entity)
    {
      entitiesById[entity.Id] = entity;

      foreach (var child in entity.Children)
      {
        CollectEntitiesRecursive(child);
      }
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
    private void AddEntityHierarchy(SceneEntity entity, CrownTreeNode parentNode)
    {
      string label = entity.ToString();

      var entityNode = new CrownTreeNode(label)
      {
        Tag = entity
      };
      parentNode.Nodes.Add(entityNode);

      entityNodesById[entity.Id] = entityNode;

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
        AddEntityHierarchy(child, entityNode);
      }

      entityNode.Expanded = true;
    }

    private void AttachEntityNodeToParent(SceneEntity entity, CrownTreeNode node)
    {
      // Remove from old parent
      var currentParent = node.ParentNode;
      if (currentParent != null)
      {
        currentParent.Nodes.Remove(node);
      }
      else
      {
        treeView.Nodes.Remove(node);
      }

      CrownTreeNode parentNode = null;

      if (entity.ParentId.HasValue && entityNodesById.TryGetValue(entity.ParentId.Value, out var existingParentNode))
      {
        parentNode = existingParentNode;
      }
      else if (sceneRootNode != null)
      {
        parentNode = sceneRootNode;
      }

      if (parentNode != null)
      {
        parentNode.Nodes.Add(node);
      }
      else
      {
        // Fallback if we somehow have no scene root yet
        treeView.Nodes.Add(node);
      }
    }

  } // class HierarchyDock

} // namespace SwimEditor
