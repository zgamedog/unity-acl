using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;

public class Animation2ACL
{
    [MenuItem("Assets/Generate ACL")]
    static void GenerateACL()
    {
        var clip = Selection.activeObject as AnimationClip;
        var asset_path = AssetDatabase.GetAssetPath(clip);
        var go = AssetDatabase.LoadAssetAtPath<GameObject>(asset_path);
        if (go == null)
        {
            Debug.LogErrorFormat("Cannot find corresponding GameObject for {0}", asset_path);
            return;
        }
        var sjson_path = @"Temp\" + clip.name + ".acl.sjson";
        if (!Clip2ACLSJSON(go, clip, sjson_path))
            return;
        
        var bin_path = @"Temp\" + clip.name + ".acl";
        if (!SJSON2BIN(sjson_path, bin_path))
            return;

        var final_path = Path.ChangeExtension(asset_path, null) + "@" + clip.name + ".bytes";
        File.Copy(bin_path, final_path);
        Debug.LogFormat("ACL Bin {0}", final_path);
    }
    
    [MenuItem("Assets/Generate ACL", true)]
    static bool GenerateACLValidation()
    {
        var clip = Selection.activeObject as AnimationClip;
        return (clip != null);
    }

    class BoneAndTrack
    {
        static readonly Quaternion DefaultRotation = Quaternion.identity;
        static readonly Vector3 DefaultTranslation = Vector3.zero;
        static readonly Vector3 DefaultScale = Vector3.one;

        string name, parent;
        Quaternion bind_rotation;
        Vector3 bind_tranlation, bind_scale;

        Quaternion[] track_rotation;
        Vector3[] track_translation, track_scale;

        public void ParseBone(Transform bone, bool first)
        {
            name = bone.name;
            if (!first)
                parent = bone.parent.name;
            bind_rotation = bone.localRotation;
            bind_tranlation = bone.localPosition;
            bind_scale = bone.localScale;
        }

        static AnimationCurve TryGetEditorCurve(AnimationClip clip, string relativePath, string propertyName)
        {
            // try different curve
            var binding = EditorCurveBinding.FloatCurve(relativePath, typeof(Transform), propertyName);
            var curve = AnimationUtility.GetEditorCurve(clip, binding);
            if (curve != null)
                return curve;
            binding = EditorCurveBinding.PPtrCurve(relativePath, typeof(Transform), propertyName);
            curve = AnimationUtility.GetEditorCurve(clip, binding);
            if (curve != null)
                return curve;
            binding = EditorCurveBinding.DiscreteCurve(relativePath, typeof(Transform), propertyName);
            curve = AnimationUtility.GetEditorCurve(clip, binding);
            if (curve != null)
                return curve;
            // Debug.LogError("Cannot find curve for " + relativePath + " with " + propertyName);
            return null;
        }

