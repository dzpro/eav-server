﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using System.Xml.XPath;
using ToSic.Eav.ImportExport;
using ToSic.Eav.ImportExport.Models;
using ToSic.Eav.ImportExport.Versioning;
using ToSic.Eav.ImportExport.Xml;

namespace ToSic.Eav.Repository.Efc.Parts
{
    public partial class DbVersioning
    {

        #region Currently still unused versioning stuff!!!

        /// <summary>
        /// Get an Entity in the specified Version from DataTimeline using XmlImport
        /// </summary>
        /// <param name="entityId">EntityId</param>
        /// <param name="changeId">ChangeId to retrieve</param>
        /// <param name="defaultCultureDimension">Default Language</param>
        private ImpEntity PrepareRestoreEntity(int entityId, int changeId, int defaultCultureDimension)
        {
            // Get Timeline Item
            string timelineItem;
            try
            {
                timelineItem = GetItem(entityId, changeId).Data;
            }
            catch (InvalidOperationException ex)
            {
                throw new InvalidOperationException(
                    $"Error getting EntityId {entityId} with ChangeId {changeId} from DataTimeline. {ex.Message}");
            }
            if (timelineItem == null)
                throw new InvalidOperationException(
                    $"EntityId {entityId} with ChangeId {changeId} not found in DataTimeline.");

            // Parse XML
            var xEntity = XElement.Parse(timelineItem);
            var assignmentObjectTypeName = xEntity.Attribute(XmlConstants.KeyTargetType).Value;
            var assignmentObjectTypeId = new DbShortcuts(DbContext).GetAssignmentObjectType(assignmentObjectTypeName).AssignmentObjectTypeId;

            // Prepare source and target-Languages
            //if (!defaultCultureDimension.HasValue)
            //    throw new NotSupportedException("GetEntityVersion without defaultCultureDimension is not yet supported.");

            var defaultLanguage = DbContext.Dimensions.GetDimension(defaultCultureDimension).ExternalKey;
            var targetDimensions = DbContext.Dimensions.GetLanguages();
            var allSourceDimensionIds = ((IEnumerable<object>)xEntity.XPathEvaluate("/Value/Dimension/@DimensionId")).Select(d => int.Parse(((XAttribute)d).Value)).ToArray();
            var allSourceDimensionIdsDistinct = allSourceDimensionIds.Distinct().ToArray();
            var sourceDimensions = DbContext.Dimensions.GetDimensions(allSourceDimensionIdsDistinct).ToList();
            int sourceDefaultDimensionId;
            if (allSourceDimensionIdsDistinct.Contains(defaultCultureDimension))	// if default culture exists in the Entity, sourceDefaultDimensionId is still the same
                sourceDefaultDimensionId = defaultCultureDimension;
            else
            {
                var sourceDimensionsIdsGrouped = (from n in allSourceDimensionIds group n by n into g select new { DimensionId = g.Key, Qty = g.Count() }).ToArray();
                sourceDefaultDimensionId = sourceDimensionsIdsGrouped.Any() ? sourceDimensionsIdsGrouped.OrderByDescending(g => g.Qty).First().DimensionId : defaultCultureDimension;
            }
            var targetDimsRetyped = targetDimensions.Select(d => new Data.Dimension { DimensionId = d.DimensionId, Key = d.ExternalKey}).ToList();
            var sourceDimsRetyped = sourceDimensions.Select(s => new Data.Dimension {DimensionId = s.DimensionId, Key = s.ExternalKey}).ToList();
            // Load Entity from Xml unsing XmlImport
            return XmlToImportEntity.BuildImpEntityFromXml(xEntity, assignmentObjectTypeId, targetDimsRetyped, sourceDimsRetyped, sourceDefaultDimensionId, defaultLanguage);
        }


        /// <summary>
        /// Retrieve the history-list of a specific item
        /// </summary>
        /// <param name="entityId">the Id as used in the DB</param>
        /// <param name="historyId">the id of the item-history</param>
        /// <returns></returns>
        /// <remarks>
        /// Must use entity-id, because even though the Guid feels safer, but it's not unique in this DB, as the same GUID can exist in various apps...
        /// </remarks>
        public ItemHistory GetItem(int entityId, int historyId)
            => GetItemHistory(entityId, historyId, true).First();


        /// <summary>
        /// Retrieve the history-list of a specific item
        /// </summary>
        /// <param name="entityId">the Id as used in the DB</param>
        /// <param name="includeData">true if the history-data should be included, false it we'll only retrieve the list of records</param>
        /// <returns></returns>
        /// <remarks>
        /// Must use entity-id, because even though the Guid feels safer, but it's not unique in this DB, as the same GUID can exist in various apps...
        /// </remarks>
        public List<ItemHistory> GetHistoryList(int entityId, bool includeData)
            => GetItemHistory(entityId, 0, includeData);

