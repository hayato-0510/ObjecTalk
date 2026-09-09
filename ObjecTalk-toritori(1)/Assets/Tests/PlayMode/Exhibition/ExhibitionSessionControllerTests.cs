using System;
using System.Collections;
using NUnit.Framework;
using ObjecTalk.Core.Exhibition;
using ObjecTalk.Presentation.Exhibition;
using UnityEngine;
using UnityEngine.TestTools;

namespace ObjecTalk.Tests.PlayMode.Exhibition
{
    public sealed class ExhibitionSessionControllerTests
    {
        private static ExhibitionIdlePolicy Policy() =>
            new ExhibitionIdlePolicy(TimeSpan.FromSeconds(45), TimeSpan.FromSeconds(30));

        [UnityTest]
        public IEnumerator ShowsAttractOverlay_WhenPolicyReportsAttract()
        {
            var now = new DateTimeOffset(2026, 9, 10, 9, 0, 0, TimeSpan.Zero);
            var overlay = new GameObject("Attract");
            var resetCalls = 0;

            var host = new GameObject("ExhibitionSessionController");
            var controller = host.AddComponent<ExhibitionSessionController>();
            controller.InitializeForTests(Policy(), overlay, () => resetCalls++, () => now);

            controller.NotifyActivity();
            yield return null;
            Assert.That(overlay.activeSelf, Is.False, "操作直後はアトラクトを隠す");

            now += TimeSpan.FromSeconds(46);
            yield return null;
            Assert.That(overlay.activeSelf, Is.True, "無操作 45 秒超でアトラクト表示");
            Assert.That(resetCalls, Is.Zero);

            now += TimeSpan.FromSeconds(30);
            yield return null;
            Assert.That(resetCalls, Is.EqualTo(1), "さらに 30 秒でリセット");

            UnityEngine.Object.Destroy(host);
            UnityEngine.Object.Destroy(overlay);
        }

        [UnityTest]
        public IEnumerator ResetIsCalledOnce_EvenIfPolicyKeepsReportingReset()
        {
            var now = new DateTimeOffset(2026, 9, 10, 9, 0, 0, TimeSpan.Zero);
            var overlay = new GameObject("Attract");
            var resetCalls = 0;

            var host = new GameObject("ExhibitionSessionController");
            var controller = host.AddComponent<ExhibitionSessionController>();
            controller.InitializeForTests(Policy(), overlay, () => resetCalls++, () => now);

            controller.NotifyActivity();
            now += TimeSpan.FromSeconds(120);
            yield return null;
            yield return null;
            yield return null;

            Assert.That(resetCalls, Is.EqualTo(1));

            UnityEngine.Object.Destroy(host);
            UnityEngine.Object.Destroy(overlay);
        }
    }
}
