using System;

namespace ADOFAIWebBridge
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class WebCommandAttribute : Attribute
    {
        public string Name { get; }

        public WebCommandAttribute(string name)
        {
            Name = name;
        }
    }
}
