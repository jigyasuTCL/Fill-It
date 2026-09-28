using System;
using System.IO;
using UnityEngine;

namespace Shared.FileStorage
{
    public sealed class FileStorage
    {
        public FileReader ReadAllText(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return FileReader.Failed(new[] { "File path is empty." });
            }

            try
            {
                if (!File.Exists(path))
                {
                    return FileReader.Failed(new[] { $"File does not exist: {path}" });
                }

                return FileReader.Loaded(File.ReadAllText(path));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                return FileReader.Failed(new[] { $"File could not be read: {exception.Message}" });
            }
        }

        public FileSaver WriteAllText(string path, string text)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return FileSaver.Failed(new[] { "File path is empty." });
            }

            try
            {
                string directoryPath = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }

                File.WriteAllText(path, text ?? string.Empty);
                return FileSaver.Saved();
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                return FileSaver.Failed(new[] { $"File could not be saved: {exception.Message}" });
            }
        }

        public bool Exists(string path)
        {
            return !string.IsNullOrWhiteSpace(path) && File.Exists(path);
        }

        public string CreatePersistentDataPath(string fileName, string fallbackFileName)
        {
            string safeFileName = string.IsNullOrWhiteSpace(fileName) ? fallbackFileName : fileName;
            return Path.Combine(Application.persistentDataPath, safeFileName);
        }
    }
}
