// Experiment E4 diagnostics (batch mode: -executeMethod E4Diag.Run): are the avatars humanoid and valid, and does
// the Mixamo clip actually move the guitarist when played through the humanoid retargeting?

using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

public static class E4Diag
{
    public static void Run()
    {
        foreach (var path in new[] { "Assets/E4/Avatar/Export/Male_Adult_04.fbx", "Assets/E4/Mocap/guitar_playing_111490901.fbx" })
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            var avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
            Debug.Log($"E4D: {path}: type {importer.animationType}, avatar {(avatar != null ? avatar.name : "none")}, human {avatar?.isHuman}, valid {avatar?.isValid}");
            foreach (var c in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")))
                Debug.Log($"E4D:   clip '{c.name}': {c.length:F2} s, human motion {c.isHumanMotion}, legacy {c.legacy}, curves {AnimationUtility.GetCurveBindings(c).Length}");
        }

        EditorSceneManager.OpenScene("Assets/E4/E4.unity");
        var player = GameObject.Find("Guitarist");
        var animator = player.GetComponent<Animator>();
        var clip = AssetDatabase.LoadAllAssetsAtPath("Assets/E4/Mocap/guitar_playing_111490901.fbx").OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview"));
        Debug.Log($"E4D: animator avatar {animator.avatar?.name} (human {animator.isHuman}), controller {animator.runtimeAnimatorController?.name}");

        var graph = PlayableGraph.Create("E4Diag");
        var output = AnimationPlayableOutput.Create(graph, "out", animator);
        var playable = AnimationClipPlayable.Create(graph, clip);
        output.SetSourcePlayable(playable);
        var hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        var rest = hand.position;
        foreach (var t in new[] { 0f, 0.5f, 1.0f, 2.0f })
        {
            playable.SetTime(t);
            graph.Evaluate();
            Debug.Log($"E4D: t={t:F1} s right hand at {hand.position} ({(hand.position - rest).magnitude * 100:F0} cm from rest)");
        }
        graph.Destroy();
    }
}
