using CatFlap.Levels;
using Shared.FileStorage;
using UnityEngine;
using SharedFileStorage = Shared.FileStorage.FileStorage;

namespace CatFlap.Runtime
{
    public sealed class CatFlapLevelRepository
    {
        private readonly CatFlapLevelJsonParser parser;
        private readonly CatFlapLevelValidator validator;
        private readonly SharedFileStorage storage;

        public CatFlapLevelRepository()
            : this(new CatFlapLevelJsonParser(), new CatFlapLevelValidator(), new SharedFileStorage())
        {
        }

        public CatFlapLevelRepository(CatFlapLevelJsonParser parser, CatFlapLevelValidator validator)
            : this(parser, validator, new SharedFileStorage())
        {
        }

        public CatFlapLevelRepository(CatFlapLevelJsonParser parser, CatFlapLevelValidator validator, SharedFileStorage storage)
        {
            this.parser = parser;
            this.validator = validator;
            this.storage = storage ?? new SharedFileStorage();
        }

        public CatFlapRuntimeLevelLoadResult LoadFromJson(string json)
        {
            CatFlapLevelParseResult parse = parser.Parse(json);
            if (!parse.IsValid)
            {
                return CatFlapRuntimeLevelLoadResult.Failed(parse.Errors);
            }

            CatFlapLevelValidationResult validation = validator.Validate(parse.Level);
            if (!validation.IsValid)
            {
                return CatFlapRuntimeLevelLoadResult.Failed(validation.Errors, validation.Warnings);
            }

            return CatFlapRuntimeLevelLoadResult.Loaded(parse.Level, validation.Warnings);
        }

        public CatFlapRuntimeLevelLoadResult LoadFromTextAsset(TextAsset textAsset)
        {
            if (textAsset == null)
            {
                return CatFlapRuntimeLevelLoadResult.Failed(new[] { "Level TextAsset is missing." });
            }

            return LoadFromJson(textAsset.text);
        }

        public CatFlapRuntimeLevelLoadResult LoadFromFilePath(string path)
        {
            FileReader result = storage.ReadAllText(path);
            if (!result.IsLoaded)
            {
                return CatFlapRuntimeLevelLoadResult.Failed(result.Errors);
            }

            return LoadFromJson(result.Text);
        }
    }
}
