using System;

namespace Unimetry
{
    /// <summary>
    /// Marks a parameter of an <see cref="EventAttribute"/> method as an event attribute.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
    public sealed class EventTagAttribute : Attribute
    {
        /// <summary>
        /// Initializes the attribute using the parameter name as the attribute key.
        /// </summary>
        public EventTagAttribute()
        {
        }

        /// <summary>
        /// Initializes the attribute with an explicit attribute key.
        /// </summary>
        /// <param name="name">Attribute key recorded on the event.</param>
        public EventTagAttribute(string name)
        {
            Name = name;
        }

        /// <summary>
        /// Gets the explicit attribute key, or <see langword="null"/> when the parameter name is used.
        /// </summary>
        public string Name { get; }
    }
}
