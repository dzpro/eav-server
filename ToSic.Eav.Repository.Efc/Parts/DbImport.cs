﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using ToSic.Eav.ImportExport.Logging;
using ToSic.Eav.ImportExport.Models;
using ToSic.Eav.Persistence.Efc.Models;

namespace ToSic.Eav.Repository.Efc.Parts
{
    /// <summary>
    /// Import Schema and Entities to the EAV SqlStore
    /// </summary>
    public class DbImport
    {
        #region Private Fields
        private readonly DbDataController _context;
        private readonly bool _dontUpdateExistingAttributeValues;
        private readonly bool _keepAttributesMissingInImport;
        private readonly List<ImportLogItem> _importLog = new List<ImportLogItem>();
        private readonly bool _largeImport;
        #endregion

        #region Properties
        /// <summary>
        /// Get the Import Log
        /// </summary>
        public List<ImportLogItem> ImportLog => _importLog;

        bool PreventUpdateOnDraftEntities { get; }
        #endregion

        /// <summary>
        /// Initializes a new instance of the Import class.
        /// </summary>
        public DbImport(int? zoneId, int? appId, bool dontUpdateExistingAttributeValues = true, bool keepAttributesMissingInImport = true, bool preventUpdateOnDraftEntities = true, bool largeImport = true)
        {
            _context = DbDataController.Instance(zoneId, appId);
            _dontUpdateExistingAttributeValues = dontUpdateExistingAttributeValues;
            _keepAttributesMissingInImport = keepAttributesMissingInImport;
            PreventUpdateOnDraftEntities = preventUpdateOnDraftEntities;
            _largeImport = largeImport;
        }

        /// <summary>
        /// Import AttributeSets and Entities
        /// </summary>
        public void ImportIntoDb(IEnumerable<ImpContentType> newAttributeSets, IEnumerable<ImpEntity> newEntities)
        {
            _context.PurgeAppCacheOnSave = false;

            // Enhance the SQL timeout for imports
            if (_largeImport)
                _context.SqlDb.Database.SetCommandTimeout(3600);

            #region initialize DB connection / transaction
            // Make sure the connection is open - because on multiple calls it's not clear if it was already opened or not
            var con = _context.SqlDb.Database.GetDbConnection();
            if (con.State != ConnectionState.Open)
                con.Open();

            var transaction = _context.SqlDb.Database.BeginTransaction();

            #endregion
            // run import, but rollback transaction if necessary
            try 
            {
                #region import AttributeSets if any were included
                if (newAttributeSets != null)
                    _context.Versioning.QueueDuringAction(() => { // .ActivateQueue();
                        var newSetsList = newAttributeSets.ToList();
                        // first: import the attribute sets in the system scope, as they may be needed by others...
                        // ...and would need a cache-refresh before 
                        var sysAttributeSets = newSetsList.Where(a => a.Scope == Constants.ScopeSystem).ToList();
                        if (sysAttributeSets.Any())
                            ImportSomeAttributeSets(sysAttributeSets);

                        // now the remaining attributeSets
                        var nonSysAttribSets = newSetsList.Where(a => !sysAttributeSets.Contains(a)).ToList();
                        if (nonSysAttribSets.Any())
                            ImportSomeAttributeSets(nonSysAttribSets);

                        //_context.Versioning.ProcessQueue();
                    });

                #endregion

                #region import Entities
                if (newEntities != null)
                    _context.Versioning.QueueDuringAction(() => // .ActivateQueue();
                    {
                        foreach (var entity in newEntities)
                            _context.DoAndSave(() => PersistOneImportEntity(entity));

                        _context.Relationships.ImportRelationshipQueueAndSave();

                        // must do this after importing the relationship queue!
                        //_context.Versioning.ProcessQueue();
                    });
                #endregion

                // Commit DB Transaction
                transaction.Commit();
                _context.SqlDb.Database.CloseConnection();

            }
            catch (Exception)
            {
                transaction.Rollback();
                throw;
            }
        }

        private void ImportSomeAttributeSets(IEnumerable<ImpContentType> newAttributeSets)
        {
            foreach (var attributeSet in newAttributeSets)
                ImportAttributeSet(attributeSet);

            _context.Relationships.ImportRelationshipQueueAndSave();

            // in case anything imported was to be shared, ensure that
            _context.AttribSet.EnsureSharedAttributeSetsOnEverythingAndSave();
        }

