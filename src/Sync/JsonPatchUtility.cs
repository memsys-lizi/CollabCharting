using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CollabCharting
{
    internal static class JsonPatchUtility
    {
        public static List<JsonDiffOperation> CreateDiff(string beforeJson, string afterJson)
        {
            JToken before = JToken.Parse(beforeJson);
            JToken after = JToken.Parse(afterJson);
            var operations = new List<JsonDiffOperation>();
            DiffTokens(before, after, string.Empty, operations);
            return operations;
        }

        public static bool TryApply(
            string currentJson,
            IEnumerable<JsonDiffOperation> operations,
            bool reverse,
            out string patchedJson,
            out string conflict)
        {
            JToken root = JToken.Parse(currentJson);
            foreach (JsonDiffOperation operation in operations)
            {
                bool expectedExists = reverse ? operation.NewExists : operation.OldExists;
                JToken? expectedValue = reverse ? operation.NewValue : operation.OldValue;
                bool desiredExists = reverse ? operation.OldExists : operation.NewExists;
                JToken? desiredValue = reverse ? operation.OldValue : operation.NewValue;

                if (!TryGetToken(root, operation.Path, out JToken? currentToken))
                {
                    currentToken = null;
                }

                if (expectedExists)
                {
                    if (currentToken == null || !JToken.DeepEquals(currentToken, expectedValue))
                    {
                        patchedJson = currentJson;
                        conflict = $"路径 {operation.Path} 已被其他操作修改";
                        return false;
                    }
                }
                else if (currentToken != null)
                {
                    patchedJson = currentJson;
                    conflict = $"路径 {operation.Path} 已存在，无法应用历史";
                    return false;
                }

                if (!SetToken(ref root, operation.Path, desiredExists ? desiredValue?.DeepClone() : null, desiredExists))
                {
                    patchedJson = currentJson;
                    conflict = $"路径 {operation.Path} 无法写入";
                    return false;
                }
            }

            patchedJson = root.ToString(Formatting.None);
            conflict = string.Empty;
            return true;
        }

        private static void DiffTokens(JToken before, JToken after, string path, List<JsonDiffOperation> operations)
        {
            if (JToken.DeepEquals(before, after))
            {
                return;
            }

            if (before.Type == JTokenType.Object && after.Type == JTokenType.Object)
            {
                JObject beforeObject = (JObject)before;
                JObject afterObject = (JObject)after;
                var names = new HashSet<string>(beforeObject.Properties().Select(p => p.Name), StringComparer.Ordinal);
                names.UnionWith(afterObject.Properties().Select(p => p.Name));

                foreach (string name in names.OrderBy(name => name, StringComparer.Ordinal))
                {
                    JProperty? beforeProperty = beforeObject.Property(name);
                    JProperty? afterProperty = afterObject.Property(name);
                    string childPath = AppendPath(path, name);
                    if (beforeProperty == null || afterProperty == null)
                    {
                        operations.Add(new JsonDiffOperation
                        {
                            Path = childPath,
                            OldExists = beforeProperty != null,
                            OldValue = beforeProperty?.Value.DeepClone(),
                            NewExists = afterProperty != null,
                            NewValue = afterProperty?.Value.DeepClone()
                        });
                        continue;
                    }

                    DiffTokens(beforeProperty.Value, afterProperty.Value, childPath, operations);
                }

                return;
            }

            operations.Add(new JsonDiffOperation
            {
                Path = path,
                OldExists = true,
                OldValue = before.DeepClone(),
                NewExists = true,
                NewValue = after.DeepClone()
            });
        }

        private static bool TryGetToken(JToken root, string path, out JToken? token)
        {
            token = root;
            if (string.IsNullOrEmpty(path))
            {
                return true;
            }

            foreach (string segment in SplitPath(path))
            {
                if (token is JObject obj)
                {
                    token = obj.Property(segment)?.Value;
                }
                else if (token is JArray array && int.TryParse(segment, out int index) && index >= 0 && index < array.Count)
                {
                    token = array[index];
                }
                else
                {
                    token = null;
                }

                if (token == null)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool SetToken(ref JToken root, string path, JToken? value, bool exists)
        {
            if (string.IsNullOrEmpty(path))
            {
                if (!exists || value == null)
                {
                    return false;
                }

                root = value;
                return true;
            }

            string[] segments = SplitPath(path).ToArray();
            JToken parent = root;
            for (int i = 0; i < segments.Length - 1; i++)
            {
                if (parent is JObject obj)
                {
                    parent = obj.Property(segments[i])?.Value ?? new JObject();
                }
                else if (parent is JArray array && int.TryParse(segments[i], out int index) && index >= 0 && index < array.Count)
                {
                    parent = array[index];
                }
                else
                {
                    return false;
                }
            }

            string last = segments[segments.Length - 1];
            if (parent is JObject parentObject)
            {
                if (exists)
                {
                    parentObject[last] = value;
                }
                else
                {
                    parentObject.Property(last)?.Remove();
                }

                return true;
            }

            if (parent is JArray parentArray && int.TryParse(last, out int arrayIndex))
            {
                if (exists)
                {
                    if (value == null)
                    {
                        return false;
                    }

                    if (arrayIndex == parentArray.Count)
                    {
                        parentArray.Add(value);
                    }
                    else if (arrayIndex >= 0 && arrayIndex < parentArray.Count)
                    {
                        parentArray[arrayIndex] = value;
                    }
                    else
                    {
                        return false;
                    }
                }
                else if (arrayIndex >= 0 && arrayIndex < parentArray.Count)
                {
                    parentArray.RemoveAt(arrayIndex);
                }
                else
                {
                    return false;
                }

                return true;
            }

            return false;
        }

        private static string AppendPath(string path, string segment)
        {
            string escaped = segment.Replace("~", "~0").Replace("/", "~1");
            return string.IsNullOrEmpty(path) ? "/" + escaped : path + "/" + escaped;
        }

        private static IEnumerable<string> SplitPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                yield break;
            }

            foreach (string segment in path.TrimStart('/').Split('/'))
            {
                yield return segment.Replace("~1", "/").Replace("~0", "~");
            }
        }
    }
}
