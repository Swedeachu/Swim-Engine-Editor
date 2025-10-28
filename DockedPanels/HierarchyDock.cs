using System;
using System.Drawing;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;

namespace SwimEditor
{

  public class HierarchyDock : DockContent
  {

    private DarkTreeView treeView;

    public event Action<object> OnSelectionChanged;

    public HierarchyDock()
    {
      // Dark theme needs explicit colors
      treeView = new DarkTreeView
      {
        Dock = DockStyle.Fill,
        HideSelection = false,
        BorderStyle = BorderStyle.None,
        BackColor = SwimEditorTheme.PageBg,
        ForeColor = Color.Gainsboro
      };

      treeView.AfterSelect += (s, e) =>
      {
        OnSelectionChanged?.Invoke(e.Node);
      };

      Controls.Add(treeView);

      // Populate with a large fake scene for testing
      PopulateFakeScene(objectCount: 50, maxComponentsPerObject: 8, randomSeed: 1337);

      // Expand the root for immediate visual verification
      if (treeView.Nodes.Count > 0)
        treeView.Nodes[0].Expand();
    }

    /// <summary>
    /// Creates a big fake hierarchy to test DarkTreeView scrolling and layout.
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

      treeView.BeginUpdate();
      try
      {
        treeView.Nodes.Clear();

        var root = treeView.Nodes.Add("Scene (Test World)");
        // Add a few fixed systems up top
        var systems = root.Nodes.Add("Systems");
        systems.Nodes.Add("RenderSystem");
        systems.Nodes.Add("PhysicsSystem");
        systems.Nodes.Add("AudioSystem");
        systems.Nodes.Add("UISystem");

        // Cameras / lights
        var cameras = root.Nodes.Add("Cameras");
        cameras.Nodes.Add("Main Camera");
        cameras.Nodes.Add("UI Camera");

        var lights = root.Nodes.Add("Lights");
        lights.Nodes.Add("Directional Light");
        lights.Nodes.Add("Fill Light");
        lights.Nodes.Add("Rim Light");

        // Massive batch of objects
        var objectsRoot = root.Nodes.Add("GameObjects");

        for (int i = 0; i < objectCount; i++)
        {
          // Mix in some long/varied names to force horizontal scroll when needed
          string longBit = (i % 9 == 0)
            ? $"_{longNameTokens[rnd.Next(longNameTokens.Length)]}_{longNameTokens[rnd.Next(longNameTokens.Length)]}_ID{i:0000}"
            : $"_{i:0000}";

          var go = objectsRoot.Nodes.Add("GameObject" + longBit);

          // Always include Transform
          go.Nodes.Add("Transform");

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

            go.Nodes.Add(comp);
          }

          // Some nested children to test deeper trees
          if (i % 7 == 0)
          {
            var childA = go.Nodes.Add("Child_A" + (i % 100));
            childA.Nodes.Add("Transform");
            childA.Nodes.Add("MeshRenderer");
            childA.Nodes.Add("BoxCollider");

            if (i % 14 == 0)
            {
              var grand = childA.Nodes.Add("GrandChild_X" + (i % 37));
              grand.Nodes.Add("Transform");
              grand.Nodes.Add("ParticleSystem");

              if (i % 28 == 0)
              {
                var gg = grand.Nodes.Add("GreatGrandChild_" + (i % 11));
                gg.Nodes.Add("Transform");
                gg.Nodes.Add("Script<AIController>");
              }
            }

            var childB = go.Nodes.Add("Child_B" + (i % 33));
            childB.Nodes.Add("Transform");
            childB.Nodes.Add("RectTransform");
            childB.Nodes.Add("Image");
            childB.Nodes.Add("Button");
          }

          // Expand a subset for visual diversity
          if (i < 5 || (i % 25 == 0))
            go.Expand();
        }

        // Expand upper groups for immediate content without fully opening everything
        root.Expand();
        systems.Expand();
        cameras.Expand();
        lights.Expand();
        objectsRoot.Expand();
      }
      finally
      {
        treeView.EndUpdate();
      }
    }

  } // class HierarchyDock

} // Namespace SwimEditor
