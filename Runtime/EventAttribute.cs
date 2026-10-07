using System;

namespace Unimetry
{
    /// <summary>
    /// Marks a method so the Unity IL post-processor records an OpenTelemetry event around its body.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class EventAttribute : Attribute
    {
        /// <summary>
        /// Initializes the attribute using <c>{TypeName}.{MethodName}</c> as the event name.
        /// </summary>
        public EventAttribute()
        {
        }

        /// <summary>
        /// Initializes the attribute with an explicit event name.
        /// </summary>
        /// <param name="name">Event name stored in the OTLP <c>eventName</c> field.</param>
        public EventAttribute(string name)
        {
            Name = name;
        }

        /// <summary>
        /// Gets the explicit event name, or <see langword="null"/> when the default name is used.
        /// </summary>
        public string Name { get; }
    }
}
