﻿using System.Collections.Generic;
using System.Linq;

// ReSharper disable once CheckNamespace
namespace ToSic.Eav.Persistence.Efc.Models
{
    public partial class ToSicEavAttributeSets
    {
        // Ensure Static name of new AttributeSets
        //public AttributeSet()
        //{
        //    _StaticName = Guid.NewGuid().ToString();
        //    _AppID = AppID;
        //}


        public IEnumerable<ToSicEavAttributes> GetAttributes()
            => ToSicEavAttributesInSets.Select(item => item.Attribute).ToList();

        public ToSicEavAttributes AttributeByName(string attributeName)
            => GetAttributes().FirstOrDefault(attr => attr.StaticName.ToLower() == attributeName.ToLower());

        //public ToSicEavEntities EntityByGuid(Guid entityGuid)
        //    => ToSicEavEntities.FirstOrDefault(entity => entity.EntityGuid == entityGuid && entity.ChangeLogDeleted == null);

    }

}
