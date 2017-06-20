﻿using System;
using System.Linq;
using Microsoft.EntityFrameworkCore.Storage;
using ToSic.Eav.Interfaces;
using ToSic.Eav.Persistence.Efc.Models;

namespace ToSic.Eav.Repository.Efc.Parts
{
    public partial class DbEntity
    {
        public bool DebugKeepTransactionOpen = true;
        public IDbContextTransaction DebugTransaction; 

        public int SaveEntity(IEntity eToSave, SaveOptions so)
        {

            #region Step 1: Do some initial error checking and preparations
            if (eToSave == null)
                throw new ArgumentNullException(nameof(eToSave));

            if (eToSave.Type == null)
                throw new Exception("trying to save entity without known content-type, cannot continue");

            var usedLanguages = eToSave.Attributes.Values
                .SelectMany(v => v.Values)
                .SelectMany(vl => vl.Languages)
                .GroupBy(l => l.Key)
                .Select(g => g.First())
                .ToList();

            if(usedLanguages.Count > 0)
                if (!usedLanguages.All(l => so.Languages.Any(sol => sol.Key == l.Key)))
                    throw new Exception("found languages in save which are not available in environment");
                

            #endregion Step 1


            #region Step 2: check header record - does it already exist, what ID should we use, etc.

            var isNew = eToSave.RepositoryId <= 0;


            #endregion Step 2

            var transaction = DbContext.SqlDb.Database.BeginTransaction();
            var changeId = DbContext.Versioning.GetChangeLogId();
            ToSicEavEntities dbEntity = null;
            try
            {

                #region Step 3: either create a new entity, or if it's an update, do draft/published checks to ensure correct data

                if (isNew)
                {
                    #region Step 3a: Create new

                    dbEntity = new ToSicEavEntities
                    {
                        AssignmentObjectTypeId = eToSave.Metadata?.TargetType ?? Constants.NotMetadata,
                        KeyNumber = eToSave.Metadata?.KeyNumber,
                        KeyGuid = eToSave.Metadata?.KeyGuid,
                        KeyString = eToSave.Metadata?.KeyString,
                        ChangeLogCreated = changeId,
                        ChangeLogModified = changeId,
                        EntityGuid = eToSave.EntityGuid != Guid.Empty ? eToSave.EntityGuid : Guid.NewGuid(),
                        IsPublished = eToSave.IsPublished,
                        PublishedEntityId = eToSave.IsPublished ? null : eToSave.GetPublished()?.EntityId,
                        Owner = DbContext.UserName,
                        AttributeSetId = eToSave.Type.ContentTypeId
                    };

                    DbContext.SqlDb.Add(dbEntity);
                    DbContext.SqlDb.SaveChanges();
                    #endregion
                }
                else
                {
                    #region Step 3b: Check published (only if not new) - make sure we don't have multiple drafts

                    dbEntity = DbContext.Entities.GetDbEntity(eToSave.RepositoryId);
                    var existingDraftId = DbContext.Publishing.GetDraftEntityId(eToSave.EntityId);

                    #region Unpublished Save (Draft-Saves) - do some possible error checking

                    // Current Entity is published but Update as a draft
                    if (dbEntity.IsPublished && !eToSave.IsPublished && !so.ForceNoBranche)
                        // Prevent duplicate Draft
                        throw existingDraftId.HasValue
                            ? new InvalidOperationException(
                                $"Published EntityId {eToSave.RepositoryId} has already a draft with EntityId {existingDraftId}")
                            : new InvalidOperationException(
                                "It seems you're trying to update a published entity with a draft - this is not possible - the save should actually try to create a new draft instead without calling update.");

                    // Prevent editing of Published if there's a draft
                    if (dbEntity.IsPublished && existingDraftId.HasValue)
                        throw new Exception(
                            $"Update Entity not allowed because a draft exists with EntityId {existingDraftId}");

                    #endregion

                    #region If draft but should be published, correct what's necessary

                    // Update as Published but Current Entity is a Draft-Entity
                    // case 1: saved entity is a draft and save wants to publish
                    // case 2: new data is set to not publish, but we don't want a branch
                    if (!dbEntity.IsPublished && eToSave.IsPublished || !eToSave.IsPublished && so.ForceNoBranche)
                    {
                        if (dbEntity.PublishedEntityId.HasValue)
                            // if Entity has a published Version, add an additional DateTimeline Item for the Update of this Draft-Entity
                            DbContext.Versioning.SaveEntity(dbEntity.EntityId, dbEntity.EntityGuid, false);
                        dbEntity = DbContext.Publishing.ClearDraftBranchAndSetPublishedState(eToSave.RepositoryId,
                            eToSave.IsPublished); // must save intermediate because otherwise we get duplicate IDs
                    }

                    #endregion

                    #endregion Step 3b
                }

                #endregion Step 3

                #region Step 4: Save all normal values
                // first, clean up all existing attributes / values (flush)
                dbEntity.ToSicEavValues.Clear();
                DbContext.SqlDb.SaveChanges();  // this is necessary after remove, because otherwise EF state tracking gets messed up
                foreach (var attribute in eToSave.Attributes.Values.Where(a => a.Type != AttributeTypeEnum.Entity.ToString())) // todo: put in constant
                {
                    var attribId = eToSave.Type.Attributes.Single(a => string.Equals(a.Name, attribute.Name, StringComparison.InvariantCultureIgnoreCase)).AttributeId;
                    foreach (var value in attribute.Values)
                    {
                        dbEntity.ToSicEavValues.Add(new ToSicEavValues
                        {
                            AttributeId = attribId,
                            Value = value.SerializableObject.ToString(),
                            ChangeLogCreated = changeId, // todo: remove some time later
                            ToSicEavValuesDimensions = value.Languages?.Select(l => new ToSicEavValuesDimensions
                            {
                                DimensionId = so.Languages.Single(ol => ol.Key == l.Key).DimensionId,
                                ReadOnly = l.ReadOnly
                            }).ToList()
                        });
                    }
                }
                DbContext.SqlDb.SaveChanges(); // save all the values we just added

                #endregion

                #region Step 5: Save / update all relationships

                DbContext.Relationships.SaveRelationships(dbEntity, eToSave, so);

                #endregion

                #region Ensure versioning
                DbContext.Versioning.SaveEntity(dbEntity.EntityId, dbEntity.EntityGuid, useDelayedSerialize: true);
                #endregion

                //throw new Exception("test exception, don't want to persist till I'm sure it's pretty stable");
                // finish transaction - finalize
                if (DebugKeepTransactionOpen)
                    DebugTransaction = transaction;
                else
                    transaction.Commit();
            }
            catch 
            {
                // if anything fails, undo everything
                transaction.Rollback();
            }

            return dbEntity.EntityId;
        }

    }
}
