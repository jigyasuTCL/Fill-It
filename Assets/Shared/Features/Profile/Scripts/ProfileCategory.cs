using System;
using System.Collections.Generic;

namespace Shared.Profile
{
    /// <summary>
    /// A titled group of items inside one tab - the "Classic" band of frames in the profile
    /// picker, and whatever bands follow it.
    ///
    /// Grouping lives in the catalog rather than in the UI because it is content: which band an
    /// item belongs to is a design decision made alongside authoring the art, not something the
    /// panel should be inferring from ids or unlock kinds.
    /// </summary>
    [Serializable]
    public sealed class ProfileCategory
    {
        /// <summary>
        /// Shown as the section header. Blank is legitimate - an untitled category renders its
        /// items with no header, which is how to author a single ungrouped run.
        /// </summary>
        public string Title = string.Empty;

        public List<ProfileItemDefinition> Items = new List<ProfileItemDefinition>();

        /// <summary>
        /// Hides the whole band without deleting it, for a season's worth of content that is
        /// finished but not yet live. Same intent as ProfileItemDefinition.Enabled one level up.
        /// </summary>
        public bool Enabled = true;
    }
}
