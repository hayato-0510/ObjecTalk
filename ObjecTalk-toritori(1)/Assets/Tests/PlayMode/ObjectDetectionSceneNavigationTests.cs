using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace ObjecTalk.Tests.PlayMode
{
    /// <summary>
    /// End-to-end for every button in ObjectDetectionScene: which screen it moves to, and that each
    /// one is wired through a single path (no leftover serialized OnClick, no dead Figma flow
    /// component).
    ///
    /// These drive the real scene rather than the controllers, which live in Assembly-CSharp and
    /// cannot be referenced from a test assembly. Assertions are on GameObject state, active scene,
    /// and serialized wiring. Cases that must reach a camera panel wait for it and self-skip when no
    /// webcam is present.
    /// </summary>
    public sealed class ObjectDetectionSceneNavigationTests
    {
        private const string SceneName = "ObjectDetectionScene";
        private const string HomeSceneName = "Myscenes";

        private const string Selection = "TalkSelectionPanel";
        private const string CameraRoot = "MatcherRootPanel";
        private const string RegisterCamera = "MatcherRootPanel/RegisterCameraPanel";
        private const string TalkCamera = "MatcherRootPanel/TalkCameraPanel";

        private Transform _canvas;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            yield return SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Single);
            yield return null;
            yield return null;
            _canvas = GameObject.Find("Canvas").transform;
        }

        [UnityTearDown]
        public IEnumerator DropPersistentObjects()
        {
            // Some tests navigate into Myscenes, whose managers log errors while tearing down here;
            // that noise is unrelated to what these tests assert. [UnitySetUp] reloads the scene.
            LogAssert.ignoreFailingMessages = true;

            foreach (var go in Object.FindObjectsByType<GameObject>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (go != null && go.scene.name == "DontDestroyOnLoad")
                {
                    Object.Destroy(go);
                }
            }

            yield return null;
        }

        // --- entry: selection panel ------------------------------------------------------------

        [Test]
        public void SceneOpensOnTheSelectionPanelWithTheCameraPanelHidden()
        {
            Assert.That(Panel(Selection).activeInHierarchy, Is.True);
            Assert.That(Panel(CameraRoot).activeSelf, Is.False);
        }

        [UnityTest]
        public IEnumerator RegisterButtonHandsOffFromSelectionToCameraRoot()
        {
            Click(Selection + "/Button(reg)");
            yield return null;

            Assert.That(Panel(Selection).activeSelf, Is.False);
            Assert.That(Panel(CameraRoot).activeInHierarchy, Is.True);
        }

        [UnityTest]
        public IEnumerator TalkButtonHandsOffFromSelectionToCameraRoot()
        {
            Click(Selection + "/Button(talk)");
            yield return null;

            Assert.That(Panel(Selection).activeSelf, Is.False);
            Assert.That(Panel(CameraRoot).activeInHierarchy, Is.True);
        }

        [UnityTest]
        public IEnumerator SelectionBackButtonReturnsToHome()
        {
            // Myscenes ships a TMP font asset ("NotoSansJP-Medium SDF") that fails to deserialize
            // in this project; that error is unrelated to the navigation under test.
            LogAssert.ignoreFailingMessages = true;

            Click(Selection + "/BackButton");
            yield return WaitForScene(HomeSceneName);

            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(HomeSceneName));
        }

        // --- routing into the camera panels --------------------------------------------------

        [UnityTest]
        public IEnumerator RegisterButtonRoutesToTheRegistrationCameraPanel()
        {
            RequireWebcam();
            Click(Selection + "/Button(reg)");

            yield return WaitForActive(RegisterCamera);

            Assert.That(Panel(RegisterCamera).activeInHierarchy, Is.True);
            Assert.That(Panel(TalkCamera).activeSelf, Is.False);
        }

        [UnityTest]
        public IEnumerator TalkButtonRoutesToTheTalkCameraPanel()
        {
            RequireWebcam();
            Click(Selection + "/Button(talk)");

            yield return WaitForActive(TalkCamera);

            Assert.That(Panel(TalkCamera).activeInHierarchy, Is.True);
            Assert.That(Panel(RegisterCamera).activeSelf, Is.False);
        }

        // --- back navigation out of the camera panels ----------------------------------------

        [UnityTest]
        public IEnumerator RegisterCameraBackReturnsToTheSelectionPanel()
        {
            RequireWebcam();
            Click(Selection + "/Button(reg)");
            yield return WaitForActive(RegisterCamera);

            Click(RegisterCamera + "/BackButton");
            yield return null;

            Assert.That(Panel(Selection).activeInHierarchy, Is.True,
                "Back from the register camera must reopen the selection panel, not leave a blank screen.");
            Assert.That(Panel(CameraRoot).activeSelf, Is.False);
        }

        [UnityTest]
        public IEnumerator TalkCameraBackReturnsToTheSelectionPanel()
        {
            RequireWebcam();
            Click(Selection + "/Button(talk)");
            yield return WaitForActive(TalkCamera);

            Click(TalkCamera + "/BackButton");
            yield return null;

            Assert.That(Panel(Selection).activeInHierarchy, Is.True);
            Assert.That(Panel(CameraRoot).activeSelf, Is.False);
        }

        [UnityTest]
        public IEnumerator FinishButtonReturnsToHome()
        {
            LogAssert.ignoreFailingMessages = true; // see SelectionBackButtonReturnsToHome
            RequireWebcam();
            Click(Selection + "/Button(talk)");
            yield return WaitForActive(TalkCamera);

            Click(TalkCamera + "/Button(fin)");
            yield return WaitForScene(HomeSceneName);

            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(HomeSceneName));
        }

        // --- wiring hygiene ------------------------------------------------------------------

        [Test]
        public void NoNavigationButtonKeepsASerializedOnClick()
        {
            string[] paths =
            {
                Selection + "/BackButton",
                RegisterCamera + "/BackButton",
                "MatcherRootPanel/PlacementPanel/BackButton",
                "MatcherRootPanel/NamingPanel/BackButton",
                TalkCamera + "/BackButton",
                TalkCamera + "/Button(fin)",
            };

            foreach (var path in paths)
            {
                Assert.That(ButtonAt(path).onClick.GetPersistentEventCount(), Is.Zero,
                    $"{path} must be wired from code only.");
            }
        }

        [Test]
        public void ActionButtonsHaveNoDeadFigmaFlowComponent()
        {
            string[] paths =
            {
                RegisterCamera + "/Button(regstart)",
                "MatcherRootPanel/PlacementPanel/Button(next)",
                "MatcherRootPanel/NamingPanel/Button(complete)",
                TalkCamera + "/Button(fin)",
                Selection + "/Button(reg)",
                Selection + "/Button(talk)",
            };

            foreach (var path in paths)
            {
                foreach (var c in Panel(path).GetComponents<Component>())
                {
                    Assert.That(c == null ? "" : c.GetType().Name, Is.Not.EqualTo("FigmaPrototypeFlowButton"),
                        $"{path} still carries a dead FigmaPrototypeFlowButton.");
                }
            }
        }

        // --- helpers ----------------------------------------------------------------------------

        private static void RequireWebcam()
        {
            if (WebCamTexture.devices.Length == 0)
            {
                Assert.Ignore("No webcam device; this route needs a live camera panel.");
            }
        }

        private GameObject Panel(string relativePath)
        {
            var t = _canvas.Find(relativePath);
            Assert.That(t, Is.Not.Null, $"'{relativePath}' not found under Canvas.");
            return t.gameObject;
        }

        private Button ButtonAt(string relativePath)
        {
            var b = Panel(relativePath).GetComponent<Button>();
            Assert.That(b, Is.Not.Null, $"'{relativePath}' has no Button.");
            return b;
        }

        private void Click(string relativePath)
        {
            ButtonAt(relativePath).onClick.Invoke();
        }

        private IEnumerator WaitForActive(string relativePath)
        {
            yield return WaitUntil(() =>
            {
                var t = _canvas.Find(relativePath);
                return t != null && t.gameObject.activeInHierarchy;
            }, $"'{relativePath}' did not become active");
        }

        private static IEnumerator WaitForScene(string sceneName)
        {
            yield return WaitUntil(() => SceneManager.GetActiveScene().name == sceneName,
                $"scene '{sceneName}' did not become active");
        }

        private static IEnumerator WaitUntil(System.Func<bool> condition, string failMessage)
        {
            const float timeoutSeconds = 25f;
            var deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (condition())
                {
                    yield break;
                }

                yield return null;
            }

            Assert.Fail($"{failMessage} within {timeoutSeconds}s.");
        }
    }
}
