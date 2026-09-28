using System;
using System.Collections.Generic;
using Shared.Store;

namespace Shared.GameConfig
{
    [Serializable]
    public sealed class StoreConfig
    {
        public List<StoreItemDefinition> Items = new List<StoreItemDefinition>();

        public StoreConfig CloneNormalized()
        {
            var clone = new StoreConfig { Items = new List<StoreItemDefinition>() };
            if (Items != null)
            {
                foreach (StoreItemDefinition item in Items)
                {
                    if (item != null)
                    {
                        clone.Items.Add(item.CloneNormalized());
                    }
                }
            }

            return clone;
        }
    }
}