        /// <summary>
        /// Import an AttributeSet with all Attributes and AttributeMetaData
        /// </summary>
        private void ImportAttributeSet(ImpContentType impContentType)
        {
            // initialize destinationSet - create or test existing if ok
            var destinationSet = GetAndCheckIfValidOrCreateDestinationSet(impContentType);
            if (destinationSet == null) // something went wrong, skip this import
                return;

            // if this set expects to share it's configuration, ensure that it does
            if (destinationSet.AlwaysShareConfiguration)
                _context.AttribSet.EnsureSharedAttributeSetsOnEverythingAndSave();

            // append all Attributes
            foreach (var importAttribute in impContentType.Attributes)
            {
                ToSicEavAttributes destinationAttribute;
                if(!_context.Attributes.AttributeExistsInSet(destinationSet.AttributeSetId, importAttribute.StaticName))
                {
                    // try to add new Attribute
                    var isTitle = importAttribute == impContentType.TitleAttribute;
                    destinationAttribute = _context.Attributes
                        .AppendToEndAndSave(destinationSet, 0, importAttribute.StaticName, importAttribute.Type, importAttribute.InputType, isTitle);//, false);
                }
				else
                {
					_importLog.Add(new ImportLogItem(EventLogEntryType.Warning, "Attribute already exists") { ImpAttribute = importAttribute });
                    destinationAttribute = destinationSet.ToSicEavAttributesInSets
                        .Single(a => a.Attribute.StaticName == importAttribute.StaticName).Attribute;
                }

                // save additional entities containing AttributeMetaData for this attribute
                if (importAttribute.AttributeMetaData != null)
                    SaveImportedAttributeMetadata(importAttribute.AttributeMetaData, destinationAttribute.AttributeId);
            }

            // optionally re-order the attributes if specified in import
            if (impContentType.SortAttributes)
                SortAttributesByImportOrder(impContentType, destinationSet);
        }

        private ToSicEavAttributeSets GetAndCheckIfValidOrCreateDestinationSet(ImpContentType impContentType)
        {
            var destinationSet = _context.AttribSet.GetAttributeSet(impContentType.StaticName);

            // add new AttributeSet, do basic configuration if possible, then save
            if (destinationSet == null)
                destinationSet = _context.AttribSet.PrepareSet(impContentType.Name, impContentType.Description,
                    impContentType.StaticName, impContentType.Scope, false, null);

            // to use existing attribute Set, do some minimal conflict-checking
            else
            {
                _importLog.Add(new ImportLogItem(EventLogEntryType.Information, "AttributeSet already exists")
                {
                    ImpContentType = impContentType
                });
                if (destinationSet.UsesConfigurationOfAttributeSet.HasValue)
                {
                    _importLog.Add(new ImportLogItem(EventLogEntryType.Error,
                        "Not allowed to import/extend an AttributeSet which uses Configuration of another AttributeSet.")
                    {
                        ImpContentType = impContentType
                    });
                    return null;
                }
            }

            // If a "Ghost"-content type is specified, try to assign that
            if (!string.IsNullOrEmpty(impContentType.UsesConfigurationOfAttributeSet))
            {
                var ghostParentId = FindCorrectGhostParentId(impContentType);
                if (ghostParentId == 0) return null;
                destinationSet.UsesConfigurationOfAttributeSet = ghostParentId;
            }

            destinationSet.AlwaysShareConfiguration = impContentType.AlwaysShareConfiguration;
            _context.SqlDb.SaveChanges();

            // all ok :)
            return destinationSet;
        }

        /// <summary>
        /// Look up the ghost-parent-id
        /// </summary>
        /// <param name="impContentType"></param>
        /// <returns>The parent id as needed, or 0 if not found - which usually indicates an import problem</returns>
        private int FindCorrectGhostParentId(ImpContentType impContentType)
        {
            // Look for a content type with the StaticName, which has no "UsesConfigurationOf..." set (is a master)
            var ghostAttributeSets = _context.SqlDb.ToSicEavAttributeSets.Where(
                    a => a.StaticName == impContentType.UsesConfigurationOfAttributeSet
                         && a.ChangeLogDeleted == null
                         && a.UsesConfigurationOfAttributeSet == null).
                OrderBy(a => a.AttributeSetId)
                .ToList();

            if (ghostAttributeSets.Count == 0)
            {
                _importLog.Add(new ImportLogItem(EventLogEntryType.Warning, "AttributeSet not imported because master set not found: " + impContentType.UsesConfigurationOfAttributeSet) {ImpContentType = impContentType});
                return 0;
            }

            // If multiple masters are found, use first and add a warning message
            if (ghostAttributeSets.Count > 1)
                _importLog.Add(new ImportLogItem(EventLogEntryType.Warning, "Multiple potential master AttributeSets found for StaticName: " + impContentType.UsesConfigurationOfAttributeSet) {ImpContentType = impContentType});
            
            // all ok, return id
            return ghostAttributeSets.First().AttributeSetId;
        }

        /// <summary>
        /// Save additional entities describing the attribute
        /// </summary>
        /// <param name="attributeMetaData"></param>
        /// <param name="destinationAttributeId"></param>
        private void SaveImportedAttributeMetadata(List<ImpEntity> attributeMetaData, int destinationAttributeId)
        {
            foreach (var entity in attributeMetaData)
            {
                // Validate Entity
                entity.KeyTypeId = Constants.MetadataForField;

                // Set KeyNumber
                if (destinationAttributeId == 0 || destinationAttributeId < 0) // < 0 is ef-core temp id
                    _context.SqlDb.SaveChanges();
                entity.KeyNumber = destinationAttributeId;

                // Get guid of previously existing assignment - if it exists
                var existingMetadata = _context.Entities
                    .GetAssignedEntities(Constants.MetadataForField, keyNumber: destinationAttributeId)
                    .FirstOrDefault(e => e.AttributeSetId == destinationAttributeId);

                if (existingMetadata != null)
                    entity.EntityGuid = existingMetadata.EntityGuid;

                PersistOneImportEntity(entity);
            }
        }

