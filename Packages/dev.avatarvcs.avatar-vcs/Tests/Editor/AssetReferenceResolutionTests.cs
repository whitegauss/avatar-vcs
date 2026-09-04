using System.Linq;
using AvatarVcs.Editor.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AvatarVcs.Tests.Editor
{
    /// <summary>
    /// ReferenceResolver.ResolveAsset has to tell sub-assets apart by localId
    /// (several materials inside one FBX, an AnimatorController's states), but
    /// the overwhelmingly common recorded reference is the file's main asset.
    /// These pin both halves so the main-asset fast path can't quietly break
    /// sub-asset resolution.
    /// </summary>
    public class AssetReferenceResolutionTests
    {
        private const string Dir = "Assets/AvatarVcsTests_AssetRef_Temp";
        private const string ContainerPath = Dir + "/WithSubAssets.asset";

        private Material mainAsset;
        private Material subAsset;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            if (!AssetDatabase.IsValidFolder(Dir))
                AssetDatabase.CreateFolder("Assets", "AvatarVcsTests_AssetRef_Temp");

            mainAsset = new Material(Shader.Find("Standard")) { name = "Main" };
            AssetDatabase.CreateAsset(mainAsset, ContainerPath);

            subAsset = new Material(Shader.Find("Standard")) { name = "Sub" };
            AssetDatabase.AddObjectToAsset(subAsset, ContainerPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(ContainerPath, ImportAssetOptions.ForceUpdate);

            mainAsset = AssetDatabase.LoadAssetAtPath<Material>(ContainerPath);
            subAsset = AssetDatabase.LoadAllAssetsAtPath(ContainerPath)
                .OfType<Material>()
                .Single(m => m != mainAsset);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            if (AssetDatabase.IsValidFolder(Dir)) AssetDatabase.DeleteAsset(Dir);
        }

        private static (string guid, long localId) IdOf(Object o)
        {
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(o, out var guid, out long localId);
            return (guid, localId);
        }

        [Test]
        public void ResolveAsset_FindsTheMainAsset()
        {
            var (guid, localId) = IdOf(mainAsset);

            Assert.AreSame(mainAsset, ReferenceResolver.ResolveAsset(guid, localId));
        }

        [Test]
        public void ResolveAsset_StillFindsASubAssetSharingTheSameGuid()
        {
            var (guid, subLocalId) = IdOf(subAsset);
            var (mainGuid, mainLocalId) = IdOf(mainAsset);

            Assert.AreEqual(mainGuid, guid, "sanity check: sub-asset shares the file's GUID");
            Assert.AreNotEqual(mainLocalId, subLocalId, "sanity check: only the localId tells them apart");

            Assert.AreSame(subAsset, ReferenceResolver.ResolveAsset(guid, subLocalId),
                "the main-asset fast path must not shadow sub-asset resolution");
        }

        [Test]
        public void ResolveAsset_ReturnsNullForALocalIdThatIsNotInTheFile()
        {
            var (guid, _) = IdOf(mainAsset);

            Assert.IsNull(ReferenceResolver.ResolveAsset(guid, -6470530889692970645L));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("ffffffffffffffffffffffffffffffff")] // well-formed but unknown
        public void ResolveAsset_ReturnsNullForAnUnresolvableGuid(string guid)
        {
            Assert.IsNull(ReferenceResolver.ResolveAsset(guid, 0L));
        }
    }
}
