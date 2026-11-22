using System.IO;
using Knez.Game.View;
using NUnit.Framework;
using UnityEngine;

namespace Knez.Game.Tests
{
    /// <summary>
    /// The view builds and draws a battle, and renders it to <c>artifacts/unity/*.png</c> so the look can be checked
    /// without opening the editor (run batch mode without -nographics). EditMode: objects are made and destroyed here.
    /// </summary>
    public class BattleViewTests
    {
        [TestCase("scenarios/drill.json", 0)]
        [TestCase("scenarios/drill.json", 300)]
        [TestCase("scenarios/walls.json", 400)]
        [TestCase("scenarios/bench/s1-clash.json", 500)]
        public void Draw_RendersTheBattle(string scenario, int ticks)
        {
            var root = new GameObject("Test battle");
            var camObject = new GameObject("Test camera");
            try
            {
                var cam = camObject.AddComponent<Camera>();
                cam.aspect = 16f / 9f;
                var runner = root.AddComponent<BattleRunner>();
                runner.Load(ContentFiles.LoadScenario(ContentFiles.DefaultRoot(), scenario), 1);
                var view = root.AddComponent<BattleView>();
                view.Bind(runner, cam);
                view.Build();
                runner.Step(ticks);
                view.Draw(runner.Simulation!.State, 1f);

                int fighters = runner.Simulation.State.Fighters.Count;
                Assert.That(root.GetComponentsInChildren<SpriteRenderer>().Length, Is.GreaterThan(fighters), "a disc per fighter, plus walls and ground");
                if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                {
                    return; // -nographics: nothing to render
                }

                var target = new RenderTexture(1280, 720, 24);
                cam.targetTexture = target;
                cam.Render();
                RenderTexture.active = target;
                var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                image.Apply();
                RenderTexture.active = null;
                cam.targetTexture = null;
                string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "artifacts", "unity"));
                Directory.CreateDirectory(folder);
                string name = Path.GetFileNameWithoutExtension(scenario) + "-" + ticks + ".png";
                File.WriteAllBytes(Path.Combine(folder, name), image.EncodeToPNG());
                Object.DestroyImmediate(image);
                Object.DestroyImmediate(target);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(camObject);
            }
        }
    }
}
