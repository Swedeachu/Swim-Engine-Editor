using System;
using System.Linq;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;
using ReaLTaiizor.Controls;

namespace SwimEditor
{

  public class HierarchyDock : DockContent
  {

    private CrownTreeView treeView;

    public event Action<object> OnSelectionChanged;

    public HierarchyDock()
    {
      // Dark theme with CrownTreeView (ReaLTaiizor) replacing DarkTreeView
      treeView = new CrownTreeView
      {
        Dock = DockStyle.Fill
      };

      // Selection change uses SelectedNodesChanged, not AfterSelect
      treeView.SelectedNodesChanged += (s, e) =>
      {
        var node = treeView.SelectedNodes.LastOrDefault();
        if (node != null)
        {
          OnSelectionChanged?.Invoke(node);
        }
      };

      Controls.Add(treeView);

      // Populate with a large fake scene for testing
      PopulateFakeScene(objectCount: 50, maxComponentsPerObject: 8, randomSeed: 1337);

      // Expand the root for immediate visual verification
      if (treeView.Nodes.Count > 0)
      {
        treeView.Nodes[0].Expanded = true;
      }
    }

    /// <summary>
    /// Creates a big fake hierarchy to test CrownTreeView scrolling and layout.
    /// - Many root-level and nested objects
    /// - Variable component counts
    /// - Some long names to exercise horizontal scrolling
    /// </summary>
    private void PopulateFakeScene(int objectCount, int maxComponentsPerObject, int randomSeed = 0)
    {
      var rnd = (randomSeed == 0) ? new Random() : new Random(randomSeed);

      string[] componentPool =
      {
        "Transform",
        "MeshRenderer",
        "MeshFilter",
        "SkinnedMeshRenderer",
        "BoxCollider",
        "SphereCollider",
        "CapsuleCollider",
        "Rigidbody",
        "AudioSource",
        "AudioListener",
        "Animator",
        "ParticleSystem",
        "Light",
        "Camera",
        "NavMeshAgent",
        "Script<RotateBehavior>",
        "Script<FollowTarget>",
        "Script<AIController>",
        "Canvas",
        "RectTransform",
        "Image",
        "Text",
        "Button",
        "RawImage",
        "EventSystem"
      };

      string[] longNameTokens =
      {
        "Ultra", "Hyper", "Mega", "Giga", "Quantum", "Neon", "Retro", "Voxel",
        "Procedural", "PBR", "Instanced", "Deferred", "Clustered", "Occlusion",
        "Volumetric", "Holographic", "Spline", "Bezier", "Anisotropic", "Temporal"
      };

      treeView.Nodes.Clear();

      var root = new CrownTreeNode("Scene (Test World)");
      treeView.Nodes.Add(root);

      // Add a few fixed systems up top
      var systems = new CrownTreeNode("Systems");
      systems.Nodes.Add(new CrownTreeNode("RenderSystem"));
      systems.Nodes.Add(new CrownTreeNode("PhysicsSystem"));
      systems.Nodes.Add(new CrownTreeNode("AudioSystem"));
      systems.Nodes.Add(new CrownTreeNode("UISystem"));
      root.Nodes.Add(systems);

      // Cameras / lights
      var cameras = new CrownTreeNode("Cameras");
      cameras.Nodes.Add(new CrownTreeNode("Main Camera"));
      cameras.Nodes.Add(new CrownTreeNode("UI Camera"));
      root.Nodes.Add(cameras);

      var lights = new CrownTreeNode("Lights");
      lights.Nodes.Add(new CrownTreeNode("Directional Light"));
      lights.Nodes.Add(new CrownTreeNode("Fill Light"));
      lights.Nodes.Add(new CrownTreeNode("Rim Light"));
      root.Nodes.Add(lights);

      // Massive batch of objects
      var objectsRoot = new CrownTreeNode("GameObjects");
      root.Nodes.Add(objectsRoot);

      for (int i = 0; i < objectCount; i++)
      {
        // Mix in some long/varied names to force horizontal scroll when needed
        string longBit = (i % 9 == 0)
          ? $"_{longNameTokens[rnd.Next(longNameTokens.Length)]}_{longNameTokens[rnd.Next(longNameTokens.Length)]}_ID{i:0000}"
          : $"_{i:0000}";

        var go = new CrownTreeNode("GameObject" + longBit);
        objectsRoot.Nodes.Add(go);

        // Always include Transform
        go.Nodes.Add(new CrownTreeNode("Transform"));

        // Random number of other components
        int compCount = 1 + rnd.Next(Math.Max(1, maxComponentsPerObject));
        for (int c = 0; c < compCount; c++)
        {
          string comp = componentPool[rnd.Next(componentPool.Length)];

          // Occasionally create very long component names to test h-scroll
          if (rnd.NextDouble() < 0.08)
          {
            comp += $" [LOD={rnd.Next(0, 4)}] (Layer={rnd.Next(0, 32)}) <Tag=Test_{i % 7}>";
          }

          go.Nodes.Add(new CrownTreeNode(comp));
        }

        // Some nested children to test deeper trees
        if (i % 7 == 0)
        {
          var childA = new CrownTreeNode("Child_A" + (i % 100));
          childA.Nodes.Add(new CrownTreeNode("Transform"));
          childA.Nodes.Add(new CrownTreeNode("MeshRenderer"));
          childA.Nodes.Add(new CrownTreeNode("BoxCollider"));

          if (i % 14 == 0)
          {
            var grand = new CrownTreeNode("GrandChild_X" + (i % 37));
            grand.Nodes.Add(new CrownTreeNode("Transform"));
            grand.Nodes.Add(new CrownTreeNode("ParticleSystem"));

            if (i % 28 == 0)
            {
              var gg = new CrownTreeNode("GreatGrandChild_" + (i % 11));
              gg.Nodes.Add(new CrownTreeNode("Transform"));
              gg.Nodes.Add(new CrownTreeNode("Script<AIController>"));
              grand.Nodes.Add(gg);
            }

            childA.Nodes.Add(grand);
          }

          var childB = new CrownTreeNode("Child_B" + (i % 33));
          childB.Nodes.Add(new CrownTreeNode("Transform"));
          childB.Nodes.Add(new CrownTreeNode("RectTransform"));
          childB.Nodes.Add(new CrownTreeNode("Image"));
          childB.Nodes.Add(new CrownTreeNode("Button"));

          go.Nodes.Add(childA);
          go.Nodes.Add(childB);
        }

        // Expand a subset for visual diversity
        if (i < 5 || (i % 25 == 0))
          go.Expanded = true;
      }

      // Expand upper groups for immediate content without fully opening everything
      root.Expanded = true;
      systems.Expanded = true;
      cameras.Expanded = true;
      lights.Expanded = true;
      objectsRoot.Expanded = true;
    }

  } // class HierarchyDock

} // Namespace SwimEditor
