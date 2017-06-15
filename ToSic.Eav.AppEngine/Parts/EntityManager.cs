﻿using System;
using System.Collections.Generic;
using System.Linq;
using ToSic.Eav.ImportExport.Versioning;
using ToSic.Eav.Interfaces;

namespace ToSic.Eav.Apps.Parts
{
    /// <summary>
    /// Manager for entities in an app
    /// </summary>
    public class EntitiesManager: BaseManager
    {
        public EntitiesManager(AppManager app) : base(app)
        {
        }

        /// <summary>
        /// Publish an entity 
        /// </summary>
        /// <param name="repositoryId"></param>
        /// <param name="state"></param>
        /// <returns></returns>
        public bool Publish(int repositoryId, bool state)
        {
            _appManager.DataController.Publishing.PublishDraftInDbEntity(repositoryId);//, state);
            return state;
        }

        #region Delete

        /// <summary>
        /// delete an entity
        /// </summary>
        /// <param name="id"></param>
        /// <param name="force"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public bool Delete(int id, bool force = false)
        {
            var canDelete = _appManager.DataController.Entities.CanDeleteEntity(id);
            if (!canDelete.Item1 && !force)
                throw new Exception(canDelete.Item2);
            return _appManager.DataController.Entities.DeleteEntity(id);
        }

        public bool Delete(Guid guid) => _appManager.DataController.Entities.DeleteEntity(guid);

        public bool DeletePossible(int entityId) => _appManager.DataController.Entities.CanDeleteEntity(entityId).Item1;

        public string DeleteHinderance(int entityId) => _appManager.DataController.Entities.CanDeleteEntity(entityId).Item2;
        #endregion


        public Tuple<int, Guid> Create(string typeName, Dictionary<string, object> values, IIsMetadata isMetadata = null)
        {
            var contentType = _appManager.Cache.GetContentType(typeName);
            var ent = _appManager.DataController.Entities.AddEntity(contentType.ContentTypeId, values, isMetadata);
            return new Tuple<int, Guid>(ent.EntityId, ent.EntityGuid);
        }

        /// <summary>
        /// Update an entity
        /// </summary>
        /// <param name="id"></param>
        /// <param name="values"></param>
        /// <param name="dimensionIds"></param>
        public void Update(int id, Dictionary<string, object> values, ICollection<int> dimensionIds = null)
            => _appManager.DataController.Entities.SaveEntity(id, values, dimensionIds: dimensionIds);

        /// <summary>
        /// Get an entity, or create it with the values provided.
        /// Important for use cases, where an information must exist for sure, so it would be created with the provided defaults
        /// </summary>
        /// <param name="newGuid"></param>
        /// <param name="contentTypeName"></param>
        /// <param name="values"></param>
        /// <returns></returns>
        public int GetOrCreate(Guid? newGuid, string contentTypeName, Dictionary<string, object> values)
        {
            if (newGuid.HasValue && _appManager.DataController.Entities.EntityExists(newGuid.Value))
            {
                // check if it's deleted - if yes, resurrect
                var existingEnt = _appManager.DataController.Entities.GetEntitiesByGuid(newGuid.Value).First();
                if (existingEnt.ChangeLogDeleted != null)
                    existingEnt.ChangeLogDeleted = null;

                return existingEnt.EntityId;
            }
            var contentType = _appManager.Cache.GetContentType(contentTypeName).ContentTypeId;
            return _appManager.DataController.Entities.AddEntity(contentType, values, entityGuid: newGuid).EntityId;
        }


        public List<ItemHistory> GetHistory(int id) => _appManager.DataController.Versioning.GetHistoryList(id, true);

        public void RestorePrevious(int id, int historyId)
            => _appManager.DataController.Versioning.RestoreEntity(id, historyId); 
    }
}
