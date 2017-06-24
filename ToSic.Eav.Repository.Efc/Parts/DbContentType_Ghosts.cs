﻿using System;
using System.Collections.Generic;
using System.Linq;
using ToSic.Eav.Persistence.Efc.Models;

namespace ToSic.Eav.Repository.Efc.Parts
{
    public partial class DbContentType
    {

        /// <summary>
        /// if AttributeSet refers another AttributeSet, get ID of the refered AttributeSet. Otherwise returns passed AttributeSetId.
        /// </summary>
        /// <param name="attributeSetId">AttributeSetId to resolve</param>
        internal int ResolvePotentialGhostAttributeSetId(int attributeSetId)
        {
            var usesConfigurationOfAttributeSet = DbContext.SqlDb.ToSicEavAttributeSets
                .Where(a => a.AttributeSetId == attributeSetId)
                .Select(a => a.UsesConfigurationOfAttributeSet)
                .Single();
            return usesConfigurationOfAttributeSet ?? attributeSetId;
        }

        public List<ToSicEavAttributeSets> FindPotentialGhostSources(string contentTypeParentName)
        {
            var ghostAttributeSets = DbContext.SqlDb.ToSicEavAttributeSets.Where(
                    a => a.StaticName == contentTypeParentName
                         && a.ChangeLogDeleted == null
                         && a.UsesConfigurationOfAttributeSet == null).
                OrderBy(a => a.AttributeSetId)
                .ToList();
            return ghostAttributeSets;
        }


        public void CreateGhost(string staticName)
        {
            var ct = GetTypeByStaticName(staticName);
            if (ct != null)
                throw new Exception("current App already has a content-type with this static name - cannot continue");

            // find the original
            var attSets = DbContext.SqlDb.ToSicEavAttributeSets
                .Where(ats => ats.StaticName == staticName
                    && !ats.UsesConfigurationOfAttributeSet.HasValue    // never duplicate a clone/ghost
                    && ats.ChangeLogDeleted == null                     // never duplicate a deleted
                    && ats.AlwaysShareConfiguration == false)           // never duplicate an always-share
                .OrderBy(ats => ats.AttributeSetId)
                .ToList();

            if (!attSets.Any())
                throw new ArgumentException("can't find an original, non-ghost content-type with the static name '" + staticName + "'");

            if (attSets.Count > 1)
                throw new Exception("found " + attSets.Count + " (expected 1) original, non-ghost content-type with the static name '" + staticName + "' - so won't create ghost as it's not clear off which you would want to ghost.");

            var attSet = attSets.First();
            var newSet = new ToSicEavAttributeSets
            {
                AppId = DbContext.AppId, // needs the new, current appid
                StaticName = attSet.StaticName,
                Name = attSet.Name,
                Scope = attSet.Scope,
                Description = attSet.Description,
                UsesConfigurationOfAttributeSet = attSet.AttributeSetId,
                AlwaysShareConfiguration = false, // this is copy, never re-share
                ChangeLogCreated = DbContext.Versioning.GetChangeLogId()
            };
            DbContext.SqlDb.Add(newSet);

            // save first, to ensure it has an Id
            DbContext.SqlDb.SaveChanges();
        }

    }
}
