using System;
using System.ComponentModel;

namespace Unimetry
{
    /// <summary>
    /// Marks a method whose body has already been woven with an event scope.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public sealed class EventWovenAttribute : Attribute
    {
    }
}
