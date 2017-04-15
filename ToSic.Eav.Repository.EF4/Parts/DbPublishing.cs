﻿using System;
using System.Linq;

namespace ToSic.Eav.Repository.EF4.Parts
{
    internal class DbPublishing: BllCommandBase
    {
        public DbPublishing(DbDataController c) : base(c) { }

        /// <summary>
        /// Publish a Draft-Entity
        /// </summary>
        /// <param name="entityId"></param>
        /// <param name="autoSave">Call SaveChanges() automatically? Set to false if you want to do further DB changes</param>
        /// <returns>The published Entity</returns>
        public Entity PublishDraftInDbEntity(int entityId, bool autoSave)
        {
            var unpublishedEntity = DbContext.Entities.GetDbEntity(entityId);
            if (unpublishedEntity.IsPublished)
            {
                // try to get the draft if it exists
                var draftId = GetDraftEntityId(entityId);
                if (!draftId.HasValue)
                    throw new InvalidOperationException($"EntityId {entityId} is already published");
                unpublishedEntity = DbContext.Entities.GetDbEntity(draftId.Value);
            }
            Entity publishedEntity;

            // Publish Draft-Entity
            if (!unpublishedEntity.PublishedEntityId.HasValue)
            {
                unpublishedEntity.IsPublished = true;
                publishedEntity = unpublishedEntity;
            }
            // Replace currently published Entity with draft Entity and delete the draft
            else
            {
                publishedEntity = DbContext.Entities.GetDbEntity(unpublishedEntity.PublishedEntityId.Value);
                DbContext.Values.CloneEntityValues(unpublishedEntity, publishedEntity);

                // delete the Draft Entity
                DbContext.Entities.DeleteEntity(unpublishedEntity, false);
            }

            if (autoSave)
                DbContext.SqlDb.SaveChanges();

            return publishedEntity;
        }

        /// <summary>
        /// Should clean up branches of this item, and set the one and only as published
        /// </summary>
        /// <param name="unpublishedEntityId"></param>
        /// <param name="newPublishedState"></param>
        /// <returns></returns>
        public Entity ClearDraftBranchAndSetPublishedState(int unpublishedEntityId, bool newPublishedState = true)
        {
            var unpublishedEntity = DbContext.Entities.GetDbEntity(unpublishedEntityId);
            // 2dm 2016-06-29 this should now be allowed, so we turn off the test
            //if (unpublishedEntity.IsPublished)
            //    throw new InvalidOperationException(string.Format("EntityId {0} is already published", unpublishedEntityId));

            Entity publishedEntity;

            // Publish Draft-Entity
            if (!unpublishedEntity.PublishedEntityId.HasValue)
            {
                unpublishedEntity.IsPublished = newPublishedState;
                publishedEntity = unpublishedEntity;
            }
            // Replace currently published Entity with draft Entity and delete the draft
            else
            {
                publishedEntity = DbContext.Entities.GetDbEntity(unpublishedEntity.PublishedEntityId.Value);
                publishedEntity.IsPublished = newPublishedState;

                // delete the Draft Entity
                DbContext.Entities.DeleteEntity(unpublishedEntity, false);
            }

            return publishedEntity;
        }

        /// <summary>
        /// Get Draft EntityId of a Published EntityId. Returns null if there's none.
        /// </summary>
        /// <param name="entityId">EntityId of the Published Entity</param>
        internal int? GetDraftEntityId(int entityId)
        {
            return DbContext.SqlDb.Entities.Where(e => e.PublishedEntityId == entityId && !e.ChangeLogIDDeleted.HasValue).Select(e => (int?)e.EntityID).SingleOrDefault();
        }
    }
}