        /// <summary>
        /// Sometimes the import asks for sorting the fields again according to input
        /// this method will then take care of re-sorting them correctly
        /// Fields which were not in the import will simply land at the end
        /// </summary>
        /// <param name="impContentType"></param>
        /// <param name="destinationSet"></param>
        private void SortAttributesByImportOrder(ImpContentType impContentType, ToSicEavAttributeSets destinationSet)
        {
            var attributeList = _context.SqlDb.ToSicEavAttributesInSets
                .Where(a => a.AttributeSetId == destinationSet.AttributeSetId)
                .ToList();

            attributeList = attributeList
                .OrderBy(a => impContentType.Attributes
                    .IndexOf(impContentType.Attributes
                        .First(ia => ia.StaticName == a.Attribute.StaticName)))
                .ToList();
            _context.Attributes.PersistAttributeOrder(attributeList);
        }

        /// <summary>
        /// Import an Entity with all values
        /// </summary>
        private void PersistOneImportEntity(ImpEntity impEntity)
        {
            #region try to get AttributeSet or otherwise cancel & log error

            var dbAttrSet = _context.AttribSet.GetAttributeSet(impEntity.AttributeSetStaticName);

            if (dbAttrSet == null) // AttributeSet not Found
            {
                _importLog.Add(new ImportLogItem(EventLogEntryType.Error, "AttributeSet not found")
                {
                    ImpEntity = impEntity,
                    ImpContentType = new ImpContentType {StaticName = impEntity.AttributeSetStaticName}
                });
                return;
            }

            #endregion

            // Find existing Enties - meaning both draft and non-draft
            List<ToSicEavEntities> dbExistingEntities = null;
            if (impEntity.EntityGuid.HasValue)
                dbExistingEntities = _context.Entities.GetEntitiesByGuid(impEntity.EntityGuid.Value).ToList();

            #region Simplest case - add (nothing existing to update)
            if (dbExistingEntities == null || !dbExistingEntities.Any())
            {
                _context.Entities.AddImportEntity(dbAttrSet.AttributeSetId, impEntity, _importLog, impEntity.IsPublished, null);
                return;
            }

            #endregion

            #region Another simple case - we have published entities, but are saving unpublished - so we create a new one

            if (!impEntity.IsPublished && dbExistingEntities.Count(e => e.IsPublished == false) == 0 && !impEntity.ForceNoBranch)
            {
                var publishedId = dbExistingEntities.First().EntityId;
                _context.Entities.AddImportEntity(dbAttrSet.AttributeSetId, impEntity, _importLog, impEntity.IsPublished, publishedId);
                return;
            }

            #endregion 
             
            #region Update-Scenario - much more complex to decide what to change/update etc.

            #region Do Various Error checking like: Does it really exist, is it not draft, ensure we have the correct Content-Type

            // Get existing, published Entity
            var editableVersionOfTheEntity = dbExistingEntities.OrderBy(e => e.IsPublished ? 1 : 0).First(); // get draft first, otherwise the published
            _importLog.Add(new ImportLogItem(EventLogEntryType.Information, "Entity already exists", impEntity));
        

            #region ensure we don't save a draft is this is not allowed (usually in the case of xml-import)

            // Prevent updating Draft-Entity - since the initial would be draft if it has one, this would throw
            if (PreventUpdateOnDraftEntities && !editableVersionOfTheEntity.IsPublished)
            {
                _importLog.Add(new ImportLogItem(EventLogEntryType.Error, "Importing a Draft-Entity is not allowed", impEntity));
                return;
            }

            #endregion

            #region Ensure entity has same AttributeSet (do this after checking for the draft etc.
            var editableEntityContentType = _context.AttribSet.GetAttributeSet(editableVersionOfTheEntity.AttributeSetId);
            if (editableEntityContentType.StaticName != impEntity.AttributeSetStaticName)
            {
                _importLog.Add(new ImportLogItem(EventLogEntryType.Error, "Existing entity (which should be updated) has different ContentType", impEntity));
                return;
            }
            #endregion



            #endregion

            // todo: TestImport - ensure that it correctly skips the existing values
            var newValues = impEntity.Values;
            if (_dontUpdateExistingAttributeValues) // Skip values that are already present in existing Entity
                newValues = newValues.Where(v => editableVersionOfTheEntity.ToSicEavValues.All(ev => ev.Attribute.StaticName != v.Key))
                    .ToDictionary(v => v.Key, v => v.Value);

            // todo: TestImport - ensure that the EntityId of this is what previously was the RepositoryID
            _context.Entities.SaveEntity(editableVersionOfTheEntity.EntityId/*RepositoryId*/, newValues, /*masterRecord:true,*/ updateLog: _importLog,
                preserveUndefinedValues: _keepAttributesMissingInImport, isPublished: impEntity.IsPublished, forceNoBranch: impEntity.ForceNoBranch);

            #endregion
        }
    }
}