using System;

namespace Mutannot
{
    /// <summary>
    /// Patch, generated with <c>git diff</c>, that should cause the test to fail.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
    public class ShouldCatchAttribute : Attribute
    {
        public ShouldCatchAttribute(string patch) { }
    }
}