        public void ParseTrack(AnimationClip clip, string relativePath)
        {
            int samples = Mathf.CeilToInt(clip.length * clip.frameRate) + 1;

            var rot_x = TryGetEditorCurve(clip, relativePath, "m_LocalRotation.x");
            var rot_y = TryGetEditorCurve(clip, relativePath, "m_LocalRotation.y");
            var rot_z = TryGetEditorCurve(clip, relativePath, "m_LocalRotation.z");
            var rot_w = TryGetEditorCurve(clip, relativePath, "m_LocalRotation.w");
            var pos_x = TryGetEditorCurve(clip, relativePath, "m_LocalPosition.x");
            var pos_y = TryGetEditorCurve(clip, relativePath, "m_LocalPosition.y");
            var pos_z = TryGetEditorCurve(clip, relativePath, "m_LocalPosition.z");
            var scale_x = TryGetEditorCurve(clip, relativePath, "m_LocalScale.x");
            var scale_y = TryGetEditorCurve(clip, relativePath, "m_LocalScale.y");
            var scale_z = TryGetEditorCurve(clip, relativePath, "m_LocalScale.z");

            bool has_rot = rot_x != null && rot_y != null && rot_z != null && rot_w != null;
            bool has_pos = pos_x != null && pos_y != null && pos_z != null;
            bool has_scale = scale_x != null && scale_y != null && scale_z != null;

            if (has_rot)
                track_rotation = new Quaternion[samples];
            else
                track_rotation = new Quaternion[0];
            if (has_pos)
                track_translation = new Vector3[samples];
            else
                track_translation = new Vector3[0];
            if (has_scale)
                track_scale = new Vector3[samples];
            else
                track_scale = new Vector3[0];

            for (int i = 0; i < samples; i++)
            {
                float time = (float)i / clip.frameRate;
                if (has_rot)
                    track_rotation[i] = new Quaternion(rot_x.Evaluate(time), rot_y.Evaluate(time), rot_z.Evaluate(time), rot_w.Evaluate(time));
                if (has_pos)
                    track_translation[i] = new Vector3(pos_x.Evaluate(time), pos_y.Evaluate(time), pos_z.Evaluate(time));
                if (has_scale)
                    track_scale[i] = new Vector3(scale_x.Evaluate(time), scale_y.Evaluate(time), scale_z.Evaluate(time));
            }

            bool valid = false;
            if (has_rot)
            {
                for (int i = 0; i < samples; i++)
                {
                    if (track_rotation[i] != DefaultRotation)
                    {
                        valid = true;
                        break;
                    }
                }
                if (!valid)
                    track_rotation = new Quaternion[0];
            }

            valid = false;
            if (has_pos)
            {
                for (int i = 0; i < samples; i++)
                {
                    if (track_translation[i] != DefaultTranslation)
                    {
                        valid = true;
                        break;
                    }
                }
                if (!valid)
                    track_translation = new Vector3[0];
            }
            
            valid = false;
            if (has_scale)
            {
                for (int i = 0; i < samples; i++)
                {
                    if (track_scale[i] != DefaultScale)
                    {
                        valid = true;
                        break;
                    }
                }
                if (!valid)
                    track_scale = new Vector3[0];
            }
        }

        public void WriteBone(StreamWriter writer)
        {
            writer.WriteLine("\t{");
            writer.WriteLine("\t\tname = \"" + name + "\"");
            writer.WriteLine("\t\tparent = \"" + parent + "\"");
            writer.WriteLine("\t\tvertex_distance = 3.0");
            if (bind_rotation != DefaultRotation)
            {
                writer.WriteLine("\t\tbind_rotation = [ {0}, {1}, {2}, {3} ]", bind_rotation.x, bind_rotation.y, bind_rotation.z, bind_rotation.w);
            }
            if (bind_tranlation != DefaultTranslation)
            {
                writer.WriteLine("\t\tbind_translation = [ {0}, {1}, {2} ]", bind_tranlation.x, bind_tranlation.y, bind_tranlation.z);
            }
            if (bind_scale != DefaultScale)
            {
                writer.WriteLine("\t\tbind_scale = [ {0}, {1}, {2} ]", bind_scale.x, bind_scale.y, bind_scale.z);
            }
            writer.WriteLine("\t}");
        }
        
        public void WriteTrack(StreamWriter writer)
        {
            // Empty track is also needed, or acl_compressor will assert

            writer.WriteLine("\t{");
            writer.WriteLine("\t\tname = \"" + name + "\"");
            if (track_rotation.Length > 0)
            {
                writer.WriteLine("\t\trotations =");
                writer.WriteLine("\t\t[");
                foreach (var rot in track_rotation)
                {
                    writer.WriteLine("\t\t\t[ {0}, {1}, {2}, {3} ]", rot.x, rot.y, rot.z, rot.w);
                }
                writer.WriteLine("\t\t]");
            }
            if (track_translation.Length > 0)
            {
                writer.WriteLine("\t\ttranslations =");
                writer.WriteLine("\t\t[");
                foreach (var pos in track_translation)
                {
                    writer.WriteLine("\t\t\t[ {0}, {1}, {2} ]", pos.x, pos.y, pos.z);
                }
                writer.WriteLine("\t\t]");
            }
            if (track_scale.Length > 0)
            {
                writer.WriteLine("\t\tscales =");
                writer.WriteLine("\t\t[");
                foreach (var scale in track_scale)
                {
                    writer.WriteLine("\t\t\t[ {0}, {1}, {2} ]", scale.x, scale.y, scale.z);
                }
                writer.WriteLine("\t\t]");
            }
            writer.WriteLine("\t}");
        }
    }

