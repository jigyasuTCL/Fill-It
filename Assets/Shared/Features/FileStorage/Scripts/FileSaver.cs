using System.Collections.Generic;
using System.Linq;

namespace Shared.FileStorage
{
    public sealed class FileSaver
    {
        private FileSaver(bool isSaved, IEnumerable<string> errors)
        {
            IsSaved = isSaved;
            Errors = (errors ?? Enumerable.Empty<string>()).ToArray();
        }

        public bool IsSaved { get; }

        public IReadOnlyList<string> Errors { get; }

        public static FileSaver Saved()
        {
            return new FileSaver(true, Enumerable.Empty<string>());
        }

        public static FileSaver Failed(IEnumerable<string> errors)
        {
            return new FileSaver(false, errors);
        }
    }
}
