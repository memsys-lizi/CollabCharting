using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CollabCharting
{
    internal static class EntityIdRegistry
    {
        private static readonly Dictionary<string, List<EntityRecord>> records =
            new Dictionary<string, List<EntityRecord>>(StringComparer.Ordinal);

        private static int nextId;

        public static void Reset()
        {
            records.Clear();
            nextId = 0;
        }

        public static void InitializeFromCurrentLevel()
        {
            if (!EditorStateAdapter.IsEditorReady)
            {
                Reset();
                return;
            }

            InitializeFromLevelText(EditorStateAdapter.EncodeCurrentLevel());
        }

        public static void InitializeFromLevelText(string levelText)
        {
            Reset();
            if (string.IsNullOrWhiteSpace(levelText))
            {
                return;
            }

            try
            {
                JObject root = JObject.Parse(levelText);
                InitializeDomain("event", (root["actions"] as JArray) ?? new JArray());
                InitializeDomain("decoration", (root["decorations"] as JArray) ?? new JArray());
            }
            catch (Exception ex)
            {
                Main.Mod?.Logger.Warning($"Failed to initialize collab entity ids: {ex.Message}");
                Reset();
            }
        }

        public static void PrepareDiffIds(
            string domain,
            JArray before,
            JArray after,
            out List<string> beforeIds,
            out List<string> afterIds)
        {
            AlignDomainToSnapshot(domain, before);
            beforeIds = GetDomain(domain).Select(record => record.Id).ToList();
            afterIds = MatchAfterIds(domain, before, after, beforeIds);
            CommitDomain(domain, after, afterIds);
        }

        public static int ResolveIndex(string domain, CollabOperationTarget target, JArray array)
        {
            if (string.IsNullOrWhiteSpace(target.EntityId))
            {
                return -1;
            }

            List<EntityRecord> domainRecords = GetDomain(domain);
            for (int i = 0; i < domainRecords.Count && i < array.Count; i++)
            {
                if (string.Equals(domainRecords[i].Id, target.EntityId, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        public static bool TryGetEntityId(string domain, int index, out string entityId)
        {
            entityId = string.Empty;
            List<EntityRecord> domainRecords = GetDomain(domain);
            if (index < 0 || index >= domainRecords.Count)
            {
                return false;
            }

            entityId = domainRecords[index].Id;
            return !string.IsNullOrWhiteSpace(entityId);
        }

        public static bool TryGetEventEntityId(int floor, string eventType, int occurrence, out string entityId)
        {
            entityId = string.Empty;
            if (floor < 0 || string.IsNullOrWhiteSpace(eventType) || occurrence < 0)
            {
                return false;
            }

            int seen = 0;
            foreach (EntityRecord record in GetDomain("event"))
            {
                if (record.Floor != floor ||
                    !string.Equals(record.EventType, eventType, StringComparison.Ordinal))
                {
                    continue;
                }

                if (seen == occurrence)
                {
                    entityId = record.Id;
                    return !string.IsNullOrWhiteSpace(entityId);
                }

                seen++;
            }

            return false;
        }

        public static bool TryGetEntityInfo(string domain, string entityId, out int index, out int floor, out string eventType)
        {
            index = -1;
            floor = -1;
            eventType = string.Empty;
            if (string.IsNullOrWhiteSpace(entityId))
            {
                return false;
            }

            List<EntityRecord> domainRecords = GetDomain(domain);
            for (int i = 0; i < domainRecords.Count; i++)
            {
                EntityRecord record = domainRecords[i];
                if (!string.Equals(record.Id, entityId, StringComparison.Ordinal))
                {
                    continue;
                }

                index = i;
                floor = record.Floor;
                eventType = record.EventType;
                return true;
            }

            return false;
        }

        public static void ApplyBatch(CollabOperationBatch batch, JObject root)
        {
            if (batch == null || root == null)
            {
                return;
            }

            foreach (CollabAtomicOperation op in batch.Ops)
            {
                switch (op.Target.Domain)
                {
                    case "event":
                        ApplyEntityOperation("event", (root["actions"] as JArray) ?? new JArray(), op);
                        break;
                    case "decoration":
                        ApplyEntityOperation("decoration", (root["decorations"] as JArray) ?? new JArray(), op);
                        break;
                }
            }

            ReconcileDomain("event", (root["actions"] as JArray) ?? new JArray());
            ReconcileDomain("decoration", (root["decorations"] as JArray) ?? new JArray());
        }

        private static void InitializeDomain(string domain, JArray array)
        {
            var domainRecords = new List<EntityRecord>();
            for (int i = 0; i < array.Count; i++)
            {
                domainRecords.Add(CreateRecord(domain, CreateInitialId(domain, i, array[i]), array[i]));
            }

            records[domain] = domainRecords;
        }

        private static void AlignDomainToSnapshot(string domain, JArray snapshot)
        {
            List<EntityRecord> domainRecords = GetDomain(domain);
            if (domainRecords.Count != snapshot.Count)
            {
                InitializeDomain(domain, snapshot);
                return;
            }

            for (int i = 0; i < snapshot.Count; i++)
            {
                if (!LooksLikeSameEntity(domainRecords[i], snapshot[i]))
                {
                    InitializeDomain(domain, snapshot);
                    return;
                }
            }
        }

        private static List<string> MatchAfterIds(string domain, JArray before, JArray after, List<string> beforeIds)
        {
            var afterIds = Enumerable.Repeat(string.Empty, after.Count).ToList();
            var usedBefore = new HashSet<int>();

            for (int i = 0; i < after.Count; i++)
            {
                int match = FindExactUnused(before, after[i], usedBefore);
                if (match >= 0)
                {
                    afterIds[i] = beforeIds[match];
                    usedBefore.Add(match);
                }
            }

            if (before.Count == after.Count)
            {
                for (int i = 0; i < after.Count; i++)
                {
                    if (!string.IsNullOrEmpty(afterIds[i]) || usedBefore.Contains(i))
                    {
                        continue;
                    }

                    if (IsSameEntity(before[i] as JObject, after[i] as JObject, domain))
                    {
                        afterIds[i] = beforeIds[i];
                        usedBefore.Add(i);
                    }
                }
            }

            for (int i = 0; i < after.Count; i++)
            {
                if (string.IsNullOrEmpty(afterIds[i]))
                {
                    afterIds[i] = CreateNewId(domain);
                }
            }

            return afterIds;
        }

        private static int FindExactUnused(JArray before, JToken afterItem, HashSet<int> usedBefore)
        {
            for (int i = 0; i < before.Count; i++)
            {
                if (!usedBefore.Contains(i) && JToken.DeepEquals(before[i], afterItem))
                {
                    return i;
                }
            }

            return -1;
        }

        private static void CommitDomain(string domain, JArray array, List<string> ids)
        {
            var domainRecords = new List<EntityRecord>();
            for (int i = 0; i < array.Count; i++)
            {
                string id = i < ids.Count && !string.IsNullOrWhiteSpace(ids[i])
                    ? ids[i]
                    : CreateNewId(domain);
                domainRecords.Add(CreateRecord(domain, id, array[i]));
            }

            records[domain] = domainRecords;
        }

        private static void ApplyEntityOperation(string domain, JArray array, CollabAtomicOperation op)
        {
            List<EntityRecord> domainRecords = GetDomain(domain);
            string kind = op.Kind;
            string entityId = op.Target.EntityId;
            if (string.IsNullOrWhiteSpace(entityId))
            {
                return;
            }

            if (kind.EndsWith(".add", StringComparison.Ordinal))
            {
                int index = Math.Min(Math.Max(op.Payload.Value<int?>("index") ?? array.Count - 1, 0), domainRecords.Count);
                JToken item = op.Payload["item"] ?? (index >= 0 && index < array.Count ? array[index] : new JObject());
                domainRecords.Insert(index, CreateRecord(domain, entityId, item));
            }
            else if (kind.EndsWith(".remove", StringComparison.Ordinal))
            {
                int index = domainRecords.FindIndex(record => string.Equals(record.Id, entityId, StringComparison.Ordinal));
                if (index >= 0)
                {
                    domainRecords.RemoveAt(index);
                }
            }
            else if (kind.EndsWith(".setProperties", StringComparison.Ordinal))
            {
                int index = domainRecords.FindIndex(record => string.Equals(record.Id, entityId, StringComparison.Ordinal));
                if (index >= 0 && index < array.Count)
                {
                    domainRecords[index] = CreateRecord(domain, entityId, array[index]);
                }
                else if (op.Payload["afterItem"] is JObject)
                {
                    int insertIndex = Math.Min(Math.Max(op.Payload.Value<int?>("index") ?? array.Count - 1, 0), domainRecords.Count);
                    JToken item = insertIndex >= 0 && insertIndex < array.Count ? array[insertIndex] : op.Payload["afterItem"]!;
                    domainRecords.Insert(insertIndex, CreateRecord(domain, entityId, item));
                }
            }
            else if (kind.EndsWith(".reorder", StringComparison.Ordinal) && op.Payload["entityIds"] is JArray idArray)
            {
                var byId = domainRecords.ToDictionary(record => record.Id, StringComparer.Ordinal);
                var reordered = new List<EntityRecord>();
                for (int i = 0; i < idArray.Count && i < array.Count; i++)
                {
                    string id = idArray[i]?.Value<string>() ?? string.Empty;
                    reordered.Add(byId.TryGetValue(id, out EntityRecord record)
                        ? CreateRecord(domain, record.Id, array[i])
                        : CreateRecord(domain, CreateNewId(domain), array[i]));
                }

                records[domain] = reordered;
            }
        }

        private static void ReconcileDomain(string domain, JArray array)
        {
            List<EntityRecord> domainRecords = GetDomain(domain);
            if (domainRecords.Count != array.Count)
            {
                InitializeDomain(domain, array);
                return;
            }

            for (int i = 0; i < array.Count; i++)
            {
                domainRecords[i] = CreateRecord(domain, domainRecords[i].Id, array[i]);
            }
        }

        private static List<EntityRecord> GetDomain(string domain)
        {
            if (!records.TryGetValue(domain, out List<EntityRecord> domainRecords))
            {
                domainRecords = new List<EntityRecord>();
                records[domain] = domainRecords;
            }

            return domainRecords;
        }

        private static EntityRecord CreateRecord(string domain, string id, JToken token)
        {
            JObject? item = token as JObject;
            return new EntityRecord
            {
                Id = id,
                Hash = OperationDiffUtility.HashToken(token),
                Floor = item?.Value<int?>("floor") ?? -1,
                EventType = item?.Value<string>("eventType") ?? string.Empty,
                Domain = domain
            };
        }

        private static bool LooksLikeSameEntity(EntityRecord record, JToken token)
        {
            JObject? item = token as JObject;
            if (item == null)
            {
                return false;
            }

            if (record.Hash == OperationDiffUtility.HashToken(token))
            {
                return true;
            }

            if (!string.Equals(record.EventType, item.Value<string>("eventType"), StringComparison.Ordinal))
            {
                return false;
            }

            return record.Domain != "decoration" && record.Floor == (item.Value<int?>("floor") ?? -1);
        }

        private static bool IsSameEntity(JObject? before, JObject? after, string domain)
        {
            if (before == null || after == null)
            {
                return false;
            }

            if (domain == "decoration")
            {
                return string.Equals(before.Value<string>("eventType"), after.Value<string>("eventType"), StringComparison.Ordinal);
            }

            return before.Value<int?>("floor") == after.Value<int?>("floor") &&
                   string.Equals(before.Value<string>("eventType"), after.Value<string>("eventType"), StringComparison.Ordinal);
        }

        private static string CreateInitialId(string domain, int index, JToken token)
        {
            string hash = OperationDiffUtility.HashToken(token);
            string prefix = hash.Length > 10 ? hash.Substring(0, 10) : hash;
            return $"{domain}:initial:{index}:{prefix}";
        }

        private static string CreateNewId(string domain)
        {
            nextId++;
            return $"{domain}:runtime:{nextId}:{Guid.NewGuid():N}";
        }

        private sealed class EntityRecord
        {
            public string Domain { get; set; } = string.Empty;

            public string Id { get; set; } = string.Empty;

            public string Hash { get; set; } = string.Empty;

            public int Floor { get; set; }

            public string EventType { get; set; } = string.Empty;
        }
    }
}
