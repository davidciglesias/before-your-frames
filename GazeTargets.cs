using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace BYEFrameBlink
{
    /// <summary>
    /// Objects the game is waiting for you to look at. Many scenes progress on gaze alone: components subscribe to
    /// InteractableManager.OnGazeAtGetName / OnGazeAtGetTag / OnGazeGetObject and compare the hit object with a target
    /// they hold in a field (a GameObject, a Transform, or a name/tag string). This reads those subscribers' fields
    /// (and one level into plain data containers) and returns the live colliders they refer to. Used by autoplay and
    /// the "/bye/gazetargets" debug command.
    /// </summary>
    internal static class GazeTargets
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        public static List<Collider> Collect(int layerMask, List<string> describe = null)
        {
            var result = new List<Collider>();
            var seen = new HashSet<Collider>();
            Scan(InteractableManager.OnGazeAtGetName, StringKind.Name, layerMask, result, seen, describe);
            Scan(InteractableManager.OnGazeAtGetTag, StringKind.Tag, layerMask, result, seen, describe);
            Scan(InteractableManager.OnGazeGetObject, StringKind.None, layerMask, result, seen, describe);
            return result;
        }

        private enum StringKind { None, Name, Tag }

        private static void Scan(Delegate d, StringKind kind, int layerMask, List<Collider> result, HashSet<Collider> seen, List<string> describe)
        {
            if (d == null) return;
            foreach (Delegate sub in d.GetInvocationList())
            {
                if (!(sub.Target is MonoBehaviour mb) || mb == null || !mb.isActiveAndEnabled) continue;
                int before = result.Count;
                Add(mb.gameObject, layerMask, result, seen); // e.g. OnGazeEvent, AnimateOnGaze compare with themselves
                ScanObject(mb, kind, layerMask, result, seen, 0);
                describe?.Add($"{mb.GetType().Name} on '{mb.name}' ({kind}) -> {result.Count - before} target(s)" +
                              (result.Count > before ? ": " + Names(result, before) : ""));
            }
        }

        private static string Names(List<Collider> list, int from)
        {
            var names = new List<string>();
            for (int i = from; i < list.Count; i++) names.Add(list[i].name);
            return string.Join(", ", names.ToArray());
        }

        private static void ScanObject(object obj, StringKind kind, int layerMask, List<Collider> result, HashSet<Collider> seen, int depth)
        {
            for (Type t = obj.GetType(); t != null && t != typeof(MonoBehaviour) && t != typeof(object); t = t.BaseType)
            {
                foreach (FieldInfo f in t.GetFields(Fields | BindingFlags.DeclaredOnly))
                {
                    object v;
                    try { v = f.GetValue(obj); } catch { continue; }
                    if (v == null) continue;
                    if (v is GameObject go) Add(go, layerMask, result, seen);
                    else if (v is Component c && !(v is MonoBehaviour)) Add(c.gameObject, layerMask, result, seen);
                    else if (v is string s && s.Length > 0 && kind != StringKind.None) AddByString(s, kind, layerMask, result, seen);
                    else if (v is IEnumerable list && !(v is string) && depth == 0)
                    {
                        foreach (object e in list)
                        {
                            if (e is GameObject lg) Add(lg, layerMask, result, seen);
                            else if (e is Component lc && !(e is MonoBehaviour)) Add(lc.gameObject, layerMask, result, seen);
                        }
                    }
                    else if (depth == 0 && !(v is UnityEngine.Object) && f.FieldType.IsClass && !f.FieldType.IsArray &&
                             f.FieldType.Assembly == typeof(InteractableManager).Assembly)
                        ScanObject(v, kind, layerMask, result, seen, depth + 1); // e.g. IAU_*CI _dataContainer
                }
            }
        }

        private static void AddByString(string s, StringKind kind, int layerMask, List<Collider> result, HashSet<Collider> seen)
        {
            if (kind == StringKind.Name)
            {
                GameObject go = GameObject.Find(s);
                if (go != null) Add(go, layerMask, result, seen);
                return;
            }
            if (s == "Untagged") return;
            GameObject[] tagged;
            try { tagged = GameObject.FindGameObjectsWithTag(s); } catch { return; } // not a defined tag
            foreach (GameObject go in tagged) Add(go, layerMask, result, seen);
        }

        private static void Add(GameObject go, int layerMask, List<Collider> result, HashSet<Collider> seen)
        {
            if (go == null || !go.activeInHierarchy) return;
            foreach (Collider col in go.GetComponentsInChildren<Collider>())
            {
                if (!col.enabled || ((1 << col.gameObject.layer) & layerMask) == 0 || !seen.Add(col)) continue;
                result.Add(col);
                break; // one collider per target object is enough to aim at
            }
        }
    }
}