        /// <summary>
        /// Retrieve the history-list of a specific item
        /// </summary>
        /// <param name="entityId">the Id as used in the DB</param>
        /// <param name="historyId">the optional history-id record - use 0 to get all</param>
        /// <param name="includeData">true if the history-data should be included, false it we'll only retrieve the list of records</param>
        /// <returns></returns>
        /// <remarks>
        /// Must use entity-id, because even though the Guid feels safer, but it's not unique in this DB, as the same GUID can exist in various apps...
        /// </remarks>
        private List<ItemHistory> GetItemHistory(int entityId, int historyId, bool includeData)
        {
            // get Versions from DataTimeline
            var rootQuery = DbContext.SqlDb.ToSicEavDataTimeline
                .Where(t =>
                    t.SourceTable == EntitiesTableName
                    && t.Operation == "s" // only full entity-set-operations 
                    && t.SourceId == entityId
                );
            if (historyId > 0)
                rootQuery = rootQuery.Where(t => t.SysLogId == historyId);

            var entityVersions = rootQuery
                .OrderByDescending(t => t.SysCreatedDate)
                .Join(DbContext.SqlDb.ToSicEavChangeLog, t => t.SysLogId, c => c.ChangeId, (history, log) => new { History = history, Log = log })
                .Select(d =>  new ItemHistory
                {
                    TimeStamp = d.History.SysCreatedDate,
                    ChangeSetId = d.History.SysLogId.Value,
                    HistoryId = d.History.Id,
                    User = d.Log.User,
                    Data = includeData ? d.History.NewData : null
                })
                .ToList();

            var versionNumber = entityVersions.Count;	// add version number decrement to prevent additional sorting
            foreach (var entityHistoryItem in entityVersions)
                entityHistoryItem.VersionNumber = versionNumber--;

            return entityVersions;
        }


        ///// <summary>
        ///// Get the Values of an Entity in the specified Version
        ///// </summary>
        //public DataTable Unused_GetEntityVersionValues(int entityId, int changeId, int? defaultCultureDimension, string multiValuesSeparator = null)
        //{
        //    var entityVersion = PrepareRestoreEntity(entityId, changeId, defaultCultureDimension);

        //    var result = new DataTable();
        //    result.Columns.Add("Field");
        //    result.Columns.Add(XmlConstants.EntityLanguage);
        //    result.Columns.Add(XmlConstants.ValueNode);
        //    result.Columns.Add("SharedWith");

        //    foreach (var attribute in entityVersion.Values)
        //    {
        //        foreach (var valueModel in attribute.Value)
        //        {
        //            var firstLanguage = valueModel.ValueDimensions.First().DimensionExternalKey;
        //            result.Rows.Add(attribute.Key, firstLanguage, DbContext.Values.GetTypedValue(valueModel, multiValuesSeparator: multiValuesSeparator));	// Add Main-Language

        //            foreach (var valueDimension in valueModel.ValueDimensions.Skip(1))	// Add additional Languages
        //            {
        //                result.Rows.Add(attribute.Key, valueDimension.DimensionExternalKey, DbContext.Values.GetTypedValue(valueModel, multiValuesSeparator: multiValuesSeparator), firstLanguage + (valueDimension.ReadOnly ? " (read)" : " (write)"));
        //            }
        //        }
        //    }

        //    return result;
        //}

        /// <summary>
        /// Restore an Entity to the specified Version by creating a new Version using the Import
        /// </summary>
        public void RestoreEntity(int entityId, int changeId, int defaultCultureDimension)
        {
            // Get Entity in specified Version/ChangeId
            var newVersion = PrepareRestoreEntity(entityId, changeId, defaultCultureDimension);

            // ensure we have an AppId, as this item could exist multiple times
            if (DbContext.AppId == 0)
                throw new Exception("can't work without a valid app-id, will cancel");

            // Restore Entity
            var import = new DbImport(DbContext.ZoneId, DbContext.AppId, false, false);
            import.ImportIntoDb(null, new List<ImpEntity> { newVersion });
            
            // IMPORTANT : IF THIS IS EVER USED, REMEMBER TO CLEAR THE CACHE afterwards in the calling method

            // Delete Draft (if any)
            var entityDraft = DbContext.Publishing.GetDraftEntityId(entityId);
            if (entityDraft.HasValue)
                DbContext.Entities.DeleteEntity(entityDraft.Value);//.RepositoryId);

        }

        #endregion
    }
}
