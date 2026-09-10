using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace ObjecTalk.Tests.EditMode
{
    /// <summary>
    /// The Android player settings the recognition pipeline needs: API 29 minimum, ARM64-only,
    /// IL2CPP, Vulkan-first with an OpenGL ES 3 fallback, multithreaded rendering, and managed
    /// stripping left on (so the settings, not a later accident, define the shipping surface).
    /// </summary>
    [TestFixture]
    public sealed class AndroidRecognitionBuildSettingsTests
    {
        private static readonly NamedBuildTarget Android = NamedBuildTarget.Android;

        [Test]
        public void MinimumApiLevelIsAtLeast29()
        {
            Assert.That((int)PlayerSettings.Android.minSdkVersion, Is.GreaterThanOrEqualTo(29));
        }

        [Test]
        public void TargetApiLevelIsNotPinnedBelow29()
        {
            var target = PlayerSettings.Android.targetSdkVersion;
            // 0 == "Automatic (highest installed)", which is what we want; any explicit pin must be >= 29.
            Assert.That(target == AndroidSdkVersions.AndroidApiLevelAuto || (int)target >= 29,
                $"targetSdkVersion is pinned to {target}; use Automatic or API 29+.");
        }

        [Test]
        public void BuildsArm64Only()
        {
            // ARM64 is forced by com.unity.webrtc, whose libwebrtc.aar carries arm64-v8a and
            // x86_64 only; an ARMv7 player dies with DllNotFoundException: webrtc and loses the
            // realtime voice conversation. OpenCV+Unity ships the opposite pair (armeabi-v7a and
            // x86), so AKAZE cannot run here either - Android installs one ABI per APK, and voice
            // is the feature that has to win. See NullLocalFeatureVerifier for the fallback.
            Assert.That(PlayerSettings.Android.targetArchitectures, Is.EqualTo(AndroidArchitecture.ARM64));
        }

        [Test]
        public void UsesIl2Cpp()
        {
            Assert.That(PlayerSettings.GetScriptingBackend(Android), Is.EqualTo(ScriptingImplementation.IL2CPP));
        }

        [Test]
        public void PrefersVulkanWithOpenGlEs3Fallback()
        {
            var apis = PlayerSettings.GetGraphicsAPIs(BuildTarget.Android);
            Assert.That(apis, Is.EqualTo(new[] { GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLES3 }));
        }

        [Test]
        public void UsesMultithreadedRendering()
        {
            Assert.That(PlayerSettings.GetMobileMTRendering(Android), Is.True);
        }

        [Test]
        public void ManagedStrippingIsNotDisabled()
        {
            Assert.That(PlayerSettings.GetManagedStrippingLevel(Android),
                Is.Not.EqualTo(ManagedStrippingLevel.Disabled));
        }

        [Test]
        public void MainManifestOverridesAllowBackupSoTheSqliteAarDoesNotBreakTheMerge()
        {
            // sqlite-android-*.aar declares android:allowBackup="true"; the app manifest sets it to
            // false. Without tools:replace the Gradle manifest merger fails the build.
            var manifest = Path.Combine(Application.dataPath, "Plugins", "Android", "AndroidManifest.xml");
            Assert.That(File.Exists(manifest), $"{manifest} is missing.");

            var xml = File.ReadAllText(manifest);
            Assert.That(xml, Does.Contain("tools:replace=\"android:allowBackup\""),
                "The <application> element must keep tools:replace=\"android:allowBackup\".");
        }
    }
}