    static void TraverseBone(Transform bone, List<BoneAndTrack> bone_and_tracks, AnimationClip clip, string relativeTransform)
    {
        BoneAndTrack bone_and_track = new BoneAndTrack();
        bone_and_track.ParseBone(bone, relativeTransform.Length == 0);
        bone_and_tracks.Add(bone_and_track);

        if (relativeTransform.Length == 0)
            relativeTransform = bone.name;
        else
            relativeTransform = relativeTransform + "/" + bone.name;
        bone_and_track.ParseTrack(clip, relativeTransform);

        for (int i = 0; i < bone.childCount; i++)
        {
            TraverseBone(bone.GetChild(i), bone_and_tracks, clip, relativeTransform);
        }
    }

    static bool Clip2ACLSJSON(GameObject go, AnimationClip clip, string path)
    {
        int samples = Mathf.CeilToInt(clip.length * clip.frameRate) + 1;
        
        var curves = AnimationUtility.GetCurveBindings(clip);
        if (curves.Length == 0)
        {
            Debug.LogError("No Animation Data found");
            return false;
        }
        if (curves[0].path.Length == 0)
        {
            Debug.LogError("Maybe using AnimationType Humanoid?");
            return false;
        }

        List<BoneAndTrack> bone_and_tracks = new List<BoneAndTrack>();
        Debug.Assert(go.transform.childCount == 1);
        TraverseBone(go.transform.GetChild(0), bone_and_tracks, clip, "");

        StreamWriter writer = new StreamWriter(path);
        writer.WriteLine("version = 1");
        writer.WriteLine();
        
        writer.WriteLine("clip =");
        writer.WriteLine("{");
        writer.WriteLine("\tname = \"" + clip.name + "\"");
        writer.WriteLine("\tnum_samples = " + samples);
        writer.WriteLine("\tsample_rate = " + clip.frameRate);
        writer.WriteLine("\terror_threshold = 0.01");
        writer.WriteLine("}");
        writer.WriteLine();
        
        writer.WriteLine("bones =");
        writer.WriteLine("[");
        foreach (var bone_and_track in bone_and_tracks)
        {
            bone_and_track.WriteBone(writer);
        }
        writer.WriteLine("]");
        writer.WriteLine();
        
        writer.WriteLine("tracks =");
        writer.WriteLine("[");
        foreach (var bone_and_track in bone_and_tracks)
        {
            bone_and_track.WriteTrack(writer);
        }
        writer.WriteLine("]");
        writer.WriteLine();
        writer.Close();

        Debug.LogFormat("Finish Generating {0}", path);
        return true;
    }
    
    static bool SJSON2BIN(string sjson, string bin)
    {
        var tool_path = Application.dataPath + @"\ACL\Editor\acl_compressor.exe";
        sjson = Path.GetDirectoryName(Application.dataPath) + @"\" + sjson;
        bin = Path.GetDirectoryName(Application.dataPath) + @"\" + bin;
        var processInfo = new System.Diagnostics.ProcessStartInfo(tool_path, "-acl=\"" + sjson + "\" -out=\"" + bin + "\"");
        processInfo.CreateNoWindow = true;
        processInfo.UseShellExecute = false;
 
        var process = System.Diagnostics.Process.Start(processInfo);
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            Debug.LogError("acl_compressor run failed");
            return false;
        }
        process.Close();
        Debug.LogFormat("Finish Converting {0}", bin);
        return true;
    }

}
