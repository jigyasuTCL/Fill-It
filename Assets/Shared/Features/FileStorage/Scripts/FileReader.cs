using System.Collections.Generic;
using System.Linq;

namespace Shared.FileStorage
{
    public sealed class FileReader
    {
        private FileReader(bool isLoaded, string text, IEnumerable<string> errors)
        {
            IsLoaded = isLoaded;
            Text = text ?? string.Empty;
            Errors = (errors ?? Enumerable.Empty<string>()).ToArray();
        }

        public bool IsLoaded { get; }

        public string Text { get; }

        public IReadOnlyList<string> Errors { get; }

        public static FileReader Loaded(string text)
        {
            return new FileReader(true, text, Enumerable.Empty<string>());
        }

        public static FileReader Failed(IEnumerable<string> errors)
        {
            return new FileReader(false, string.Empty, errors);
        }
    }
}
