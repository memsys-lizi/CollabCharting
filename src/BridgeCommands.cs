using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine.SceneManagement;

namespace CollabCharting
{
    internal static class BridgeCommands
    {
        public static object ListScenes(JToken? parameters)
        {
            Scene activeScene = SceneManager.GetActiveScene();
            var scenes = new List<object>();

            for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
            {
                string path = SceneUtility.GetScenePathByBuildIndex(i);
                string name = Path.GetFileNameWithoutExtension(path);

                scenes.Add(new
                {
                    buildIndex = i,
                    name,
                    path,
                    active = activeScene.buildIndex == i || activeScene.name == name
                });
            }

            return new
            {
                active = new
                {
                    buildIndex = activeScene.buildIndex,
                    name = activeScene.name,
                    path = activeScene.path
                },
                scenes
            };
        }

        public static object LoadScene(JToken? parameters)
        {
            int buildIndex = parameters?["buildIndex"]?.Value<int>() ?? -1;
            if (buildIndex < 0 || buildIndex >= SceneManager.sceneCountInBuildSettings)
            {
                throw new System.ArgumentOutOfRangeException(nameof(buildIndex), $"Invalid scene buildIndex: {buildIndex}");
            }

            string path = SceneUtility.GetScenePathByBuildIndex(buildIndex);
            string name = Path.GetFileNameWithoutExtension(path);
            SceneManager.LoadScene(buildIndex);

            return new
            {
                ok = true,
                buildIndex,
                name,
                path
            };
        }
    }
}
