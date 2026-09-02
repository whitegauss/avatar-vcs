using System.Collections.Generic;
using System.Diagnostics;
using AvatarVcs.Editor.Core;
using AvatarVcs.Editor.History;
using NUnit.Framework;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace AvatarVcs.Tests.Editor
{
    /// <summary>
    /// KAN-14: a repeatable harness for "how big / how slow is one commit of
    /// a large default-config avatar". [Explicit] -- it never runs in a
    /// normal suite pass; invoke it by name from the Test Runner.
    ///
    /// It builds a SYNTHETIC hierarchy (no VRChat SDK available here, so no
    /// real PhysBone/Contact/Constraint mass), so the numbers are indicative
    /// only, not a substitute for profiling an actual VRChat avatar. Covers
    /// ticket items 1 (committed JSON byte size) and 2 (Commit() wall time).
    /// Item 3 -- the per-Inspector-drag RecomputeSelectedDiff frame cost with
    /// the AvatarVCS window open -- needs interactive profiling and is not
    /// covered here.
    /// </summary>
    public class CommitPerformanceBenchmark
    {
        private readonly List<string> avatarGuids = new();
        private readonly List<GameObject> spawned = new();
        private readonly List<Object> assets = new();

        private class ManyFieldComponent : MonoBehaviour
        {
            public float a, b, c;
            public Vector3 v1, v2;
            public Color color = Color.white;
            public string label;
            public bool flag1, flag2;
            public int i1, i2, i3;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var guid in avatarGuids) CommitStore.DeleteAvatarHistory(guid);
            avatarGuids.Clear();
            foreach (var go in spawned) if (go != null) Object.DestroyImmediate(go);
            spawned.Clear();
            foreach (var a in assets) if (a != null) Object.DestroyImmediate(a);
            assets.Clear();
        }

        [Explicit("Perf harness -- run manually from the Test Runner; see class docs (KAN-14)")]
        [TestCase(200)]
        [TestCase(1000)]
        [TestCase(3000)]
        public void Commit_LargeSyntheticAvatar_ReportsSizeAndTime(int transformCount)
        {
            var avatar = BuildSyntheticAvatar(transformCount);
            spawned.Add(avatar);

            ContainerManager.EnsureRootWithDefaults(avatar); // default config: avatar root is tracked
            var avatarGuid = ContainerManager.GetAvatarGuid(avatar);
            avatarGuids.Add(avatarGuid);

            BranchManager.Commit(avatar, "warmup"); // first commit also creates the root guid etc.

            const int runs = 5;
            var times = new List<double>();
            long jsonBytes = 0;
            var nudge = avatar.GetComponentInChildren<ManyFieldComponent>();

            for (var i = 0; i < runs; i++)
            {
                if (nudge != null) nudge.a = i + 1; // make each commit a real, differing snapshot

                var sw = Stopwatch.StartNew();
                var commit = BranchManager.Commit(avatar, $"bench {i}");
                sw.Stop();
                times.Add(sw.Elapsed.TotalMilliseconds);

                jsonBytes = new System.IO.FileInfo(
                    $"{CommitStore.GetAvatarDir(avatarGuid)}/commits/{commit.commitId}.json").Length;
            }

            times.Sort();
            Debug.Log($"[KAN-14] transforms={transformCount}  commitJSON={jsonBytes / 1024.0:F1} KiB  "
                + $"Commit() median={times[runs / 2]:F1} ms  (min={times[0]:F1} max={times[^1]:F1}, n={runs})");

            Assert.Greater(jsonBytes, 0, "sanity: a commit file was written");
        }

        private GameObject BuildSyntheticAvatar(int transformCount)
        {
            var root = new GameObject("BenchAvatar");
            var body = new GameObject("Body");
            body.transform.SetParent(root.transform, false);

            var mesh = new Mesh { name = "BenchMesh" };
            assets.Add(mesh);
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2 };
            for (var i = 0; i < 40; i++)
                mesh.AddBlendShapeFrame($"shape_{i}", 100f,
                    new[] { Vector3.zero, Vector3.zero, Vector3.zero }, null, null);
            body.AddComponent<SkinnedMeshRenderer>().sharedMesh = mesh;

            // Fan out an Armature-like tree, a component on every node.
            var made = 2; // root + Body
            var frontier = new Queue<Transform>();
            frontier.Enqueue(body.transform);
            while (made < transformCount)
            {
                var parent = frontier.Count > 0 ? frontier.Dequeue() : body.transform;
                for (var k = 0; k < 4 && made < transformCount; k++)
                {
                    var child = new GameObject($"node_{made}");
                    child.transform.SetParent(parent, false);
                    child.transform.localPosition = new Vector3(0.01f * k, 0.02f, 0.03f);
                    child.AddComponent<ManyFieldComponent>().label = $"n{made}";
                    frontier.Enqueue(child.transform);
                    made++;
                }
            }
            return root;
        }
    }
}
