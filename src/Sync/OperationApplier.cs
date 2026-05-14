using System;
using System.Collections.Generic;
using System.Linq;
using ADOFAI;
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

            JObject validatedRoot = (JObject)root.DeepClone();
            foreach (CollabAtomicOperation op in batch.Ops)
            {
                if (!TryApplyAtomic(validatedRoot, op, op.Payload, out conflict))
                {
                    return false;
                }
            }

            int localSelectedFloor = EditorStateAdapter.GetSelectedFloorId();
            if (!CanApplyDirect(batch))
            {
                EntityIdRegistry.ApplyBatch(batch, validatedRoot);
                EditorStateAdapter.ApplyLevelText(
                    validatedRoot.ToString(Formatting.None),
                    reason,
                    preserveEntityIds: true,
                    selectedFloor: localSelectedFloor);
                return true;
            }

            if (!ValidateDirectPayloads(batch, out conflict))
            {
                return false;
            }

            JObject directRoot = root;
            var directState = new DirectApplyState();
            CollabRuntime.IsApplyingRemote = true;
            try
            {
                foreach (CollabAtomicOperation op in batch.Ops)
                {
                    if (!TryApplyAtomicDirect(directRoot, op, op.Payload, directState, out conflict))
                    {
                        return false;
                    }

                    if (!TryApplyAtomic(directRoot, op, op.Payload, out conflict))
                    {
                        return false;
                    }
                }

                EntityIdRegistry.ApplyBatch(batch, directRoot);
                EditorStateAdapter.RefreshAfterDirectOperation(
                    reason,
                    localSelectedFloor,
                    directState.PathChanged,
                    directState.EventsChanged,
                    directState.DecorationsChanged);
            }
            finally
            {
                CollabRuntime.IsApplyingRemote = false;
                OperationCapture.ResetBaseline();
            }

            return true;
        }

        private static bool ValidateDirectPayloads(CollabOperationBatch batch, out string conflict)
        {
            conflict = string.Empty;
            foreach (CollabAtomicOperation op in batch.Ops)
            {
                if (op.Kind.EndsWith(".add", StringComparison.Ordinal))
                {
                    if (op.Payload["item"] is JObject item &&
                        !TryDecodeLevelEvent(item, out _, out conflict))
                    {
                        return false;
                    }
                }
                else if (op.Kind.EndsWith(".setProperties", StringComparison.Ordinal))
                {
                    if (op.Payload["afterItem"] is JObject afterItem &&
                        !TryDecodeLevelEvent(afterItem, out _, out conflict))
                    {
                        return false;
                    }
                }
                else if (op.Kind == "decoration.reorder" && op.Payload["items"] is JArray items)
                {
                    foreach (JToken item in items)
                    {
                        if (!(item is JObject itemObject) ||
                            !TryDecodeLevelEvent(itemObject, out _, out conflict))
                        {
                            conflict = string.IsNullOrWhiteSpace(conflict) ? "排序数据无法解析" : conflict;
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        private static bool CanApplyDirect(CollabOperationBatch batch)
        {
            foreach (CollabAtomicOperation op in batch.Ops)
            {
                switch (op.Kind)
                {
                    case "path.insertFloor":
                    case "path.deleteFloors":
                    case "path.setFloor":
                    case "event.add":
                    case "event.remove":
                    case "event.setProperties":
                    case "decoration.add":
                    case "decoration.remove":
                    case "decoration.setProperties":
                    case "decoration.reorder":
                        continue;
                    default:
                        return false;
                }
            }

            return true;
        }

        private static bool TryApplyAtomicDirect(
            JObject root,
            CollabAtomicOperation op,
            JObject payload,
            DirectApplyState state,
            out string conflict)
        {
            conflict = string.Empty;
            switch (op.Kind)
            {
                case "path.insertFloor":
                    return ApplyPathInsertDirect(payload, state, out conflict);
                case "path.deleteFloors":
                    return ApplyPathDeleteDirect(payload, state, out conflict);
                case "path.setFloor":
                    return ApplyPathSetDirect(payload, state, out conflict);
                case "event.add":
                    return ApplyEventArrayAddDirect(root, "actions", op, payload, state, out conflict);
                case "event.remove":
                    return ApplyEventArrayRemoveDirect(root, "actions", op, payload, state, out conflict);
                case "event.setProperties":
                    return ApplyEventArrayPropertiesDirect(root, "actions", op, payload, state, out conflict);
                case "decoration.add":
                    return ApplyEventArrayAddDirect(root, "decorations", op, payload, state, out conflict);
                case "decoration.remove":
                    return ApplyEventArrayRemoveDirect(root, "decorations", op, payload, state, out conflict);
                case "decoration.setProperties":
                    return ApplyEventArrayPropertiesDirect(root, "decorations", op, payload, state, out conflict);
                case "decoration.reorder":
                    return ApplyDecorationReorderDirect(payload, state, out conflict);
                default:
                    conflict = $"无法直接应用操作类型：{op.Kind}";
                    return false;
            }
        }

        private static bool ApplyPathInsertDirect(JObject payload, DirectApplyState state, out string conflict)
        {
            conflict = string.Empty;
            LevelData levelData = ADOBase.editor.levelData;
            string mode = payload.Value<string>("mode") ?? "angle";
            int index = payload.Value<int?>("index") ?? -1;
            if (index < 0)
            {
                conflict = "轨道插入位置无效";
                return false;
            }

            if (mode == "char")
            {
                string values = PayloadValuesAsString(payload);
                if (string.IsNullOrEmpty(values))
                {
                    conflict = "轨道字符为空";
                    return false;
                }

                levelData.pathData = (levelData.pathData ?? string.Empty)
                    .Insert(Math.Min(index, levelData.pathData?.Length ?? 0), values);
                state.PathChanged = true;
                return true;
            }

            List<float> angles = levelData.angleData ?? new List<float>();
            levelData.angleData = angles;
            int insertAt = Math.Min(index, angles.Count);
            if (payload["values"] is JArray valuesArray)
            {
                foreach (JToken value in valuesArray)
                {
                    angles.Insert(insertAt++, value.Value<float>());
                }
            }
            else if (payload["value"] != null)
            {
                angles.Insert(insertAt, payload["value"]!.Value<float>());
            }
            else
            {
                conflict = "轨道角度为空";
                return false;
            }

            state.PathChanged = true;
            return true;
        }

        private static bool ApplyPathDeleteDirect(JObject payload, DirectApplyState state, out string conflict)
        {
            conflict = string.Empty;
            LevelData levelData = ADOBase.editor.levelData;
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
                string pathData = levelData.pathData ?? string.Empty;
                if (index + count > pathData.Length)
                {
                    conflict = "轨道删除范围超出当前谱面";
                    return false;
                }

                levelData.pathData = pathData.Remove(index, count);
                state.PathChanged = true;
                return true;
            }

            List<float> angles = levelData.angleData ?? new List<float>();
            if (index + count > angles.Count)
            {
                conflict = "轨道删除范围超出当前谱面";
                return false;
            }

            for (int i = 0; i < count; i++)
            {
                angles.RemoveAt(index);
            }

            state.PathChanged = true;
            return true;
        }

        private static bool ApplyPathSetDirect(JObject payload, DirectApplyState state, out string conflict)
        {
            conflict = string.Empty;
            LevelData levelData = ADOBase.editor.levelData;
            string mode = payload.Value<string>("mode") ?? "angle";
            int index = payload.Value<int?>("index") ?? -1;
            if (index < 0)
            {
                conflict = "轨道修改位置无效";
                return false;
            }

            if (mode == "char")
            {
                string pathData = levelData.pathData ?? string.Empty;
                if (index >= pathData.Length)
                {
                    conflict = "轨道修改位置超出当前谱面";
                    return false;
                }

                char replacement = (payload["newValue"]?.Value<string>() ?? string.Empty).FirstOrDefault();
                if (replacement == '\0')
                {
                    conflict = "轨道字符为空";
                    return false;
                }

                char[] chars = pathData.ToCharArray();
                chars[index] = replacement;
                levelData.pathData = new string(chars);
                state.PathChanged = true;
                return true;
            }

            List<float> angles = levelData.angleData ?? new List<float>();
            if (index >= angles.Count)
            {
                conflict = "轨道修改位置超出当前谱面";
                return false;
            }

            angles[index] = payload["newValue"]?.Value<float>() ?? 0f;
            state.PathChanged = true;
            return true;
        }

        private static bool ApplyEventArrayAddDirect(
            JObject root,
            string arrayName,
            CollabAtomicOperation op,
            JObject payload,
            DirectApplyState state,
            out string conflict)
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

            if (FindExistingEntityIdIndex(arrayName, array, op.Target) >= 0)
            {
                return true;
            }

            if (!TryDecodeLevelEvent(item, out LevelEvent levelEvent, out conflict))
            {
                return false;
            }

            IList<LevelEvent> list = GetEventList(arrayName);
            list.Insert(Math.Min(Math.Max(index, 0), list.Count), levelEvent);
            MarkArrayChanged(arrayName, state);
            return true;
        }

        private static bool ApplyEventArrayRemoveDirect(
            JObject root,
            string arrayName,
            CollabAtomicOperation op,
            JObject payload,
            DirectApplyState state,
            out string conflict)
        {
            conflict = string.Empty;
            JArray array = EnsureArray(root, arrayName);
            JObject? item = payload["item"] as JObject;
            int index = FindEntityIndex(arrayName, array, op.Target, item);
            if (index < 0)
            {
                return true;
            }

            IList<LevelEvent> list = GetEventList(arrayName);
            if (index >= list.Count)
            {
                return true;
            }

            list.RemoveAt(index);
            MarkArrayChanged(arrayName, state);
            return true;
        }

        private static bool ApplyEventArrayPropertiesDirect(
            JObject root,
            string arrayName,
            CollabAtomicOperation op,
            JObject payload,
            DirectApplyState state,
            out string conflict)
        {
            conflict = string.Empty;
            JArray array = EnsureArray(root, arrayName);
            int preferredIndex = payload.Value<int?>("index") ?? op.Target.Index;
            int index = FindEntityIndex(arrayName, array, op.Target, null, preferredIndex);
            JObject? afterItem = payload["afterItem"] as JObject;
            if (afterItem == null)
            {
                conflict = "目标对象数据为空";
                return false;
            }

            IList<LevelEvent> list = GetEventList(arrayName);
            if (index < 0 || index >= list.Count)
            {
                if (!TryDecodeLevelEvent(afterItem, out LevelEvent inserted, out conflict))
                {
                    return false;
                }

                int insertIndex = Math.Min(Math.Max(preferredIndex, 0), list.Count);
                list.Insert(insertIndex, inserted);
            }
            else
            {
                if (!(array[index] is JObject currentItem))
                {
                    conflict = "目标对象数据无法解析";
                    return false;
                }

                JObject merged = (JObject)currentItem.DeepClone();
                if (!ApplyPropertyChanges(merged, payload, out conflict))
                {
                    return false;
                }

                if (!TryDecodeLevelEvent(merged, out LevelEvent decoded, out conflict))
                {
                    return false;
                }

                CopyLevelEvent(decoded, list[index]);
            }

            MarkArrayChanged(arrayName, state);
            return true;
        }

        private static bool ApplyDecorationReorderDirect(JObject payload, DirectApplyState state, out string conflict)
        {
            conflict = string.Empty;
            if (!(payload["items"] is JArray items))
            {
                conflict = "排序数据为空";
                return false;
            }

            IList<LevelEvent> decorations = ADOBase.editor.levelData.decorations;
            decorations.Clear();
            foreach (JToken item in items)
            {
                if (!(item is JObject itemObject) ||
                    !TryDecodeLevelEvent(itemObject, out LevelEvent decoded, out conflict))
                {
                    return false;
                }

                decorations.Add(decoded);
            }

            state.DecorationsChanged = true;
            return true;
        }

        private static IList<LevelEvent> GetEventList(string arrayName)
        {
            return arrayName == "decorations"
                ? ADOBase.editor.levelData.decorations
                : ADOBase.editor.levelData.levelEvents;
        }

        private static void MarkArrayChanged(string arrayName, DirectApplyState state)
        {
            if (arrayName == "decorations")
            {
                state.DecorationsChanged = true;
            }
            else
            {
                state.EventsChanged = true;
            }
        }

        private static bool TryDecodeLevelEvent(JObject item, out LevelEvent levelEvent, out string conflict)
        {
            levelEvent = null!;
            conflict = string.Empty;
            try
            {
                var dict = GDMiniJSON.Json.Deserialize(item.ToString(Formatting.None)) as Dictionary<string, object>;
                if (dict == null)
                {
                    conflict = "事件数据无法解析";
                    return false;
                }

                levelEvent = new LevelEvent(dict);
                return true;
            }
            catch (Exception ex)
            {
                conflict = $"事件数据解析失败：{ex.Message}";
                return false;
            }
        }

        private static void CopyLevelEvent(LevelEvent source, LevelEvent target)
        {
            target.floor = source.floor;
            target.eventType = source.eventType;
            target.info = source.info;
            target.data = new Dictionary<string, object>(source.data);
            target.disabled = new Dictionary<string, bool>(source.disabled);
            target.isFake = source.isFake;
            target.realEvents = new List<LevelEvent>(source.realEvents);
            target.active = source.active;
            target.visible = source.visible;
            target.locked = source.locked;
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
                case "path.replaceAll":
                    return ApplyPathReplaceAll(root, payload, out conflict);
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

            JToken? newValue = payload["newValue"];
            if (mode == "char")
            {
                string pathData = root.Value<string>("pathData") ?? string.Empty;
                if (index >= pathData.Length)
                {
                    conflict = "轨道修改位置超出当前谱面";
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

            angles[index] = newValue?.DeepClone() ?? JValue.CreateNull();
            return true;
        }

        private static bool ApplyPathReplaceAll(JObject root, JObject payload, out string conflict)
        {
            conflict = string.Empty;
            string mode = payload.Value<string>("mode") ?? "angle";
            JToken? newValue = payload["newValue"];
            if (newValue == null)
            {
                conflict = "轨道替换数据为空";
                return false;
            }

            if (mode == "char")
            {
                root["pathData"] = newValue.Value<string>() ?? string.Empty;
                return true;
            }

            if (!(newValue is JArray newAngles))
            {
                conflict = "轨道角度数据无效";
                return false;
            }

            root["angleData"] = newAngles.DeepClone();
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

            int existingIndex = FindExistingEntityIdIndex(arrayName, array, op.Target);
            if (existingIndex >= 0)
            {
                return true;
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
                return true;
            }

            array.RemoveAt(index);
            return true;
        }

        private static bool ApplyArrayProperties(JObject root, string arrayName, CollabAtomicOperation op, JObject payload, out string conflict)
        {
            conflict = string.Empty;
            JArray array = EnsureArray(root, arrayName);
            int preferredIndex = payload.Value<int?>("index") ?? op.Target.Index;
            int index = FindEntityIndex(arrayName, array, op.Target, null, preferredIndex);
            if (index < 0 || !(array[index] is JObject item))
            {
                JObject? afterItem = payload["afterItem"] as JObject;
                if (afterItem == null)
                {
                    conflict = "目标对象已不存在";
                    return false;
                }

                int insertIndex = Math.Min(Math.Max(preferredIndex, 0), array.Count);
                array.Insert(insertIndex, afterItem.DeepClone());
                return true;
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

        private static int FindExistingEntityIdIndex(string arrayName, JArray array, CollabOperationTarget target)
        {
            string domain = arrayName == "decorations" ? "decoration" : "event";
            return EntityIdRegistry.ResolveIndex(domain, target, array);
        }

        private static bool MatchesTarget(JObject? item, CollabOperationTarget target, JObject? expected)
        {
            if (item == null)
            {
                return false;
            }

            if (expected != null)
            {
                return JToken.DeepEquals(item, expected);
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

        private sealed class DirectApplyState
        {
            public bool PathChanged { get; set; }

            public bool EventsChanged { get; set; }

            public bool DecorationsChanged { get; set; }
        }
    }
}
