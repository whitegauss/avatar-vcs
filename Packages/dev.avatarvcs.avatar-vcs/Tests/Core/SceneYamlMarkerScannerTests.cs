using System.Linq;
using AvatarVcs.Core.Repair;
using NUnit.Framework;

namespace AvatarVcs.Tests.Core
{
    [Category("Core")]
    public class SceneYamlMarkerScannerTests
    {
        // Copied out of a real project's scene (the one the .meta bug was
        // reported from), with the surrounding objects trimmed. The script
        // guids are the ones that install generated for AvatarVcsContainer
        // and AvatarVcsRoot; note m_EditorClassIdentifier is empty, which is
        // why the class can't be read off the block and the guid fields are
        // the only thing identifying these two.
        private const string RealSceneExcerpt = @"%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!1 &443647171
GameObject:
  m_ObjectHideFlags: 0
  m_Name: NO_01
--- !u!114 &443647173
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 443647171}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {fileID: 11500000, guid: 9b134eb6dcb359a4fa0a92ce61605055, type: 3}
  m_Name: 
  m_EditorClassIdentifier: 
  containerGuid: 963c1238362b4c06bd058cd6c20a9dd7
--- !u!4 &793325742
Transform:
  m_GameObject: {fileID: 793325741}
--- !u!114 &793325743
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 793325741}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {fileID: 11500000, guid: 7452257372a7ab246a08439006804c43, type: 3}
  m_Name: 
  m_EditorClassIdentifier: 
  avatarGuid: c36f7fc6e24a4b0aa85ecb0ef6b728c5
--- !u!114 &1685270829
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 1685270827}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {fileID: 11500000, guid: cedd2c4154039cc4d885355c3dbbd497, type: 3}
  m_Name: 
  m_EditorClassIdentifier: 
";

        [Test]
        public void ReadsTheContainerGuidOffItsBlock()
        {
            var container = Single(RealSceneExcerpt, "9b134eb6dcb359a4fa0a92ce61605055");

            Assert.That(container.containerGuid, Is.EqualTo("963c1238362b4c06bd058cd6c20a9dd7"));
            Assert.That(container.avatarGuid, Is.Null);
            Assert.That(container.gameObjectFileId, Is.EqualTo(443647171));
            Assert.That(container.IsDataless, Is.False);
        }

        [Test]
        public void ReadsTheAvatarGuidOffItsBlock()
        {
            var root = Single(RealSceneExcerpt, "7452257372a7ab246a08439006804c43");

            Assert.That(root.avatarGuid, Is.EqualTo("c36f7fc6e24a4b0aa85ecb0ef6b728c5"));
            Assert.That(root.containerGuid, Is.Null);
            Assert.That(root.gameObjectFileId, Is.EqualTo(793325741));
        }

        // AvatarVcsTrackedReference and AvatarVcsUntracked serialize no fields
        // at all, so the block is still found -- with nothing on it to say
        // which of the two it is. MarkerGuidClassifier decides that part.
        [Test]
        public void FieldLessMarkersAreFoundButCarryNoGuid()
        {
            var dataless = Single(RealSceneExcerpt, "cedd2c4154039cc4d885355c3dbbd497");

            Assert.That(dataless.IsDataless, Is.True);
            Assert.That(dataless.gameObjectFileId, Is.EqualTo(1685270827));
        }

        [Test]
        public void OnlyMonoBehaviourDocumentsAreReturned()
        {
            var blocks = SceneYamlMarkerScanner.ReadMonoBehaviours(RealSceneExcerpt);

            // The GameObject and Transform documents in the excerpt are not
            // components with scripts and must not appear.
            Assert.That(blocks.Count, Is.EqualTo(3));
        }

        // A component from any other package is free to have a field called
        // avatarGuid. Nothing shaped unlike our guids may be read as one --
        // it ends up in a filesystem path (CommitPaths).
        [Test]
        public void AFieldThatIsNotGuidShapedIsIgnored()
        {
            const string yaml = @"--- !u!114 &11
MonoBehaviour:
  m_GameObject: {fileID: 10}
  m_Script: {fileID: 11500000, guid: 7452257372a7ab246a08439006804c43, type: 3}
  avatarGuid: ../../../etc/passwd
";
            var block = SceneYamlMarkerScanner.ReadMonoBehaviours(yaml).Single();

            Assert.That(block.avatarGuid, Is.Null);
            Assert.That(block.IsDataless, Is.True);
        }

        [Test]
        public void AMonoBehaviourWithNoScriptLineIsSkipped()
        {
            const string yaml = @"--- !u!114 &11
MonoBehaviour:
  m_GameObject: {fileID: 10}
  m_Enabled: 1
";
            Assert.That(SceneYamlMarkerScanner.ReadMonoBehaviours(yaml), Is.Empty);
        }

        [Test]
        public void EmptyInputIsNotAnError()
        {
            Assert.That(SceneYamlMarkerScanner.ReadMonoBehaviours(null), Is.Empty);
            Assert.That(SceneYamlMarkerScanner.ReadMonoBehaviours(string.Empty), Is.Empty);
        }

        private static SceneMarkerBlock Single(string yaml, string scriptGuid) =>
            SceneYamlMarkerScanner.ReadMonoBehaviours(yaml).Single(b => b.scriptGuid == scriptGuid);
    }
}
