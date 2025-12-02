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

    // Owning entity + parent info so inspector can show parent on components (e.g. Transform)
    public int OwnerEntityId { get; set; }
    public int? OwnerParentId { get; set; }
    public string OwnerParentName { get; set; }

    public override string ToString() => Name ?? "Component";
  }

  public class SceneEntity
  {
    public int Id { get; set; }
    public int? ParentId { get; set; }

    // Filled after we build parent/child relationships so inspector can show "8 (Orbit System)"
    public string ParentName { get; set; }

    public List<SceneComponent> Components { get; } = new();
    public List<SceneEntity> Children { get; } = new();

    public JsonElement RawJson { get; set; } // raw blob

    // Optional display name from objectTag.name
    public string TagName { get; set; }

    public override string ToString()
    {
      // Keep this simple; HierarchyDock or inspector can build richer labels.
      if (!string.IsNullOrWhiteSpace(TagName))
      {
        return TagName;
      }

      return $"Entity {Id}";
    }
  }

  public class HierarchyDock : DockContent
  {
    private readonly HierarchyTreeView treeView;

    private CommandManager CommandManager = new CommandManager();

    private CrownTreeNode sceneRootNode;
    private string sceneName = "Scene";

    // Scene model (ID -> entity data)
    private readonly Dictionary<int, SceneEntity> entitiesById = new Dictionary<int, SceneEntity>();

    // UI nodes mapped by entity ID
    private readonly Dictionary<int, CrownTreeNode> entityNodesById = new Dictionary<int, CrownTreeNode>();

    // MainWindow subscribes and uses node.Tag to drive InspectorDock
    public event Action<object> OnSelectionChanged;

    public HierarchyDock()
    {
      treeView = new HierarchyTreeView
      {
        Dock = System.Windows.Forms.DockStyle.Fill
      };

      treeView.MouseClick += NodeMouseClick;

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

    private void NodeMouseClick(object sender, MouseEventArgs e)
    {
      if (e.Button != MouseButtons.Right)
      {
        return;
      }

      // CrownTreeView already selected the node on MouseDown for right-click.
      var node = treeView.SelectedNodes.LastOrDefault();
      if (node == null)
      {
        return; // right-clicked on empty area
      }

      var menu = new CrownContextMenuStrip();

      if (node.Tag is SceneEntity entity)
      {
        int id = entity.Id;

        menu.Items.Add("Create Child Entity", null, (s, _) =>
        {
          MainWindowForm.Instance.GameView.SendEngineMessage($"(scene.entity.create {id})");
        });

        menu.Items.Add("Delete Entity", null, (s, _) =>
        {
          // true = destroy children; you could pop a dialog to ask.
          MainWindowForm.Instance.GameView.SendEngineMessage($"(scene.entity.destroy {id} true)");
        });

        menu.Items.Add("Add Material", null, (s, _) =>
        {
          MainWindowForm.Instance.GameView.SendEngineMessage($"(scene.entity.addComponent {id} \"Material\")");
        });

        menu.Items.Add("Remove Material", null, (s, _) =>
        {
          MainWindowForm.Instance.GameView.SendEngineMessage($"(scene.entity.removeComponent {id} \"Material\")");
        });
      }
      else if (node.Tag is SceneComponent comp)
      {
        // Optional: context menu when right-clicking a component node
        int id = comp.OwnerEntityId;

        menu.Items.Add("Remove Component", null, (s, _) =>
        {
          MainWindowForm.Instance.GameView.SendEngineMessage($"(scene.entity.removeComponent {id} \"{comp.Name}\")");
        });
      }

      // Root-level create: only when the scene root node itself is clicked
      if (node == sceneRootNode)
      {
        menu.Items.Add("Create Entity", null, (s, _) =>
        {
          MainWindowForm.Instance.GameView.SendEngineMessage("(scene.entity.create 0)");
        });
      }

      if (menu.Items.Count > 0)
      {
        menu.Show(treeView, e.Location);
      }
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

        sceneName = newSceneName;

        // Rebuild the model from the "entities" array
        entitiesById.Clear();

        JsonElement entitiesArray;

        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("entities", out var eProp) &&
            eProp.ValueKind == JsonValueKind.Array)
        {
          entitiesArray = eProp;
        }
        else if (root.ValueKind == JsonValueKind.Array)
        {
          // Fallback: treat root itself as the array
          entitiesArray = root;
        }
        else
        {
          // No entities; just clear UI
          treeView.Nodes.Clear();
          entityNodesById.Clear();

          sceneRootNode = new CrownTreeNode(sceneName);
          treeView.Nodes.Add(sceneRootNode);
          sceneRootNode.Expanded = true;
          return;
        }

        foreach (var elem in entitiesArray.EnumerateArray())
        {
          if (elem.ValueKind != JsonValueKind.Object)
          {
            continue;
          }

          var entity = BuildSceneEntityFromJson(elem);
          if (entity == null)
          {
            continue;
          }

          entitiesById[entity.Id] = entity;
        }

        // Full load; preserve previous UI state (expanded/collapsed, selection, scroll) by ID.
        RebuildTreeFromEntities(preserveState: true);
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
              entitiesById.Remove(id);
            }
            else if (elem.ValueKind == JsonValueKind.Object &&
                     elem.TryGetProperty("id", out var idProp) &&
                     idProp.ValueKind == JsonValueKind.Number &&
                     idProp.TryGetInt32(out id))
            {
              entitiesById.Remove(id);
            }
          }
        }

        // Now rebuild hierarchy from the updated model, preserving UX where possible.
        RebuildTreeFromEntities(preserveState: true);
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

        // Single-entity create/update; rebuild to keep structure correct.
        RebuildTreeFromEntities(preserveState: true);
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

        entitiesById.Remove(id);

        // Rebuild after destroy so children are re-parented correctly
        RebuildTreeFromEntities(preserveState: true);
      }
      catch (Exception e)
      {
        MainWindowForm.Instance?.Console.AppendLog(e.Message);
      }
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

      // More defensive parent parsing: number or string; treat 0 or self as "no parent".
      if (elem.TryGetProperty("parent", out var parentProp))
      {
        if (parentProp.ValueKind == JsonValueKind.Number &&
            parentProp.TryGetInt32(out var pidNum))
        {
          if (pidNum > 0 && pidNum != id)
          {
            parentId = pidNum;
          }
        }
        else if (parentProp.ValueKind == JsonValueKind.String)
        {
          var s = parentProp.GetString();
          if (int.TryParse(s, out var pidStr) && pidStr > 0 && pidStr != id)
          {
            parentId = pidStr;
          }
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

    /// <summary>
    /// Builds the entire tree from entitiesById using ParentId relationships.
    /// Also fills SceneEntity.ParentName so the inspector can show "id (Name)".
    /// Preserves:
    ///   - Expanded entities (by ID)
    ///   - Selected entity
    ///   - Scroll position via HierarchyTreeView.VerticalScrollValue
    /// </summary>
    private void RebuildTreeFromEntities(bool preserveState)
    {
      // Snapshot current expansion, selection and scroll before we blow away the tree.
      HashSet<int> expandedIds = new HashSet<int>();
      int? selectedId = null;
      int scrollValue = 0;

      if (preserveState && sceneRootNode != null)
      {
        foreach (var kvp in entityNodesById)
        {
          var node = kvp.Value;
          if (node != null && node.Expanded)
          {
            expandedIds.Add(kvp.Key);
          }
        }

        var selectedNode = treeView.SelectedNodes.LastOrDefault();
        if (selectedNode != null && selectedNode.Tag is SceneEntity selectedEntity)
        {
          selectedId = selectedEntity.Id;
        }

        scrollValue = treeView.VerticalScrollValue;
      }

      // Clear old UI mappings
      treeView.Nodes.Clear();
      entityNodesById.Clear();

      // Rebuild parent/child relationships in the model first.
      foreach (var entity in entitiesById.Values)
      {
        entity.Children.Clear();
      }

      var roots = new List<SceneEntity>();

      foreach (var entity in entitiesById.Values)
      {
        if (entity.ParentId.HasValue &&
            entity.ParentId.Value != 0 &&
            entity.ParentId.Value != entity.Id &&
            entitiesById.TryGetValue(entity.ParentId.Value, out var parent))
        {
          parent.Children.Add(entity);
        }
        else
        {
          // Parent null, 0, self, or missing -> root-level entity under scene node
          roots.Add(entity);
        }
      }

      // Fill ParentName for inspector (e.g., "Orbit System")
      foreach (var entity in entitiesById.Values)
      {
        if (entity.ParentId.HasValue &&
            entity.ParentId.Value != 0 &&
            entitiesById.TryGetValue(entity.ParentId.Value, out var parent))
        {
          entity.ParentName = !string.IsNullOrWhiteSpace(parent.TagName)
            ? parent.TagName
            : $"Entity {parent.Id}";
        }
        else
        {
          entity.ParentName = null;
        }
      }

      // Recreate root node
      sceneRootNode = new CrownTreeNode(sceneName);
      treeView.Nodes.Add(sceneRootNode);

      // Add all roots and their children recursively
      foreach (var entity in roots)
      {
        AddEntityHierarchy(entity, sceneRootNode);
      }

      // Root always shown; children expansion handled below
      sceneRootNode.Expanded = true;

      // Restore expansion
      if (preserveState && expandedIds.Count > 0)
      {
        foreach (var id in expandedIds)
        {
          if (entityNodesById.TryGetValue(id, out var node))
          {
            node.Expanded = true;
          }
        }
      }

      // Restore selection
      if (preserveState && selectedId.HasValue &&
          entityNodesById.TryGetValue(selectedId.Value, out var selectedNodeNew))
      {
        treeView.SelectNode(selectedNodeNew);
      }

      // Restore scroll (after layout has been recomputed by UpdateNodes via node events)
      if (preserveState)
      {
        treeView.VerticalScrollValue = scrollValue;
      }
    }

    /// <summary>
    /// Builds a human-readable label for an entity (used internally if needed).
    /// </summary>
    private string GetEntityLabel(SceneEntity entity)
    {
      string baseName = !string.IsNullOrWhiteSpace(entity.TagName)
        ? entity.TagName
        : $"Entity {entity.Id}";

      return baseName;
    }

    /// <summary>
    /// Unity-like placement:
    /// SceneName
    ///   Entity 33
    ///     Transform
    ///     Material
    ///     Entity 32
    ///       Transform
    /// </summary>
    private void AddEntityHierarchy(SceneEntity entity, CrownTreeNode parentNode)
    {
      string label = GetEntityLabel(entity);

      var entityNode = new CrownTreeNode(label)
      {
        Tag = entity
      };
      parentNode.Nodes.Add(entityNode);

      entityNodesById[entity.Id] = entityNode;

      // Update component ownership + parent info so inspector can display it.
      foreach (var comp in entity.Components)
      {
        comp.OwnerEntityId = entity.Id;
        comp.OwnerParentId = entity.ParentId;
        comp.OwnerParentName = entity.ParentName;
      }

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
    }

  } // class HierarchyDock

} // namespace SwimEditor
