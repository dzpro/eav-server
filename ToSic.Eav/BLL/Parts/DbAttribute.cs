﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace ToSic.Eav.BLL.Parts
{
    public class DbAttribute: BllCommandBase
    {
        public DbAttribute(DbDataController cntx) : base(cntx) {}

        /// <summary>
        /// Get Attributes of an AttributeSet
        /// </summary>
        public IQueryable<Attribute> GetAttributes(int attributeSetId)
        {
            attributeSetId = DbContext.AttribSet.ResolveAttributeSetId(attributeSetId);

            return from ais in DbContext.SqlDb.AttributesInSets
                   where ais.AttributeSetID == attributeSetId
                   orderby ais.SortOrder
                   select ais.Attribute;
        }


        ///// <summary>
        ///// Get a List of all Attributes in specified AttributeSet
        ///// </summary>
        ///// <param name="attributeSet">Reference to an AttributeSet</param>
        ///// <param name="includeTitleAttribute">Specify whether TitleAttribute should be included</param>
        //public List<Attribute> GetAttributes(AttributeSet attributeSet, bool includeTitleAttribute = true)
        //{
        //    var items = Context.SqlDb.AttributesInSets.Where(a => a.AttributeSetID == attributeSet.AttributeSetID);
        //    if (!includeTitleAttribute)
        //        items = items.Where(a => !a.IsTitle);

        //    return items.Select(a => a.Attribute).ToList();
        //}
        //
        ///// <summary>
        ///// Get Title Attribute for specified AttributeSetId
        ///// </summary>
        //public Attribute GetTitleAttribute(int attributeSetId)
        //{
        //    return Context.SqlDb.AttributesInSets.Single(a => a.AttributeSetID == attributeSetId && a.IsTitle).Attribute;
        //}


        /// <summary>
        /// Get a list of all Attributes in Set for specified AttributeSetId
        /// </summary>
        public List<AttributeInSet> GetAttributesInSet(int attributeSetId)
        {
            return DbContext.SqlDb.AttributesInSets.Where(a => a.AttributeSetID == attributeSetId).OrderBy(a => a.SortOrder).ToList();
        }

        /// <summary>
        /// Update the order of the attributes in the set.
        /// </summary>
        /// <param name="setId"></param>
        /// <param name="newSortOrder">Array of attribute ids which defines the new sort order</param>
        public void UpdateAttributeOrder(int setId, List<int> newSortOrder)
        {
            var attributeList = DbContext.SqlDb.AttributesInSets.Where(a => a.AttributeSetID == setId).ToList();
            attributeList = attributeList.OrderBy(a => newSortOrder.IndexOf(a.AttributeID)).ToList();

            PersistAttributeSorting(attributeList);
        }

        public void PersistAttributeSorting(List<AttributeInSet> attributeList)
        {
            var index = 0;
            attributeList.ForEach(a => a.SortOrder = index++);
            DbContext.SqlDb.SaveChanges();
        }

        /// <summary>
        /// Set an Attribute as Title on an AttributeSet
        /// </summary>
        public void SetTitleAttribute(int attributeId, int attributeSetId)
        {
            DbContext.SqlDb.AttributesInSets.Single(a => a.AttributeID == attributeId && a.AttributeSetID == attributeSetId).IsTitle = true;

            // unset other Attributes with isTitle=true
            var oldTitleAttributes = DbContext.SqlDb.AttributesInSets.Where(s => s.AttributeSetID == attributeSetId && s.IsTitle);
            foreach (var oldTitleAttribute in oldTitleAttributes)
                oldTitleAttribute.IsTitle = false;

            DbContext.SqlDb.SaveChanges();
        }

        /// <summary>
        /// Set an Attribute as Title on an AttributeSet
        /// </summary>
        public void RenameStaticName(int attributeId, int attributeSetId, string newName)
        {
            if(string.IsNullOrWhiteSpace(newName))
                throw new Exception("can't rename to something empty");

            // ensure that it's in the set
            var attr = DbContext.SqlDb.AttributesInSets.Single(a => a.AttributeID == attributeId && a.AttributeSetID == attributeSetId).Attribute;
            attr.StaticName = newName;
            DbContext.SqlDb.SaveChanges();
        }

        ///// <summary>
        ///// Update an Attribute
        ///// </summary>
        //public Attribute UpdateAttribute(int attributeId, string staticName)
        //{
        //    return UpdateAttribute(attributeId, staticName, null);
        //}
        ///// <summary>
        ///// Update an Attribute
        ///// </summary>
        //public Attribute UpdateAttribute(int attributeId, string staticName, int? attributeSetId = null, bool isTitle = false)
        //{
        //    var attribute = Context.SqlDb.Attributes.Single(a => a.AttributeID == attributeId);
        //    Context.SqlDb.SaveChanges();

        //    if (isTitle)
        //        SetTitleAttribute(attributeId, attributeSetId.Value);

        //    return attribute;
        //}


        /// <summary>
        /// Append a new Attribute to an AttributeSet
        /// </summary>
        public Attribute AppendAttribute(AttributeSet attributeSet, string staticName, string type, string inputType, bool isTitle = false, bool autoSave = true)
        {
            return AppendAttribute(attributeSet, 0, staticName, type, inputType, isTitle, autoSave);
        }
        ///// <summary>
        ///// Append a new Attribute to an AttributeSet
        ///// </summary>
        //public Attribute AppendAttribute(int attributeSetId, string staticName, string type, string inputType, bool isTitle = false)
        //{
        //    return AppendAttribute(null, attributeSetId, staticName, type, inputType, isTitle, true);
        //}
        /// <summary>
        /// Append a new Attribute to an AttributeSet
        /// </summary>
        private Attribute AppendAttribute(AttributeSet attributeSet, int attributeSetId, string staticName, string type, string inputType, bool isTitle, bool autoSave)
        {
            var sortOrder = attributeSet != null ? attributeSet.AttributesInSets.Max(s => (int?)s.SortOrder) : DbContext.SqlDb.AttributesInSets.Where(a => a.AttributeSetID == attributeSetId).Max(s => (int?)s.SortOrder);
            if (!sortOrder.HasValue)
                sortOrder = 0;
            else
                sortOrder++;

            return AddAttribute(attributeSet, attributeSetId, staticName, type, inputType, sortOrder.Value, 1, isTitle, autoSave);
        }

        /// <summary>
        /// Append a new Attribute to an AttributeSet
        /// </summary>
        public Attribute AddAttribute(int attributeSetId, string staticName, string type, string inputType, int sortOrder = 0, int attributeGroupId = 1, bool isTitle = false, bool autoSave = true)
        {
            return AddAttribute(null, attributeSetId, staticName, type, inputType, sortOrder, attributeGroupId, isTitle, autoSave);
        }


        internal bool Exists(int attributeSetId, string staticName)
        {
            return DbContext.SqlDb.AttributesInSets.Any(
                s =>
                    s.Attribute.StaticName == staticName && !s.Attribute.ChangeLogIDDeleted.HasValue &&
                    s.AttributeSetID == attributeSetId && s.Set.AppID == DbContext.AppId);
        }

        /// <summary>
        /// Append a new Attribute to an AttributeSet
        /// </summary>
        private Attribute AddAttribute(AttributeSet attributeSet, int attributeSetId, string staticName, string type, string inputType, int sortOrder, int attributeGroupId, bool isTitle, bool autoSave)
        {
            if (attributeSet == null)
                attributeSet = DbContext.SqlDb.AttributeSets.Single(a => a.AttributeSetID == attributeSetId);
            else if (attributeSetId != 0)
                throw new Exception("Can only set attributeSet or attributeSetId");

//            if (!System.Text.RegularExpressions.Regex.IsMatch(staticName, Constants.AttributeStaticNameRegEx, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            if (!Constants.AttributeStaticName.IsMatch(staticName))
                throw new Exception("Attribute static name \"" + staticName + "\" is invalid. " + Constants.AttributeStaticNameRegExNotes);

            // Prevent Duplicate Name
            if (Exists(attributeSet.AttributeSetID, staticName))// Context.SqlDb.AttributesInSets.Any(s => s.Attribute.StaticName == staticName && !s.Attribute.ChangeLogIDDeleted.HasValue && s.AttributeSetID == attributeSet.AttributeSetID && s.Set.AppID == Context.AppId ))
                throw new ArgumentException("An Attribute with static name " + staticName + " already exists", nameof(staticName));

            var newAttribute = new Attribute
            {
                Type = type,
                StaticName = staticName,
                ChangeLogIDCreated = DbContext.Versioning.GetChangeLogId()
            };
            var setAssignment = new AttributeInSet
            {
                Attribute = newAttribute,
                Set = attributeSet,
                SortOrder = sortOrder,
                AttributeGroupID = attributeGroupId,
                IsTitle = isTitle
            };
            DbContext.SqlDb.AddToAttributes(newAttribute);
            DbContext.SqlDb.AddToAttributesInSets(setAssignment);

            // Set Attribute as Title if there's no title field in this set
            if (!attributeSet.AttributesInSets.Any(a => a.IsTitle))
                setAssignment.IsTitle = true;

            if (isTitle)
            {
                // unset old Title Fields
                var oldTitleFields = attributeSet.AttributesInSets.Where(a => a.IsTitle && a.Attribute.StaticName != staticName).ToList();
                foreach (var titleField in oldTitleFields)
                    titleField.IsTitle = false;
            }

            // If attribute has not been saved, we must save now to get the id (and assign entities)
            if (autoSave || newAttribute.AttributeID == 0)
                DbContext.SqlDb.SaveChanges();

            #region set the input type
            // new: set the inputType - this is a bit tricky because it needs an attached entity of type "@All" to set the value to...
            var newValues = new Dictionary<string, object>
            {
                {"VisibleInEditUI", true },
                {"Name", staticName},
                {"InputType", inputType}
            };

            UpdateAttributeAdditionalProperties(newAttribute.AttributeID, true, newValues);
            #endregion

            return newAttribute;
        }

        public bool UpdateInputType(int attributeId, string inputType)
        {
            var newValues = new Dictionary<string, object> {
                { "InputType", inputType }
            };

            UpdateAttributeAdditionalProperties(attributeId, true, newValues);
            return true;
        }

        /// <summary>
        /// Update AdditionalProperties of an attribute 
        /// </summary>
        public Entity UpdateAttributeAdditionalProperties(int attributeId, bool isAllProperty, IDictionary fieldProperties)
        {
            var fieldPropertyEntity = DbContext.SqlDb.Entities.FirstOrDefault(e => e.AssignmentObjectTypeID == Constants.AssignmentObjectTypeIdFieldProperties && e.KeyNumber == attributeId);
            if (fieldPropertyEntity != null)
                return DbContext.Entities.UpdateEntity(fieldPropertyEntity.EntityID, fieldProperties);

            var metaDataSetName = isAllProperty ? "@All" : "@" + DbContext.SqlDb.Attributes.Single(a => a.AttributeID == attributeId).Type;
            var systemScope = AttributeScope.System.ToString();
            var attSetFirst = DbContext.SqlDb.AttributeSets.FirstOrDefault(s => s.StaticName == metaDataSetName && s.Scope == systemScope && s.AppID == DbContext.AppId && !s.ChangeLogIDDeleted.HasValue /* _appId*/);
            if(attSetFirst == null)
                throw new Exception("Can't continue, couldn't find attrib-set with: " + systemScope + ":" + metaDataSetName + " in app " + DbContext.AppId);
            var attributeSetId = attSetFirst.AttributeSetID;

            return DbContext.Entities.AddEntity(attributeSetId, fieldProperties, null, attributeId, Constants.AssignmentObjectTypeIdFieldProperties);
        }


        // todo: add security check if it really is in this app and content-type
        public bool RemoveAttribute(int attributeId)
        {
            // Remove values and valueDimensions of this attribute
            var values = DbContext.SqlDb.Values.Where(a => a.AttributeID == attributeId).ToList();
            values.ForEach(v => {
                v.ValuesDimensions.ToList().ForEach(vd => {
                    DbContext.SqlDb.ValuesDimensions.DeleteObject(vd);
                });
                DbContext.SqlDb.Values.DeleteObject(v);
            });
            DbContext.SqlDb.SaveChanges();

            var attr = DbContext.SqlDb.Attributes.FirstOrDefault(a => a.AttributeID == attributeId);

            if (attr != null)
                DbContext.SqlDb.Attributes.DeleteObject(attr);

            DbContext.SqlDb.SaveChanges();
            return true;
        }
    }
}
