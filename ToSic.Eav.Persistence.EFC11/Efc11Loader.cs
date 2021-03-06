﻿using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using ToSic.Eav.App;
using ToSic.Eav.Data;
using ToSic.Eav.Data.Builder;
using ToSic.Eav.Interfaces;
using ToSic.Eav.Persistence.Efc.Models;

namespace ToSic.Eav.Persistence.Efc
{
    /// <summary>
    /// 
    /// </summary>
    public class Efc11Loader: IRepositoryLoader
    {
        #region constructor and private vars
        public Efc11Loader(EavDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        private readonly EavDbContext _dbContext;

        #endregion

        #region Testing / Analytics helpers
        internal void ResetCacheForTesting()
        => _contentTypes = new Dictionary<int, Dictionary<int, IContentType>>();
        #endregion

        #region Load Content-Types into IContent-Type Dictionary
        private Dictionary<int, Dictionary<int, IContentType>> _contentTypes = new Dictionary<int, Dictionary<int, IContentType>>();

        public IDictionary<int, IContentType> ContentTypes(int appId) => ContentTypes(appId, null);

        /// <summary>
        /// Get all ContentTypes for specified AppId. 
        /// If uses temporary caching, so if called multiple times it loads from a private field.
        /// </summary>
        public IDictionary<int, IContentType> ContentTypes(int appId, IDeferredEntitiesList source)
        {
            if (!_contentTypes.ContainsKey(appId))
                LoadContentTypesIntoLocalCache(appId, source);
            return _contentTypes[appId];
        }

        /// <summary>
        /// Load DB content-types into loader-cache
        /// </summary>
        private void LoadContentTypesIntoLocalCache(int appId, IDeferredEntitiesList source)
        {
            // Load from DB
            var contentTypes = _dbContext.ToSicEavAttributeSets
                    .Where(set => set.AppId == appId && set.ChangeLogDeleted == null)
                    .Include(set => set.ToSicEavAttributesInSets)
                        .ThenInclude(attrs => attrs.Attribute)
                    .Include(set => set.App)
                    .Include(set => set.UsesConfigurationOfAttributeSetNavigation)
                        .ThenInclude(master => master.App)
                    .ToList()
                    .Select(set => new
                    {
                        set.AttributeSetId,
                        set.Name,
                        set.StaticName,
                        set.Scope,
                        set.Description,
                        Attributes = set.ToSicEavAttributesInSets
                            .Select(a => new AttributeDefinition(appId, a.Attribute.StaticName, a.Attribute.Type, a.IsTitle, a.AttributeId, a.SortOrder, source)),
                        IsGhost = set.UsesConfigurationOfAttributeSet,
                        SharedDefinitionId = set.UsesConfigurationOfAttributeSet,
                        AppId = set.UsesConfigurationOfAttributeSetNavigation?.AppId ?? set.AppId,
                        ZoneId = set.UsesConfigurationOfAttributeSetNavigation?.App?.ZoneId ?? set.App.ZoneId,
                        ConfigIsOmnipresent =
                        set.UsesConfigurationOfAttributeSetNavigation?.AlwaysShareConfiguration ?? set.AlwaysShareConfiguration,
                    })
                .ToList();

            var shareids = contentTypes.Select(c => c.SharedDefinitionId).ToList();
            var sharedAttribs = _dbContext.ToSicEavAttributeSets
                .Include(s => s.ToSicEavAttributesInSets)
                .ThenInclude(a => a.Attribute)
                .Where(s => shareids.Contains(s.AttributeSetId))
                .ToDictionary(s => s.AttributeSetId, s => s.ToSicEavAttributesInSets.Select(a
                    => new AttributeDefinition(appId, a.Attribute.StaticName, a.Attribute.Type, a.IsTitle, a.AttributeId, a.SortOrder)));

            // Convert to ContentType-Model
            _contentTypes[appId] = contentTypes.ToDictionary(k1 => k1.AttributeSetId,
                set => (IContentType) new ContentType(appId, set.Name, set.StaticName, set.AttributeSetId,
                    set.Scope, set.Description, set.IsGhost, set.ZoneId, set.AppId, set.ConfigIsOmnipresent)
                {
                    Attributes = (set.SharedDefinitionId.HasValue
                            ? sharedAttribs[set.SharedDefinitionId.Value]
                            : set.Attributes)
                        .Cast<IAttributeDefinition>()
                        .ToList()
                }
            );
        }

        #endregion

        #region AppPackage
        /// <summary>Get Data to populate ICache</summary>
        /// <param name="appId">AppId (can be different than the appId on current context (e.g. if something is needed from the default appId, like MetaData)</param>
        /// <param name="entityIds">null or a List of EntitiIds</param>
        /// <param name="entitiesOnly">If only the CachItem.Entities is needed, this can be set to true to imporove performance</param>
        /// <returns>Item1: EntityModels, Item2: all ContentTypes, Item3: Assignment Object Types</returns>
        public AppDataPackage AppPackage(int appId, int[] entityIds = null, bool entitiesOnly = false)
        {
            var source = new AppDataPackageDeferredList();

            var contentTypes = ContentTypes(appId, source);

            var relationships = new List<EntityRelationshipItem>();

            #region prepare metadata lists for relationships etc.
            var metadataForGuid = new Dictionary<int, Dictionary<Guid, IEnumerable<IEntity>>>();
            var metadataForNumber = new Dictionary<int, Dictionary<int, IEnumerable<IEntity>>>();
            var metadataForString = new Dictionary<int, Dictionary<string, IEnumerable<IEntity>>>();

            var metadataTypes = _dbContext.ToSicEavAssignmentObjectTypes.ToImmutableDictionary(a => a.AssignmentObjectTypeId, a => a.Name);
            foreach (var mdt in metadataTypes.ToList())
            {
                metadataForGuid.Add(mdt.Key, new Dictionary<Guid, IEnumerable<IEntity>>());
                metadataForNumber.Add(mdt.Key, new Dictionary<int, IEnumerable<IEntity>>());
                metadataForString.Add(mdt.Key, new Dictionary<string, IEnumerable<IEntity>>());
            }

            #endregion

            #region Prepare & Extend EntityIds
            if (entityIds == null)
                entityIds = new int[0];

            var filterByEntityIds = entityIds.Any();

            // Ensure published Versions of Drafts are also loaded (if filtered by EntityId, otherwise all Entities from the app are loaded anyway)
            if (filterByEntityIds)
                entityIds = entityIds.Union(from e in _dbContext.ToSicEavEntities
                                            where e.PublishedEntityId.HasValue && !e.IsPublished && entityIds.Contains(e.EntityId) && !entityIds.Contains(e.PublishedEntityId.Value) && e.ChangeLogDeleted == null
                                            select e.PublishedEntityId.Value).ToArray();
            #endregion

            #region Get Entities with Attribute-Values from Database

            var rawEntities = _dbContext.ToSicEavEntities
                .Include(e => e.AttributeSet)
                .Include(e => e.ToSicEavValues)
                    .ThenInclude(v => v.ToSicEavValuesDimensions)
                .Where(e => !e.ChangeLogDeleted.HasValue &&
                            e.AttributeSet.AppId == appId &&
                            e.AttributeSet.ChangeLogDeleted == null &&
                            ( 
                                // filter by EntityIds (if set)
                                !filterByEntityIds || entityIds.Contains(e.EntityId) ||
                                e.PublishedEntityId.HasValue && entityIds.Contains(e.PublishedEntityId.Value)
                                // also load Drafts
                            ))
                .Select(e => new
                {
                    e.EntityId,
                    e.EntityGuid,
                    e.Version,
                    e.AttributeSetId,
                    Metadata = new Metadata
                    {
                        TargetType = e.AssignmentObjectTypeId,
                        KeyGuid = e.KeyGuid,
                        KeyNumber = e.KeyNumber,
                        KeyString = e.KeyString
                    },
                    e.IsPublished,
                    e.PublishedEntityId,
                    e.Owner, 
                    Modified = e.ChangeLogModifiedNavigation.Timestamp, 

                }).ToList();
            var eIds = rawEntities.Select(e => e.EntityId).ToList();

            var relatedEntities = _dbContext.ToSicEavEntityRelationships
                .Where(r => eIds.Contains(r.ParentEntityId))
                .GroupBy(g => g.ParentEntityId)
                .ToDictionary(g => g.Key, g => g.GroupBy(r => r.AttributeId)
                    .Select(rg => new
                    {
                        AttributeID = rg.Key,
                        Childs = rg.OrderBy(c => c.SortOrder).Select(c => c.ChildEntityId)
                    }));

            var attributes = _dbContext.ToSicEavValues
                .Include(v => v.ToSicEavValuesDimensions)
                    .ThenInclude(d => d.Dimension)
                .Where(r => eIds.Contains(r.EntityId))
                .Where(v => !v.ChangeLogDeleted.HasValue)
                .GroupBy(e => e.EntityId)
                .ToDictionary(e => e.Key, e => e.GroupBy(v => v.AttributeId)
                    .Select(vg => new
                    {
                        AttributeID = vg.Key,
                        Values = vg
                            .OrderBy(v2 => v2.ChangeLogCreated)
                            .Select(v2 => new
                            {
                                v2.Value,
                                Languages = v2.ToSicEavValuesDimensions.Select(l => new Dimension
                                    {
                                        DimensionId = l.DimensionId,
                                        ReadOnly = l.ReadOnly,
                                        Key = l.Dimension.EnvironmentKey.ToLowerInvariant()
                                    } as ILanguage).ToList(),
                            })
                    }));

            #endregion

            #region Build EntityModels
            var entities = new Dictionary<int, IEntity>();
            var entList = new List<IEntity>();

            foreach (var e in rawEntities)
            {
                var contentType = (ContentType)contentTypes[e.AttributeSetId];
                var newEntity = new Entity(appId, e.EntityGuid, e.EntityId, e.EntityId, e.Metadata, contentType, e.IsPublished, relationships, e.Modified, e.Owner, e.Version);

                var allAttribsOfThisType = new Dictionary<int, IAttribute>();	// temporary Dictionary to set values later more performant by Dictionary-Key (AttributeId)
                IAttribute titleAttrib = null;

                // Add all Attributes of that Content-Type
                foreach (var definition in contentType.Attributes)
                {
                    var entityAttribute = ((AttributeDefinition) definition).CreateAttribute();
                    newEntity.Attributes.Add(entityAttribute.Name, entityAttribute);
                    allAttribsOfThisType.Add(definition.AttributeId, entityAttribute);
                    if (definition.IsTitle)
                        titleAttrib = entityAttribute;
                }

                // If entity is a draft, add references to Published Entity
                if (!e.IsPublished && e.PublishedEntityId.HasValue)
                {
                    // Published Entity is already in the Entities-List as EntityIds is validated/extended before and Draft-EntityID is always higher as Published EntityId
                    newEntity.PublishedEntity = entities[e.PublishedEntityId.Value];
                    ((Entity)newEntity.PublishedEntity).DraftEntity = newEntity;
                    newEntity.EntityId = e.PublishedEntityId.Value;
                }

                #region Add metadata-lists based on AssignmentObjectTypes

                // unclear why #1 is handled in a special way - why should this not be cached? I believe 1 means no specific assignment
                if (e.Metadata.IsMetadata && !entitiesOnly)
                {
                    // Try guid first. Note that an item can be assigned to both a guid, string and an int if necessary, though not commonly used
                    if (e.Metadata.KeyGuid.HasValue)
                    {
                        // Ensure that this assignment-Type (like 4 = entity-assignment) already has a dictionary for storage
                        //if (!metadataForGuid.ContainsKey(e.Metadata.TargetType)) // ensure AssignmentObjectTypeID
                        //    metadataForGuid.Add(e.Metadata.TargetType, new Dictionary<Guid, IEnumerable<IEntity>>());

                        // Ensure that the assignment type (like 4) the target guid (like a350320-3502-afg0-...) has an empty list of items
                        if (!metadataForGuid[e.Metadata.TargetType].ContainsKey(e.Metadata.KeyGuid.Value)) // ensure Guid
                            metadataForGuid[e.Metadata.TargetType][e.Metadata.KeyGuid.Value] = new List<IEntity>();

                        // Now all containers must exist, add this item
                        ((List<IEntity>)metadataForGuid[e.Metadata.TargetType][e.Metadata.KeyGuid.Value]).Add(newEntity);
                    }
                    if (e.Metadata.KeyNumber.HasValue)
                    {
                        //if (!metadataForNumber.ContainsKey(e.Metadata.TargetType)) // ensure AssignmentObjectTypeID
                        //    metadataForNumber.Add(e.Metadata.TargetType, new Dictionary<int, IEnumerable<IEntity>>());

                        if (!metadataForNumber[e.Metadata.TargetType].ContainsKey(e.Metadata.KeyNumber.Value)) // ensure Guid
                            metadataForNumber[e.Metadata.TargetType][e.Metadata.KeyNumber.Value] = new List<IEntity>();

                        ((List<IEntity>)metadataForNumber[e.Metadata.TargetType][e.Metadata.KeyNumber.Value]).Add(newEntity);
                    }
                    if (!string.IsNullOrEmpty(e.Metadata.KeyString))
                    {
                        //if (!metadataForString.ContainsKey(e.Metadata.TargetType)) // ensure AssignmentObjectTypeID
                        //    metadataForString.Add(e.Metadata.TargetType, new Dictionary<string, IEnumerable<IEntity>>());

                        if (!metadataForString[e.Metadata.TargetType].ContainsKey(e.Metadata.KeyString)) // ensure Guid
                            metadataForString[e.Metadata.TargetType][e.Metadata.KeyString] = new List<IEntity>();

                        ((List<IEntity>)metadataForString[e.Metadata.TargetType][e.Metadata.KeyString]).Add(newEntity);
                    }
                }

                #endregion

                #region add Related-Entities Attributes to the entity
                if(relatedEntities.ContainsKey(e.EntityId))
                    foreach (var r in relatedEntities[e.EntityId])
                    {
                        var attrib = allAttribsOfThisType[r.AttributeID];
                        attrib.Values = new List<IValue> { Value.Build(attrib.Type, r.Childs, null, source) };
                    }
                #endregion

                #region Add "normal" Attributes (that are not Entity-Relations)
                if (attributes.ContainsKey(e.EntityId))
                    foreach (var a in attributes[e.EntityId])// e.Attributes)
                    {
                        IAttribute attrib;
                        try
                        {
                            attrib = allAttribsOfThisType[a.AttributeID];
                        }
                        catch (KeyNotFoundException)
                        {
                            continue;
                        }
                        if (attrib == titleAttrib)
                            newEntity.SetTitleField(attrib.Name);

                        attrib.Values = a.Values.Select(v => Value.Build(attrib.Type, v.Value, v.Languages)).ToList();

                        #region issue fix faulty data dimensions
                        // Background: there are rare cases, where data was stored incorrectly
                        // this happens when a attribute has multiple values, but some don't have languages assigned
                        // that would be invalid, as any property with a language code must have all the values (for that property) with language codes
                        if (attrib.Values.Count > 1 && attrib.Values.Any(v => !v.Languages.Any()))
                        {
                            var badValuesWithoutLanguage = attrib.Values.Where(v => !v.Languages.Any()).ToList();
                            if (badValuesWithoutLanguage.Any())
                                badValuesWithoutLanguage.ForEach(badValue =>
                                    attrib.Values.Remove(badValue));
                        }

                        #endregion
                    }

                // Special treatment in case there is no title 
                // sometimes happens if the title-field is re-defined and old data might no have this
                // also happens in rare cases, where the title-attrib is an entity-picker
                if (newEntity.Title == null && titleAttrib != null)
                    newEntity.SetTitleField(titleAttrib.Name);
                #endregion

                entities.Add(e.EntityId, newEntity);
                entList.Add(newEntity);
            }
            #endregion

            #region Populate Entity-Relationships (after all Entitys are created)

            var relationshipQuery = _dbContext.ToSicEavEntityRelationships
                .Include(er => er.Attribute.ToSicEavAttributesInSets)
                .Where(r => r.Attribute.ToSicEavAttributesInSets.Any(s => s.AttributeSet.AppId == appId))
                .Where(r => !filterByEntityIds || !r.ChildEntityId.HasValue || entityIds.Contains(r.ChildEntityId.Value) ||
                         entityIds.Contains(r.ParentEntityId))
                .OrderBy(r => r.ParentEntityId).ThenBy(r => r.AttributeId).ThenBy(r => r.ChildEntityId)
                .Select(r => new {r.ParentEntityId, r.Attribute.StaticName, r.ChildEntityId});

            var relationshipsRaw = relationshipQuery.ToList();
            foreach (var relationship in relationshipsRaw)
            {
                try
                {
                    if (entities.ContainsKey(relationship.ParentEntityId) &&
                        (!relationship.ChildEntityId.HasValue ||
                         entities.ContainsKey(relationship.ChildEntityId.Value)))
                        relationships.Add(new EntityRelationshipItem(entities[relationship.ParentEntityId],
                            relationship.ChildEntityId.HasValue ? entities[relationship.ChildEntityId.Value] : null));
                }
                catch (KeyNotFoundException) { /* ignore */ }
            }

            #endregion

            var appPack = new AppDataPackage(entities, entList, contentTypes, metadataForGuid, metadataForNumber, metadataForString, metadataTypes, relationships);
            source.AttachApp(appPack);
            return appPack;
        }

        #endregion

        // 2017-08-28 2dm disabled for now as not in use
        /// <summary>
        /// Get EntityModel for specified EntityId
        /// </summary>
        /// <returns>A single IEntity or throws InvalidOperationException</returns>
        //public IEntity Entity(int appId, int entityId)
        //    => AppPackage(appId, new[] { entityId }, true)
        //        .Entities.Single(e => e.Key == entityId).Value; // must filter by EntityId again because of Drafts

        public Dictionary<int, string> MetadataTargetTypes() => _dbContext.ToSicEavAssignmentObjectTypes
            .ToDictionary(a => a.AssignmentObjectTypeId, a => a.Name);

        public Dictionary<int, Zone> Zones(/*int zoneId = -1*/) => _dbContext.ToSicEavZones
            .Include(z => z.ToSicEavApps)
            .Include(z => z.ToSicEavDimensions)
                .ThenInclude(d => d.ParentNavigation)
            //.Where(z => z.ZoneId == zoneId || zoneId == -1)
            .ToDictionary(z => z.ZoneId, z => new Zone(z.ZoneId,
                z.ToSicEavApps.FirstOrDefault(a => a.Name == Constants.DefaultAppName)?.AppId ?? -1,
                z.ToSicEavApps.ToDictionary(a => a.AppId, a => a.Name),
                z.ToSicEavDimensions.Where(d => d.ParentNavigation?.Key == Constants.CultureSystemKey)
                    .Cast<DimensionDefinition>().ToList()));

    }
}
