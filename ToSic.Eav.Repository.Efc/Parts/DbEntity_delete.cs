﻿using System;
using System.Collections.Generic;
using System.Linq;
using ToSic.Eav.Persistence.Efc.Models;

namespace ToSic.Eav.Repository.Efc.Parts
{
    public partial class DbEntity
    {
        /// <summary>
        /// Delete an Entity
        /// </summary>
        internal bool DeleteEntity(int repositoryId, bool autoSave = true, bool removeFromParents = false)
        {
            if (repositoryId == 0)
                return false;

            // get full entity again to be sure we are deleting everything - otherwise inbound unreliable
            // note that as this is a DB-entity, the EntityId is actually the repositoryId
            var entity = DbContext.Entities.GetDbEntity(repositoryId, "ToSicEavValues,ToSicEavValues.ToSicEavValuesDimensions");


            #region Delete Related Records (Values, Value-Dimensions, Relationships)
            // Delete all Value-Dimensions
            var valueDimensions = entity.ToSicEavValues.SelectMany(v => v.ToSicEavValuesDimensions).ToList();
            DbContext.SqlDb.RemoveRange(valueDimensions);

            // Delete all Values
            DbContext.SqlDb.RemoveRange(entity.ToSicEavValues.ToList());

            // Delete all Parent-Relationships
            //entity.RelationshipsWithThisAsParent.Clear();
            DeleteRelationships(entity.RelationshipsWithThisAsParent);
            //DbContext.SqlDb.RemoveRange(entity.RelationshipsWithThisAsParent.ToList() /*.Clear()*/);
            if (removeFromParents)
                DeleteRelationships(entity.RelationshipsWithThisAsChild);
                //DbContext.SqlDb.RemoveRange(entity.RelationshipsWithThisAsChild.ToList() /*.Clear()*/);

            #endregion

            // If entity was Published, set Deleted-Flag
            if (entity.IsPublished)
            {
                entity.ChangeLogDeleted = DbContext.Versioning.GetChangeLogId();
                // Also delete the Draft (if any)
                var draftEntityId = DbContext.Publishing.GetDraftBranchEntityId(entity.EntityId);
                if (draftEntityId.HasValue)
                    DeleteEntity(draftEntityId.Value);
            }
            // If entity was a Draft, really delete that Entity
            else
            {
                // Delete all Child-Relationships
                DeleteRelationships(entity.RelationshipsWithThisAsChild);
                //DbContext.SqlDb.RemoveRange(entity.RelationshipsWithThisAsChild.ToList() /*.Clear()*/);
                DbContext.SqlDb.Remove(entity);
            }

            if (autoSave)
                DbContext.SqlDb.SaveChanges();

            return true;
        }

        private void DeleteRelationships(ICollection<ToSicEavEntityRelationships> relationships)
        {
            relationships.ToList().ForEach(r => DbContext.SqlDb.ToSicEavEntityRelationships.Remove(r));
        }


        internal Tuple<bool, string> CanDeleteEntity(int entityId)
        {
            var messages = new List<string>();
            var entity = GetDbEntity(entityId);
            //var entityModel = new Efc11Loader(DbContext.SqlDb).Entity(DbContext.AppId, entityId);
            //if (!entityModel.IsPublished && entityModel.GetPublished() == null)	// always allow Deleting Draft-Only Entity 

            if (!entity.IsPublished && entity.PublishedEntityId == null)	// always allow Deleting Draft-Only Entity 
                return new Tuple<bool, string>(true, null);

            #region check if there are relationships where this is a child
            var parents = DbContext.SqlDb.ToSicEavEntityRelationships
                .Where(r => r.ChildEntityId == entityId)
                .Select(r => new TempEntityAndTypeInfos { EntityId = r.ParentEntityId, TypeId = r.ParentEntity.AttributeSetId })
                .ToList();
            if (parents.Any())
            {
                TryToGetMoreInfosAboutDependencies(parents, messages);
                messages.Add($"found {parents.Count()} relationships where this is a child - the parents are: {string.Join(", ", parents)}.");
            }
            #endregion

            var entitiesAssignedToThis = GetAssignedEntities(Constants.MetadataForEntity, entityId)
                .Select(e => new TempEntityAndTypeInfos() { EntityId = e.EntityId, TypeId = e.AttributeSetId })
                .ToList();
            if (entitiesAssignedToThis.Any())
            {
                TryToGetMoreInfosAboutDependencies(entitiesAssignedToThis, messages);
                messages.Add($"found {entitiesAssignedToThis.Count()} entities which are metadata for this, assigned children (like in a pieline) or assigned for other reasons: {string.Join(", ", entitiesAssignedToThis)}.");
            }
            return Tuple.Create(!messages.Any(), string.Join(" ", messages));
        }

        private void TryToGetMoreInfosAboutDependencies(IEnumerable<TempEntityAndTypeInfos> dependencies, List<string> messages)
        {
            try
            {
                // try to get more infos about the parents
                foreach (var dependency in dependencies)
                    dependency.TypeName = DbContext.AttribSet.GetDbAttribSet(dependency.TypeId).Name;
            }
            catch
            {
                messages.Add("Relationships but was not able to look up more details to show a nicer error.");
            }


        }

        private class TempEntityAndTypeInfos
        {
            internal int EntityId;
            internal int TypeId;
            internal string TypeName = "";

            public override string ToString() => EntityId + " (" + TypeName + ")";

        }
    }
}
