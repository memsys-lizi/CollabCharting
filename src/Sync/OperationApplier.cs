using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CollabCharting
{
    internal static class OperationApplier
    {
        public static bool TryApply(CollabOperationBatch batch, string reason, out string conflict)
        {
            conflict = string.Empty;
            if (!EditorStateAdapter.IsEditorReady)
            {
                conflict = "编辑器尚未打开关卡";
                return false;
            }

            JObject root;
            try
            {
                root = JObject.Parse(EditorStateAdapter.EncodeCurrentLevel());
            }
            catch (Exception ex)
            {
                conflict = $"读取当前关卡失败：{ex.Message}";
                return false;
            }

            foreach (CollabAtomicOperation op in batch.Ops)
            {
                if (!TryApplyAtomic(root, op, op.Payload, out conflict))
                {
                    return false;
                }
            }

            EntityIdRegistry.ApplyBatch(batch, root);
            EditorStateAdapter.ApplyLevelText(root.ToString(Formatting.None), reason, preserveEntityIds: true);
            return true;
        }

        private static bool TryApplyAtomic(JObject root, CollabAtomicOperation op, JObject payload, out string conflict)
        {
            conflict = string.Empty;
            switch (op.Kind)
            {
                case "path.insertFloor":
                    return ApplyPathInsert(root, payload, out conflict);
                case "path.deleteFloors":
                    return ApplyPathDelete(root, payload, out conflict);
                case "path.setFloor":
                    return ApplyPathSet(root, payload, out conflict);
                case "settings.setProperties":
                    return ApplySettings(root, payload, out conflict);
                case "event.add":
                    return ApplyArrayAdd(root, "actions", op, payload, out conflict);
                case "event.remove":
                    return ApplyArrayRemove(root, "actions", op, payload, out conflict);
                case "event.setProperties":
                    return ApplyArrayProperties(root, "actions", op, payload, out conflict);
                case "decoration.add":
                    return ApplyArrayAdd(root, "decorations", op, payload, out conflict);
                case "decoration.remove":
                    return ApplyArrayRemove(root, "decorations", op, payload, out conflict);
                case "decoration.setProperties":
                    return ApplyArrayProperties(root, "decorations", op, payload, out conflict);
                case "decoration.reorder":
                    return ApplyArrayReorder(root, "decorations", payload, out conflict);
                default:
                    conflict = $"未知操作类型：{op.Kind}";
                    return false;
            }
        }

        private static bool ApplyPathInsert(JObject root, JObject payload, out string conflict)
        {
            conflict = string.Empty;
            string mode = payload.Value<string>("mode") ?? "angle";
            int index = payload.Value<int?>("index") ?? -1;
            if (index < 0)
            {
                conflict = "轨道插入位置无效";
                return false;
            }

            if (mode == "char")
            {
                string pathData = root.Value<string>("pathData") ?? string.Empty;
                string value = payload.Value<string>("value") ?? PayloadValuesAsString(payload);
                if (string.IsNullOrEmpty(value))
                {
                    conflict = "轨道字符为空";
                    return false;
                }

                index = Math.Min(index, pathData.Length);
                root["pathData"] = pathData.Insert(index, value);
                return true;
            }

            JArray angles = EnsureArray(root, "angleData");
            if (payload["values"] is JArray insertValues)
            {
                int insertAt = Math.Min(index, angles.Count);
                foreach (JToken insertValue in insertValues)
                {
                    angles.Insert(insertAt++, insertValue.DeepClone());
                }

                return true;
            }

            JToken? valueToken = payload["value"];
            if (valueToken == null)
            {
                conflict = "轨道角度为空";
                return false;
            }

            angles.Insert(Math.Min(index, angles.Count), valueToken.DeepClone());
            return true;
        }

        private static bool ApplyPathDelete(JObject root, JObject payload, out string conflict)
        {
            conflict = string.Empty;
            string mode = payload.Value<string>("mode") ?? "angle";
            int index = payload.Value<int?>("index") ?? -1;
            int count = payload.Value<int?>("count") ?? 1;
            if (index < 0 || count <= 0)
            {
                conflict = "轨道删除范围无效";
                return false;
            }

            if (mode == "char")
            {
                string pathData = root.Value<string>("pathData") ?? string.Empty;
                if (index + count > pathData.Length)
                {
                    conflict = "轨道删除范围超出当前谱面";
                    return false;
                }

                root["pathData"] = pathData.Remove(index, count);
                return true;
            }

            JArray angles = EnsureArray(root, "angleData");
            if (index + count > angles.Count)
            {
                conflict = "轨道删除范围超出当前谱面";
                return false;
            }

            for (int i = 0; i < count; i++)
            {
                angles.RemoveAt(index);
            }

            return true;
        }

        private static bool ApplyPathSet(JObject root, JObject payload, out string conflict)
        {
            conflict = string.Empty;
            string mode = payload.Value<string>("mode") ?? "angle";
            int index = payload.Value<int?>("index") ?? -1;
            if (index < 0)
            {
                conflict = "轨道修改位置无效";
                return false;
            }

            JToken? oldValue = payload["oldValue"];
            JToken? newValue = payload["newValue"];
            if (mode == "char")
            {
                string pathData = root.Value<string>("pathData") ?? string.Empty;
                if (index >= pathData.Length)
                {
                    conflict = "轨道修改位置超出当前谱面";
                    return false;
                }

                if (oldValue != null && pathData[index].ToString() != oldValue.Value<string>())
                {
                    conflict = $"轨道 {index} 已被其他玩家修改";
                    return false;
                }

                char replacement = (newValue?.Value<string>() ?? string.Empty).FirstOrDefault();
                if (replacement == '\0')
                {
                    conflict = "轨道字符为空";
                    return false;
                }

                char[] chars = pathData.ToCharArray();
                chars[index] = replacement;
                root["pathData"] = new string(chars);
                return true;
            }

            JArray angles = EnsureArray(root, "angleData");
            if (index >= angles.Count)
            {
                conflict = "轨道修改位置超出当前谱面";
                return false;
            }

            if (oldValue != null && !JToken.DeepEquals(angles[index], oldValue))
            {
                conflict = $"轨道 {index} 已被其他玩家修改";
                return false;
            }

            angles[index] = newValue?.DeepClone() ?? JValue.CreateNull();
            return true;
        }

        private static bool ApplySettings(JObject root, JObject payload, out string conflict)
        {
            JObject settings = (root["settings"] as JObject) ?? new JObject();
            root["settings"] = settings;
            return ApplyPropertyChanges(settings, payload, out conflict);
        }

        private static bool ApplyArrayAdd(JObject root, string arrayName, CollabAtomicOperation op, JObject payload, out string conflict)
        {
            conflict = string.Empty;
            JArray array = EnsureArray(root, arrayName);
            int index = payload.Value<int?>("index") ?? array.Count;
            JObject? item = payload["item"] as JObject;
            if (item == null)
            {
                conflict = "新增对象数据为空";
                return false;
            }

            array.Insert(Math.Min(Math.Max(index, 0), array.Count), item.DeepClone());
            return true;
        }

        private static bool ApplyArrayRemove(JObject root, string arrayName, CollabAtomicOperation op, JObject payload, out string conflict)
        {
            conflict = string.Empty;
            JArray array = EnsureArray(root, arrayName);
            JObject? item = payload["item"] as JObject;
            int index = FindEntityIndex(arrayName, array, op.Target, item);
            if (index < 0)
            {
                conflict = "目标对象已不存在";
                return false;
            }

            if (item != null && OperationDiffUtility.HashToken(array[index]) != OperationDiffUtility.HashToken(item))
            {
                conflict = "目标对象已被其他玩家修改，无法删除";
                return false;
            }

            array.RemoveAt(index);
            return true;
        }

        private static bool ApplyArrayProperties(JObject root, string arrayName, CollabAtomicOperation op, JObject payload, out string conflict)
        {
            JArray array = EnsureArray(root, arrayName);
            int preferredIndex = payload.Value<int?>("index") ?? op.Target.Index;
            int index = FindEntityIndex(arrayName, array, op.Target, null, preferredIndex);
            if (index < 0 || !(array[index] is JObject item))
            {
                conflict = "目标对象已不存在";
                return false;
            }

            return ApplyPropertyChanges(item, payload, out conflict);
        }

        private static bool ApplyArrayReorder(JObject root, string arrayName, JObject payload, out string conflict)
        {
            conflict = string.Empty;
            if (!(payload["items"] is JArray items))
            {
                conflict = "排序数据为空";
                return false;
            }

            root[arrayName] = items.DeepClone();
            return true;
        }

        private static bool ApplyPropertyChanges(JObject target, JObject payload, out string conflict)
        {
            conflict = string.Empty;
            List<CollabPropertyChange> changes = payload["changes"]?.ToObject<List<CollabPropertyChange>>() ?? new List<CollabPropertyChange>();
            foreach (CollabPropertyChange change in changes)
            {
                JProperty? property = target.Property(change.Path);
                if (change.OldExists)
                {
                    if (property == null || !JToken.DeepEquals(property.Value, change.OldValue))
                    {
                        conflict = $"属性 {change.Path} 已被其他玩家修改";
                        return false;
                    }
                }
                else if (property != null)
                {
                    conflict = $"属性 {change.Path} 已被其他玩家新增";
                    return false;
                }
            }

            foreach (CollabPropertyChange change in changes)
            {
                if (change.NewExists)
                {
                    target[change.Path] = change.NewValue?.DeepClone() ?? JValue.CreateNull();
                }
                else
                {
                    target.Property(change.Path)?.Remove();
                }
            }

            return true;
        }

        private static int FindEntityIndex(string arrayName, JArray array, CollabOperationTarget target, JObject? expected, int preferredIndex = -1)
        {
            string domain = arrayName == "decorations" ? "decoration" : "event";
            int registryIndex = EntityIdRegistry.ResolveIndex(domain, target, array);
            if (registryIndex >= 0)
            {
                return registryIndex;
            }

            if (preferredIndex < 0)
            {
                preferredIndex = target.Index;
            }

            if (preferredIndex >= 0 && preferredIndex < array.Count && MatchesTarget(array[preferredIndex] as JObject, target, expected))
            {
                return preferredIndex;
            }

            if (expected != null)
            {
                string expectedHash = OperationDiffUtility.HashToken(expected);
                for (int i = 0; i < array.Count; i++)
                {
                    if (OperationDiffUtility.HashToken(array[i]) == expectedHash)
                    {
                        return i;
                    }
                }
            }

            int ordinal = 0;
            for (int i = 0; i < array.Count; i++)
            {
                JObject? item = array[i] as JObject;
                if (!MatchesTarget(item, target, null))
                {
                    continue;
                }

                if (target.Index < 0 || ordinal == target.Index)
                {
                    return i;
                }

                ordinal++;
            }

            return -1;
        }

        private static bool MatchesTarget(JObject? item, CollabOperationTarget target, JObject? expected)
        {
            if (item == null)
            {
                return false;
            }

            if (expected != null)
            {
                return string.Equals(item.Value<string>("eventType"), expected.Value<string>("eventType"), StringComparison.Ordinal) &&
                       item.Value<int?>("floor") == expected.Value<int?>("floor");
            }

            if (target.Domain == "decoration")
            {
                return string.IsNullOrEmpty(target.EventType) ||
                       string.Equals(item.Value<string>("eventType"), target.EventType, StringComparison.Ordinal);
            }

            return item.Value<int?>("floor") == target.Floor &&
                   string.Equals(item.Value<string>("eventType"), target.EventType, StringComparison.Ordinal);
        }

        private static JArray EnsureArray(JObject root, string property)
        {
            if (root[property] is JArray array)
            {
                return array;
            }

            array = new JArray();
            root[property] = array;
            return array;
        }

        private static string PayloadValuesAsString(JObject payload)
        {
            if (payload["values"] is JArray values)
            {
                return string.Concat(values.Select(value => value.Value<string>()));
            }

            return payload.Value<string>("values") ?? string.Empty;
        }
    }
}
