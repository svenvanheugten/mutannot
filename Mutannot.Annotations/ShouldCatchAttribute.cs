using System;

namespace Mutannot.Annotations
{
    /// <summary>
    /// Patch, generated with `git diff`, that should cause the test to fail.
    /// You can verify that the test _actually_ fails when the patch is applied with `mutannot` (https://github.com/svenvanheugten/mutannot).
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
    public class ShouldCatchAttribute : Attribute
    {
        public ShouldCatchAttribute(string patch) => Patch = patch;

        public string Patch { get; }
    }
}
