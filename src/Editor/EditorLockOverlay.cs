using System;
using System.Collections.Generic;
using ADOFAI;
using UnityEngine;

namespace CollabCharting
{
    internal static class EditorLockOverlay
    {
        private static readonly Dictionary<string, TextMesh> labels = new Dictionary<string, TextMesh>();
        private static readonly Dictionary<int, FloorHighlightState> floorHighlights = new Dictionary<int, FloorHighlightState>();
        private static readonly Color LocalLockColor = new Color(0.2f, 0.85f, 1f, 1f);
        private static readonly Color RemoteLockColor = new Color(1f, 0.64f, 0.22f, 1f);

        public static void Update()
        {
            if (!CollabRuntime.Session.InLobby || ADOBase.editor == null)
            {
                Clear();
                return;
            }

            var active = new HashSet<string>(StringComparer.Ordinal);
            var activeFloors = new Dictionary<int, bool>();
            foreach (CollabLock collabLock in CollabRuntime.Session.ActiveLocks)
            {
                if (!TryResolvePosition(collabLock.Target, out Vector3 position, out int floorId))
                {
                    continue;
                }

                bool isLocal = CollabRuntime.Session.IsLocalLock(collabLock);
                active.Add(collabLock.Target);
                if (floorId >= 0 && !isLocal)
                {
                    activeFloors[floorId] = true;
                }

                TextMesh label = GetOrCreate(collabLock.Target);
                label.text = isLocal ? GetLocalLabel(collabLock.Target) : $"{collabLock.OwnerName} 正在编辑";
                label.color = isLocal ? LocalLockColor : RemoteLockColor;
                label.transform.position = position;
                Camera camera = ADOBase.editor.camera != null ? ADOBase.editor.camera : Camera.main;
                if (camera != null)
                {
                    label.transform.rotation = camera.transform.rotation;
                }
            }

            var remove = new List<string>();
            foreach (string key in labels.Keys)
            {
                if (!active.Contains(key))
                {
                    remove.Add(key);
                }
            }

            foreach (string key in remove)
            {
                UnityEngine.Object.Destroy(labels[key].gameObject);
                labels.Remove(key);
            }

            foreach (KeyValuePair<int, bool> pair in activeFloors)
            {
                ApplyFloorHighlight(pair.Key, RemoteLockColor);
            }

            var restore = new List<int>();
            foreach (int floorId in floorHighlights.Keys)
            {
                if (!activeFloors.ContainsKey(floorId))
                {
                    restore.Add(floorId);
                }
            }

            foreach (int floorId in restore)
            {
                RestoreFloorHighlight(floorId);
            }
        }

        public static void Clear()
        {
            foreach (TextMesh label in labels.Values)
            {
                if (label != null)
                {
                    UnityEngine.Object.Destroy(label.gameObject);
                }
            }

            labels.Clear();
            foreach (int floorId in new List<int>(floorHighlights.Keys))
            {
                RestoreFloorHighlight(floorId);
            }
        }

        private static TextMesh GetOrCreate(string target)
        {
            if (labels.TryGetValue(target, out TextMesh label) && label != null)
            {
                return label;
            }

            GameObject obj = new GameObject("CollabLockLabel_" + target.Replace(':', '_'));
            label = obj.AddComponent<TextMesh>();
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.fontSize = 32;
            label.characterSize = 0.08f;
            label.color = new Color(0.45f, 0.9f, 1f, 1f);
            MeshRenderer renderer = obj.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.sortingOrder = 30000;
            }

            labels[target] = label;
            return label;
        }

