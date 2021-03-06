﻿using System;
using System.Diagnostics;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using ToSic.Eav.Data;
using ToSic.Eav.Data.Builder;
using ToSic.Eav.Persistence.Efc.Models;
using ToSic.Eav.Persistence.Logging;

namespace ToSic.Eav.Repository.Efc.Parts
{
    public partial class DbAttributeDefinition: BllCommandBase
    {
        public DbAttributeDefinition(DbDataController cntx) : base(cntx) {}

        /// <summary>
        /// Set an Attribute as Title on an AttributeSet
        /// </summary>
        public void SetTitleAttribute(int attributeId, int attributeSetId)
        {
            GetAttribute(attributeSetId, attributeId).IsTitle = true;

            // unset other Attributes with isTitle=true
            var oldTitleAttributes = DbContext.SqlDb.ToSicEavAttributesInSets
                .Where(s => s.AttributeSetId == attributeSetId && s.IsTitle);
            foreach (var oldTitleAttribute in oldTitleAttributes)
                oldTitleAttribute.IsTitle = false;

            DbContext.SqlDb.SaveChanges();
        }

        internal int GetOrCreateAttributeDefinition(int contentTypeId, AttributeDefinition newAtt)
        {
            int destAttribId;
            if (!AttributeExistsInSet(contentTypeId, newAtt.Name))
            {
                // try to add new Attribute
                destAttribId = AppendToEndAndSave(contentTypeId, newAtt);
            }
            else
            {
                DbContext.Log.Add(new LogItem(EventLogEntryType.Information, "Attribute already exists" + newAtt.Name));
                destAttribId = AttributeId(contentTypeId, newAtt.Name);
            }
            return destAttribId;
        }


        private ToSicEavAttributesInSets GetAttribute(int attributeSetId, int attributeId = 0, string name = null)
            => attributeId != 0
                ? DbContext.SqlDb.ToSicEavAttributesInSets
                    .Single(a => a.AttributeId == attributeId && a.AttributeSetId == attributeSetId)
                : DbContext.SqlDb.ToSicEavAttributesInSets
                    .Single(a => a.AttributeId == attributeId && a.Attribute.StaticName == name);


        public int AttributeId(int setId, string staticName) => GetAttribute(setId, name: staticName).Attribute.AttributeId;

        /// <summary>
        /// Set an Attribute as Title on an AttributeSet
        /// </summary>
        public void RenameAttribute(int attributeId, int attributeSetId, string newName)
        {
            if(string.IsNullOrWhiteSpace(newName))
                throw new Exception("can't rename to something empty");

            // ensure that it's in the set
            var attr = DbContext.SqlDb.ToSicEavAttributesInSets
                .Include(a => a.Attribute)
                .Single(a => a.AttributeId == attributeId && a.AttributeSetId == attributeSetId)
                .Attribute;
            attr.StaticName = newName;
            DbContext.SqlDb.SaveChanges();
        }

        /// <summary>
        /// Append a new Attribute to an AttributeSet
        /// </summary>
        internal int AppendToEndAndSave(int attributeSetId, AttributeDefinition attributeDefinition)
        {
            var maxIndex = DbContext.SqlDb.ToSicEavAttributesInSets
                .Where(a => a.AttributeSetId == attributeSetId)
                .ToList() // important because it otherwise has problems with the next step...
                .Max(s => (int?) s.SortOrder);

            attributeDefinition.SetSortOrder(maxIndex + 1 ?? 0);

            return AddAttributeAndSave(attributeSetId, attributeDefinition);
        }
        
        /// <summary>
        /// Append a new Attribute to an AttributeSet
        /// </summary>
        public int AddAttributeAndSave(int attributeSetId, AttributeDefinition attributeDefinition)
        {
            var staticName = attributeDefinition.Name;
            var type = attributeDefinition.Type;
            var isTitle = attributeDefinition.IsTitle;
            var sortOrder = attributeDefinition.SortOrder;

            var attributeSet = DbContext.AttribSet.GetDbAttribSet(attributeSetId);

            if (!Constants.AttributeStaticName.IsMatch(staticName))
                throw new Exception("Attribute static name \"" + staticName + "\" is invalid. " + Constants.AttributeStaticNameRegExNotes);

            // Prevent Duplicate Name
            if (AttributeExistsInSet(attributeSet.AttributeSetId, staticName))
                throw new ArgumentException("An Attribute with static name " + staticName + " already exists", nameof(staticName));

            var newAttribute = new ToSicEavAttributes
            {
                Type = type,
                StaticName = staticName,
                ChangeLogCreated = DbContext.Versioning.GetChangeLogId()
            };
            var setAssignment = new ToSicEavAttributesInSets
            {
                Attribute = newAttribute,
                AttributeSet = attributeSet,
                SortOrder = sortOrder,
                AttributeGroupId = 1, //attributeGroupId,
                IsTitle = isTitle
            };
            DbContext.SqlDb.Add(newAttribute);
            DbContext.SqlDb.Add(setAssignment);

            // Set Attribute as Title if there's no title field in this set
            if (!attributeSet.ToSicEavAttributesInSets.Any(a => a.IsTitle))
                setAssignment.IsTitle = true;

            if (isTitle)
            {
                // unset old Title Fields
                var oldTitleFields = attributeSet.ToSicEavAttributesInSets.Where(a => a.IsTitle && a.Attribute.StaticName != staticName).ToList();
                foreach (var titleField in oldTitleFields)
                    titleField.IsTitle = false;
            }

            DbContext.SqlDb.SaveChanges();
            return newAttribute.AttributeId;
        }
        


        public bool RemoveAttributeAndAllValuesAndSave(int attributeId)
        {
            // Remove values and valueDimensions of this attribute
            var values = DbContext.SqlDb.ToSicEavValues
                .Where(a => a.AttributeId == attributeId).ToList();

            values.ForEach(v => {
                v.ToSicEavValuesDimensions.ToList().ForEach(vd => {
                    DbContext.SqlDb.ToSicEavValuesDimensions.Remove(vd);
                });
                DbContext.SqlDb.ToSicEavValues.Remove(v);
            });
            DbContext.SqlDb.SaveChanges();

            var attr = DbContext.SqlDb.ToSicEavAttributes.FirstOrDefault(a => a.AttributeId == attributeId);

            if (attr != null)
                DbContext.SqlDb.ToSicEavAttributes.Remove(attr);

            DbContext.SqlDb.SaveChanges();
            return true;
        }



    }
}
