using System.Collections;
using NUnit.Framework;
using ObjecTalk.Presentation.Exhibition;
using ObjecTalk.Presentation.Talk;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ObjecTalk.Tests.PlayMode.Exhibition
{
    public sealed class ObjectDetectionSceneExhibitionTests
    {
        [UnityTest]
        public IEnumerator Scene_HasExhibitionSessionController_AndPushToTalkFixed()
        {
            yield return SceneManager.LoadSceneAsync("ObjectDetectionScene", LoadSceneMode.Single);
            yield return null;
            yield return null;

            var controller = Object.FindAnyObjectByType<ExhibitionSessionController>(FindObjectsInactive.Include);
            Assert.That(controller, Is.Not.Null, "ExhibitionSessionController がシーンに無い");

            var config = Object.FindAnyObjectByType<TalkConfiguration>(FindObjectsInactive.Include);
            Assert.That(config, Is.Not.Null);
            Assert.That(config.HandsFree, Is.False, "展示は push-to-talk 固定（handsFree=false）");
            Assert.That(config.ExhibitionMode, Is.True, "exhibitionMode=true");
        }
    }
}