        private static bool TryResolvePosition(string target, out Vector3 position, out int floorId)
        {
            position = Vector3.zero;
            floorId = -1;
            if (target.StartsWith("floor:", StringComparison.Ordinal))
            {
                if (!int.TryParse(target.Substring("floor:".Length), out floorId))
                {
                    return false;
                }

                return TryFloorPosition(floorId, 0.75f, out position);
            }

            if (target.StartsWith("event:", StringComparison.Ordinal))
            {
                string[] parts = target.Split(':');
                if (parts.Length < 2 || !int.TryParse(parts[1], out floorId))
                {
                    return false;
                }

                return TryFloorPosition(floorId, 1.05f, out position);
            }

            if (target.StartsWith(EditorLockTargets.EventIdPrefix, StringComparison.Ordinal))
            {
                string id = target.Substring(EditorLockTargets.EventIdPrefix.Length);
                if (!EntityIdRegistry.TryGetEntityInfo("event", id, out _, out floorId, out _))
                {
                    return false;
                }

                return TryFloorPosition(floorId, 1.05f, out position);
            }

            if (target.StartsWith(EditorLockTargets.DecorationIdPrefix, StringComparison.Ordinal))
            {
                string id = target.Substring(EditorLockTargets.DecorationIdPrefix.Length);
                if (!EntityIdRegistry.TryGetEntityInfo("decoration", id, out int index, out _, out _))
                {
                    return false;
                }

                scrDecoration decoration = scrDecorationManager.GetDecoration(index);
                if (decoration == null)
                {
                    return false;
                }

                position = decoration.transform.position + new Vector3(0f, 0.8f, -1f);
                return true;
            }

            if (target.StartsWith("decoration:", StringComparison.Ordinal))
            {
                string id = target.Substring("decoration:".Length);
                if (!int.TryParse(id, out int index))
                {
                    return false;
                }

                scrDecoration decoration = scrDecorationManager.GetDecoration(index);
                if (decoration == null)
                {
                    return false;
                }

                position = decoration.transform.position + new Vector3(0f, 0.8f, -1f);
                return true;
            }

            return false;
        }

        private static bool TryFloorPosition(int floorId, float yOffset, out Vector3 position)
        {
            position = Vector3.zero;
            if (ADOBase.editor == null || floorId < 0 || floorId >= ADOBase.editor.floors.Count)
            {
                return false;
            }

            scrFloor floor = ADOBase.editor.floors[floorId];
            if (floor == null)
            {
                return false;
            }

            position = floor.transform.position + new Vector3(0f, yOffset, -1f);
            return true;
        }

        private static string GetLocalLabel(string target)
        {
            if (target.StartsWith("event:", StringComparison.Ordinal) ||
                target.StartsWith(EditorLockTargets.EventIdPrefix, StringComparison.Ordinal))
            {
                return "你正在编辑事件";
            }

            if (target.StartsWith("decoration:", StringComparison.Ordinal) ||
                target.StartsWith(EditorLockTargets.DecorationIdPrefix, StringComparison.Ordinal))
            {
                return "你正在编辑装饰";
            }

            return "你正在编辑砖块";
        }

        private static void ApplyFloorHighlight(int floorId, Color highlight)
        {
            if (!TryGetFloor(floorId, out scrFloor floor) || floor.floorRenderer == null)
            {
                return;
            }

            if (!floorHighlights.ContainsKey(floorId))
            {
                floorHighlights[floorId] = new FloorHighlightState
                {
                    BaseColor = floor.floorRenderer.color,
                    BaseDeselectedColor = floor.floorRenderer.deselectedColor,
                    TopGlowColor = floor.topGlow != null ? floor.topGlow.color : Color.clear
                };
            }

            FloorHighlightState state = floorHighlights[floorId];
            float pulse = (Mathf.Sin(Time.unscaledTime * 5.5f) + 1f) * 0.5f;
            float blend = 0.42f + pulse * 0.18f;
            floor.floorRenderer.color = Color.Lerp(state.BaseDeselectedColor, highlight, blend);

            if (floor.topGlow != null)
            {
                floor.topGlow.color = new Color(highlight.r, highlight.g, highlight.b, 0.35f + pulse * 0.25f);
            }
        }

        private static void RestoreFloorHighlight(int floorId)
        {
            if (!floorHighlights.TryGetValue(floorId, out FloorHighlightState state))
            {
                return;
            }

            if (TryGetFloor(floorId, out scrFloor floor) && floor.floorRenderer != null)
            {
                floor.floorRenderer.color = state.BaseColor;
                if (floor.topGlow != null)
                {
                    floor.topGlow.color = state.TopGlowColor;
                }
            }

            floorHighlights.Remove(floorId);
        }

        private static bool TryGetFloor(int floorId, out scrFloor floor)
        {
            floor = default!;
            if (ADOBase.editor == null || floorId < 0 || floorId >= ADOBase.editor.floors.Count)
            {
                return false;
            }

            floor = ADOBase.editor.floors[floorId];
            return floor != null;
        }

        private sealed class FloorHighlightState
        {
            public Color BaseColor { get; set; }

            public Color BaseDeselectedColor { get; set; }

            public Color TopGlowColor { get; set; }
        }
    }
}
