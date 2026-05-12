using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ADOFAIWebBridge
{
    internal sealed class CommandRegistry
    {
        private readonly Dictionary<string, CommandDescriptor> commands =
            new Dictionary<string, CommandDescriptor>(StringComparer.Ordinal);

        public void Register(Type type)
        {
            Register(type, null);
        }

        public void Register(object target)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            Register(target.GetType(), target);
        }

        public void RegisterDelegate(string name, Func<JToken?, WebCommandContext, object?> handler)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new WebBridgeException("invalid_command", "Command name is required.");
            }

            if (commands.ContainsKey(name))
            {
                throw new WebBridgeException("duplicate_command", $"Duplicate web command: {name}");
            }

            commands.Add(name, new CommandDescriptor(name, handler));
        }

        public bool Contains(string name)
        {
            return commands.ContainsKey(name);
        }

        public async Task<object?> InvokeAsync(string name, JToken? parameters, WebCommandContext context)
        {
            if (!commands.TryGetValue(name, out CommandDescriptor descriptor))
            {
                throw new WebBridgeException("unknown_command", $"Unknown command: {name}");
            }

            if (descriptor.Handler != null)
            {
                object? delegateResult = descriptor.Handler(parameters, context);
                return await UnwrapTaskAsync(delegateResult).ConfigureAwait(false);
            }

            object?[] args = BuildArguments(descriptor.Method!, parameters, context);
            object? result = descriptor.Method!.Invoke(descriptor.Target, args);
            return await UnwrapTaskAsync(result).ConfigureAwait(false);
        }

        private static async Task<object?> UnwrapTaskAsync(object? result)
        {
            if (result is Task task)
            {
                await task.ConfigureAwait(false);
                Type taskType = task.GetType();
                if (taskType.IsGenericType)
                {
                    return taskType.GetProperty("Result")?.GetValue(task);
                }

                return null;
            }

            return result;
        }

        private void Register(Type type, object? target)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;

            foreach (MethodInfo method in type.GetMethods(flags))
            {
                WebCommandAttribute? attr = method.GetCustomAttribute<WebCommandAttribute>();
                if (attr == null)
                {
                    continue;
                }

                if (!method.IsStatic && target == null)
                {
                    throw new WebBridgeException(
                        "invalid_command",
                        $"Command {attr.Name} is an instance method. Register an object instance instead of a Type.");
                }

                if (commands.ContainsKey(attr.Name))
                {
                    throw new WebBridgeException("duplicate_command", $"Duplicate web command: {attr.Name}");
                }

                commands.Add(attr.Name, new CommandDescriptor(attr.Name, method, method.IsStatic ? null : target));
            }
        }

        private static object?[] BuildArguments(MethodInfo method, JToken? parameters, WebCommandContext context)
        {
            ParameterInfo[] methodParams = method.GetParameters();
            if (methodParams.Length == 0)
            {
                return Array.Empty<object?>();
            }

            if (methodParams.Length == 1)
            {
                Type paramType = methodParams[0].ParameterType;
                if (paramType == typeof(WebCommandContext))
                {
                    return new object?[] { context };
                }

                return new object?[] { parameters == null ? GetDefault(paramType) : parameters.ToObject(paramType) };
            }

            JObject? obj = parameters as JObject;
            return methodParams.Select(param =>
            {
                if (param.ParameterType == typeof(WebCommandContext))
                {
                    return context;
                }

                JToken? token = obj?[param.Name ?? string.Empty];
                return token == null ? GetDefault(param.ParameterType) : token.ToObject(param.ParameterType);
            }).ToArray();
        }

        private static object? GetDefault(Type type)
        {
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }

        private sealed class CommandDescriptor
        {
            public string Name { get; }

            public MethodInfo? Method { get; }

            public object? Target { get; }

            public Func<JToken?, WebCommandContext, object?>? Handler { get; }

            public CommandDescriptor(string name, MethodInfo method, object? target)
            {
                Name = name;
                Method = method;
                Target = target;
            }

            public CommandDescriptor(string name, Func<JToken?, WebCommandContext, object?> handler)
            {
                Name = name;
                Handler = handler;
            }
        }
    }
}
