using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CollabCharting
{
    internal static class OperationDiffUtility
    {
        public static bool TryCreateBatch(string beforeLevelText, string afterLevelText, string reason, out CollabOperationBatch batch)
        {
            batch = new CollabOperationBatch
            {
                OperationId = Guid.NewGuid().ToString("N"),
                Reason = reason
            };

            if (string.IsNullOrWhiteSpace(beforeLevelText) || string.IsNullOrWhiteSpace(afterLevelText))
            {
                return false;
            }

            JObject before = JObject.Parse(beforeLevelText);
            JObject after = JObject.Parse(afterLevelText);

            AddPathOperations(before, after, batch);
            AddSettingsOperations(before, after, batch);
            AddEventOperations(before, after, batch);
            AddDecorationOperations(before, after, batch);

            return batch.Ops.Count > 0;
        }

        public static string HashToken(JToken? token)
        {
            return EditorStateAdapter.HashLevelText(token?.ToString(Formatting.None) ?? string.Empty);
        }

        public static CollabOperationBatch CreateInverse(CollabOperationBatch source, string reason)
        {
            var inverse = new CollabOperationBatch
            {
                OperationId = Guid.NewGuid().ToString("N"),
                BaseRevision = source.Revision,
                AuthorSteamId = source.AuthorSteamId,
                AuthorName = source.AuthorName,
                Reason = reason
            };

            for (int i = source.Ops.Count - 1; i >= 0; i--)
            {
                CollabAtomicOperation op = source.Ops[i];
                inverse.Ops.Add(new CollabAtomicOperation
                {
                    Kind = InvertKind(op.Kind),
                    Target = CloneTarget(op.Target),
                    BeforeHash = string.Empty,
                    Payload = CloneObject(op.InversePayload),
                    InversePayload = CloneObject(op.Payload)
                });
            }

            return inverse;
        }

        public static CollabOperationBatch CloneBatch(CollabOperationBatch source)
        {
            return new CollabOperationBatch
            {
                OperationId = source.OperationId,
                BaseRevision = source.BaseRevision,
                Revision = source.Revision,
                AuthorSteamId = source.AuthorSteamId,
                AuthorName = source.AuthorName,
                Reason = source.Reason,
                Undone = source.Undone,
                RequiredFiles = source.RequiredFiles
                    .Select(file => new ResourceManifestEntry
                    {
                        RelativePath = file.RelativePath,
                        Size = file.Size,
                        Sha256 = file.Sha256
                    })
                    .ToList(),
                Ops = source.Ops.Select(CloneOperation).ToList()
            };
        }

        public static bool TransformForAcceptedOperations(CollabOperationBatch batch, IEnumerable<CollabOperationBatch> accepted, out string conflict)
        {
            conflict = string.Empty;
            foreach (CollabOperationBatch acceptedBatch in accepted)
            {
                if (acceptedBatch.Revision <= batch.BaseRevision)
                {
                    continue;
                }

                foreach (CollabAtomicOperation acceptedOp in acceptedBatch.Ops)
                {
                    if (acceptedOp.Kind == "path.insertFloor")
                    {
                        int index = acceptedOp.Payload.Value<int?>("index") ?? -1;
                        ShiftFloorTargets(batch, index, 1);
                    }
                    else if (acceptedOp.Kind == "path.deleteFloors")
                    {
                        int index = acceptedOp.Payload.Value<int?>("index") ?? -1;
                        int count = acceptedOp.Payload.Value<int?>("count") ?? 1;
                        if (!ShiftFloorTargetsForDelete(batch, index, count, out conflict))
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        private static CollabAtomicOperation CloneOperation(CollabAtomicOperation op)
        {
            return new CollabAtomicOperation
            {
                Kind = op.Kind,
                Target = CloneTarget(op.Target),
                BeforeHash = op.BeforeHash,
                Payload = CloneObject(op.Payload),
                InversePayload = CloneObject(op.InversePayload)
            };
        }

        private static string InvertKind(string kind)
        {
            switch (kind)
            {
                case "path.insertFloor":
                    return "path.deleteFloors";
                case "path.deleteFloors":
                    return "path.insertFloor";
                case "event.add":
                    return "event.remove";
                case "event.remove":
                    return "event.add";
                case "decoration.add":
                    return "decoration.remove";
                case "decoration.remove":
                    return "decoration.add";
                default:
                    return kind;
            }
        }

        private static CollabOperationTarget CloneTarget(CollabOperationTarget target)
        {
            return new CollabOperationTarget
            {
                Domain = target.Domain,
                EntityId = target.EntityId,
                Floor = target.Floor,
                EventType = target.EventType,
                Index = target.Index
            };
        }

        private static JObject CloneObject(JObject obj)
        {
            return (JObject)obj.DeepClone();
        }

        private static void AddPathOperations(JObject before, JObject after, CollabOperationBatch batch)
        {
            JArray? beforeAngles = before["angleData"] as JArray;
            JArray? afterAngles = after["angleData"] as JArray;
            if (beforeAngles != null && afterAngles != null && !JToken.DeepEquals(beforeAngles, afterAngles))
            {
                AddArrayPathOperations(beforeAngles, afterAngles, "angle", batch);
                return;
            }

            string? beforePath = before.Value<string>("pathData");
            string? afterPath = after.Value<string>("pathData");
            if (beforePath != null && afterPath != null && !string.Equals(beforePath, afterPath, StringComparison.Ordinal))
            {
                AddStringPathOperations(beforePath, afterPath, batch);
            }
        }

        private static void AddArrayPathOperations(JArray before, JArray after, string mode, CollabOperationBatch batch)
        {
            if (TryFindSingleInsert(before, after, out int insertIndex))
            {
                AddOperation(batch, "path.insertFloor", Target("path", "angleData", -1, string.Empty, insertIndex),
                    new JObject
                    {
                        ["mode"] = mode,
                        ["index"] = insertIndex,
                        ["value"] = after[insertIndex]?.DeepClone()
                    },
                    new JObject
                    {
                        ["mode"] = mode,
                        ["index"] = insertIndex,
                        ["count"] = 1,
                        ["values"] = new JArray((after[insertIndex] ?? JValue.CreateNull()).DeepClone())
                    },
                    before);
                return;
            }

            if (TryFindContiguousDelete(before, after, out int deleteIndex, out int deleteCount))
            {
                var deleted = new JArray();
                for (int i = 0; i < deleteCount; i++)
                {
                    deleted.Add((before[deleteIndex + i] ?? JValue.CreateNull()).DeepClone());
                }

                AddOperation(batch, "path.deleteFloors", Target("path", "angleData", -1, string.Empty, deleteIndex),
                    new JObject
                    {
                        ["mode"] = mode,
                        ["index"] = deleteIndex,
                        ["count"] = deleteCount,
                        ["values"] = deleted.DeepClone()
                    },
                    new JObject
                    {
                        ["mode"] = mode,
                        ["index"] = deleteIndex,
                        ["values"] = deleted.DeepClone()
                    },
                    before);
                return;
            }

            int count = Math.Min(before.Count, after.Count);
            for (int i = 0; i < count; i++)
            {
                if (JToken.DeepEquals(before[i], after[i]))
                {
                    continue;
                }

                AddOperation(batch, "path.setFloor", Target("path", "angleData", -1, string.Empty, i),
                    new JObject
                    {
                        ["mode"] = mode,
                        ["index"] = i,
                        ["oldValue"] = before[i]?.DeepClone(),
                        ["newValue"] = after[i]?.DeepClone()
                    },
                    new JObject
                    {
                        ["mode"] = mode,
                        ["index"] = i,
                        ["oldValue"] = after[i]?.DeepClone(),
                        ["newValue"] = before[i]?.DeepClone()
                    },
                    before[i]);
            }
        }

        private static void AddStringPathOperations(string before, string after, CollabOperationBatch batch)
        {
            if (after.Length == before.Length + 1 && TryFindStringInsert(before, after, out int insertIndex))
            {
                AddOperation(batch, "path.insertFloor", Target("path", "pathData", -1, string.Empty, insertIndex),
                    new JObject
                    {
                        ["mode"] = "char",
                        ["index"] = insertIndex,
                        ["value"] = after[insertIndex].ToString()
                    },
                    new JObject
                    {
                        ["mode"] = "char",
                        ["index"] = insertIndex,
                        ["count"] = 1,
                        ["values"] = new JArray(after[insertIndex].ToString())
                    },
                    new JValue(before));
                return;
            }

            if (before.Length > after.Length && TryFindStringDelete(before, after, out int deleteIndex, out int deleteCount))
            {
                string deleted = before.Substring(deleteIndex, deleteCount);
                AddOperation(batch, "path.deleteFloors", Target("path", "pathData", -1, string.Empty, deleteIndex),
                    new JObject
                    {
                        ["mode"] = "char",
                        ["index"] = deleteIndex,
                        ["count"] = deleteCount,
                        ["values"] = deleted
                    },
                    new JObject
                    {
                        ["mode"] = "char",
                        ["index"] = deleteIndex,
                        ["values"] = deleted
                    },
                    new JValue(before));
                return;
            }

            int count = Math.Min(before.Length, after.Length);
            for (int i = 0; i < count; i++)
            {
                if (before[i] == after[i])
                {
                    continue;
                }

                AddOperation(batch, "path.setFloor", Target("path", "pathData", -1, string.Empty, i),
                    new JObject
                    {
                        ["mode"] = "char",
                        ["index"] = i,
                        ["oldValue"] = before[i].ToString(),
                        ["newValue"] = after[i].ToString()
                    },
                    new JObject
                    {
                        ["mode"] = "char",
                        ["index"] = i,
                        ["oldValue"] = after[i].ToString(),
                        ["newValue"] = before[i].ToString()
                    },
                    new JValue(before[i].ToString()));
            }
        }

        private static void AddSettingsOperations(JObject before, JObject after, CollabOperationBatch batch)
        {
            JObject beforeSettings = (before["settings"] as JObject) ?? new JObject();
            JObject afterSettings = (after["settings"] as JObject) ?? new JObject();
            List<CollabPropertyChange> changes = CreatePropertyChanges(beforeSettings, afterSettings);
            if (changes.Count == 0)
            {
                return;
            }

            AddOperation(batch, "settings.setProperties", Target("settings", "settings", -1, string.Empty, -1),
                new JObject { ["changes"] = JToken.FromObject(changes) },
                new JObject { ["changes"] = JToken.FromObject(InvertChanges(changes)) },
                beforeSettings);
        }

        private static void AddEventOperations(JObject before, JObject after, CollabOperationBatch batch)
        {
            JArray beforeActions = (before["actions"] as JArray) ?? new JArray();
            JArray afterActions = (after["actions"] as JArray) ?? new JArray();
            AddEntityArrayOperations(beforeActions, afterActions, "event", batch);
        }

        private static void AddDecorationOperations(JObject before, JObject after, CollabOperationBatch batch)
        {
            JArray beforeDecorations = (before["decorations"] as JArray) ?? new JArray();
            JArray afterDecorations = (after["decorations"] as JArray) ?? new JArray();
            AddEntityArrayOperations(beforeDecorations, afterDecorations, "decoration", batch);
        }

        private static void AddEntityArrayOperations(JArray before, JArray after, string domain, CollabOperationBatch batch)
        {
            EntityIdRegistry.PrepareDiffIds(domain, before, after, out List<string> beforeIds, out List<string> afterIds);
            if (domain == "decoration" &&
                beforeIds.Count == afterIds.Count &&
                beforeIds.Count > 0 &&
                !beforeIds.SequenceEqual(afterIds) &&
                beforeIds.OrderBy(id => id, StringComparer.Ordinal).SequenceEqual(afterIds.OrderBy(id => id, StringComparer.Ordinal)))
            {
                AddOperation(batch, domain + ".reorder", Target(domain, domain, -1, string.Empty, -1),
                    new JObject
                    {
                        ["items"] = after.DeepClone(),
                        ["entityIds"] = JToken.FromObject(afterIds)
                    },
                    new JObject
                    {
                        ["items"] = before.DeepClone(),
                        ["entityIds"] = JToken.FromObject(beforeIds)
                    },
                    before);
            }

            var beforeIndexById = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < beforeIds.Count; i++)
            {
                beforeIndexById[beforeIds[i]] = i;
            }

            var afterIndexById = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < afterIds.Count; i++)
            {
                afterIndexById[afterIds[i]] = i;
            }

            foreach (KeyValuePair<string, int> pair in beforeIndexById)
            {
                if (afterIndexById.ContainsKey(pair.Key))
                {
                    continue;
                }

                JObject? item = before[pair.Value] as JObject;
                if (item == null)
                {
                    continue;
                }

                AddOperation(batch, domain + ".remove", CreateTarget(item, domain, pair.Value, pair.Key),
                    new JObject { ["index"] = pair.Value, ["item"] = item.DeepClone() },
                    new JObject { ["index"] = pair.Value, ["item"] = item.DeepClone() },
                    item);
            }

            foreach (KeyValuePair<string, int> pair in afterIndexById)
            {
                JObject? afterObject = after[pair.Value] as JObject;
                if (afterObject == null)
                {
                    continue;
                }

                if (!beforeIndexById.TryGetValue(pair.Key, out int beforeIndex))
                {
                    AddOperation(batch, domain + ".add", CreateTarget(afterObject, domain, pair.Value, pair.Key),
                        new JObject { ["index"] = pair.Value, ["item"] = afterObject.DeepClone() },
                        new JObject { ["index"] = pair.Value, ["item"] = afterObject.DeepClone() },
                        null);
                    continue;
                }

                JObject? beforeObject = before[beforeIndex] as JObject;
                if (beforeObject == null || JToken.DeepEquals(beforeObject, afterObject))
                {
                    continue;
                }

                List<CollabPropertyChange> changes = CreatePropertyChanges(beforeObject, afterObject);
                if (changes.Count == 0)
                {
                    continue;
                }

                AddOperation(batch, domain + ".setProperties", CreateTarget(afterObject, domain, pair.Value, pair.Key),
                    new JObject { ["index"] = pair.Value, ["changes"] = JToken.FromObject(changes) },
                    new JObject { ["index"] = beforeIndex, ["changes"] = JToken.FromObject(InvertChanges(changes)) },
                    beforeObject);
            }
        }

        private static List<CollabPropertyChange> CreatePropertyChanges(JObject before, JObject after)
        {
            var changes = new List<CollabPropertyChange>();
            var names = new HashSet<string>(before.Properties().Select(p => p.Name), StringComparer.Ordinal);
            names.UnionWith(after.Properties().Select(p => p.Name));
            foreach (string name in names.OrderBy(name => name, StringComparer.Ordinal))
            {
                JProperty? beforeProperty = before.Property(name);
                JProperty? afterProperty = after.Property(name);
                if (beforeProperty != null && afterProperty != null && JToken.DeepEquals(beforeProperty.Value, afterProperty.Value))
                {
                    continue;
                }

                changes.Add(new CollabPropertyChange
                {
                    Path = name,
                    OldExists = beforeProperty != null,
                    OldValue = beforeProperty?.Value.DeepClone(),
                    NewExists = afterProperty != null,
                    NewValue = afterProperty?.Value.DeepClone()
                });
            }

            return changes;
        }

        private static List<CollabPropertyChange> InvertChanges(IEnumerable<CollabPropertyChange> changes)
        {
            return changes.Select(change => new CollabPropertyChange
            {
                Path = change.Path,
                OldExists = change.NewExists,
                OldValue = change.NewValue?.DeepClone(),
                NewExists = change.OldExists,
                NewValue = change.OldValue?.DeepClone()
            }).ToList();
        }

        private static CollabOperationTarget CreateTarget(JObject item, string domain, int index, string entityId)
        {
            string eventType = item.Value<string>("eventType") ?? string.Empty;
            int floor = item.Value<int?>("floor") ?? -1;
            return Target(domain, entityId, floor, eventType, index);
        }

        private static CollabOperationTarget Target(string domain, string entityId, int floor, string eventType, int index)
        {
            return new CollabOperationTarget
            {
                Domain = domain,
                EntityId = entityId,
                Floor = floor,
                EventType = eventType,
                Index = index
            };
        }

        private static void AddOperation(
            CollabOperationBatch batch,
            string kind,
            CollabOperationTarget target,
            JObject payload,
            JObject inversePayload,
            JToken? beforeToken)
        {
            batch.Ops.Add(new CollabAtomicOperation
            {
                Kind = kind,
                Target = target,
                BeforeHash = HashToken(beforeToken),
                Payload = payload,
                InversePayload = inversePayload
            });
        }

        private static bool IsSameEntity(JObject before, JObject after, string domain)
        {
            if (domain == "decoration")
            {
                return string.Equals(before.Value<string>("eventType"), after.Value<string>("eventType"), StringComparison.Ordinal);
            }

            return before.Value<int?>("floor") == after.Value<int?>("floor") &&
                   string.Equals(before.Value<string>("eventType"), after.Value<string>("eventType"), StringComparison.Ordinal);
        }

        private static bool IsSameMultisetDifferentOrder(JArray before, JArray after)
        {
            if (before.Count != after.Count || JToken.DeepEquals(before, after))
            {
                return false;
            }

            var beforeHashes = before.Select(HashToken).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var afterHashes = after.Select(HashToken).OrderBy(x => x, StringComparer.Ordinal).ToList();
            return beforeHashes.SequenceEqual(afterHashes);
        }

        private static bool TryFindSingleInsert(JArray before, JArray after, out int index)
        {
            index = -1;
            if (after.Count != before.Count + 1)
            {
                return false;
            }

            int i = 0;
            while (i < before.Count && JToken.DeepEquals(before[i], after[i]))
            {
                i++;
            }

            index = i;
            while (i < before.Count && JToken.DeepEquals(before[i], after[i + 1]))
            {
                i++;
            }

            return i == before.Count;
        }

        private static bool TryFindContiguousDelete(JArray before, JArray after, out int index, out int count)
        {
            index = -1;
            count = before.Count - after.Count;
            if (count <= 0)
            {
                return false;
            }

            int i = 0;
            while (i < after.Count && JToken.DeepEquals(before[i], after[i]))
            {
                i++;
            }

            index = i;
            while (i < after.Count && JToken.DeepEquals(before[i + count], after[i]))
            {
                i++;
            }

            return i == after.Count;
        }

        private static bool TryFindStringInsert(string before, string after, out int index)
        {
            index = 0;
            while (index < before.Length && before[index] == after[index])
            {
                index++;
            }

            for (int i = index; i < before.Length; i++)
            {
                if (before[i] != after[i + 1])
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryFindStringDelete(string before, string after, out int index, out int count)
        {
            index = 0;
            count = before.Length - after.Length;
            if (count <= 0)
            {
                return false;
            }

            while (index < after.Length && before[index] == after[index])
            {
                index++;
            }

            for (int i = index; i < after.Length; i++)
            {
                if (before[i + count] != after[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static void ShiftFloorTargets(CollabOperationBatch batch, int index, int delta)
        {
            if (index < 0)
            {
                return;
            }

            foreach (CollabAtomicOperation op in batch.Ops)
            {
                if (op.Target.Floor >= index)
                {
                    op.Target.Floor += delta;
                    ShiftPayloadFloor(op.Payload, index, delta);
                    ShiftPayloadFloor(op.InversePayload, index, delta);
                }
            }
        }

        private static bool ShiftFloorTargetsForDelete(CollabOperationBatch batch, int index, int count, out string conflict)
        {
            conflict = string.Empty;
            if (index < 0 || count <= 0)
            {
                return true;
            }

            foreach (CollabAtomicOperation op in batch.Ops)
            {
                if (op.Target.Floor >= index && op.Target.Floor < index + count)
                {
                    conflict = $"目标 floor {op.Target.Floor} 已被其他玩家删除";
                    return false;
                }

                if (op.Target.Floor >= index + count)
                {
                    op.Target.Floor -= count;
                    ShiftPayloadFloor(op.Payload, index + count, -count);
                    ShiftPayloadFloor(op.InversePayload, index + count, -count);
                }
            }

            return true;
        }

        private static void ShiftPayloadFloor(JObject payload, int threshold, int delta)
        {
            JToken? item = payload["item"];
            if (item is JObject itemObject)
            {
                int floor = itemObject.Value<int?>("floor") ?? -1;
                if (floor >= threshold)
                {
                    itemObject["floor"] = floor + delta;
                }
            }

            JToken? changesToken = payload["changes"];
            if (changesToken == null)
            {
                return;
            }

            List<CollabPropertyChange> changes = changesToken.ToObject<List<CollabPropertyChange>>() ?? new List<CollabPropertyChange>();
            foreach (CollabPropertyChange change in changes)
            {
                if (change.Path != "floor")
                {
                    continue;
                }

                if (change.OldValue != null && change.OldValue.Type == JTokenType.Integer && change.OldValue.Value<int>() >= threshold)
                {
                    change.OldValue = change.OldValue.Value<int>() + delta;
                }

                if (change.NewValue != null && change.NewValue.Type == JTokenType.Integer && change.NewValue.Value<int>() >= threshold)
                {
                    change.NewValue = change.NewValue.Value<int>() + delta;
                }
            }

            payload["changes"] = JToken.FromObject(changes);
        }
    }
}
