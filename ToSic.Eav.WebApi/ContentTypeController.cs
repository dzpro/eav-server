﻿using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Web.Http;
using ToSic.Eav.Api;
using ToSic.Eav.Data;
using ToSic.Eav.DataSources;
using ToSic.Eav.DataSources.Caches;
using ToSic.Eav.Serializers;

namespace ToSic.Eav.WebApi
{
	/// <summary>
	/// Web API Controller for ContentTypes
	/// </summary>
	public class ContentTypeController : Eav3WebApiBase
    {
        #region Content-Type Get, Delete, Save
        [HttpGet]
	    public IEnumerable<dynamic> Get(int appId, string scope = null, bool withStatistics = false)
        {
            // scope can be null (eav) or alternatives would be "System", "2SexyContent-System", "2SexyContent-App", "2SexyContent"
            var cache = (BaseCache)DataSource.GetCache(null, appId);
            var allTypes = cache.GetContentTypes().Select(t => t.Value);

            var filteredType = allTypes.Where(t => t.Scope == scope)
                .OrderBy(t => t.Name)
                .Select(t => ContentTypeForJson(t, cache));

            return filteredType;
	    }

	    private dynamic ContentTypeForJson(IContentType t, ICache cache)
	    {
	        var metadata = GetMetadata(t, cache);

	        var nameOverride = metadata?.GetBestValue(Constants.ContentTypeMetadataLabel).ToString();
	        if (string.IsNullOrEmpty(nameOverride))
	            nameOverride = t.Name;
            var ser = new Serializer();

            var jsonReady = new
	        {
	            Id = t.AttributeSetId,
                t.Name,
                Label = nameOverride,
	            t.StaticName,
	            t.Scope,
	            t.Description,
	            UsesSharedDef = t.UsesConfigurationOfAttributeSet != null,
	            SharedDefId = t.UsesConfigurationOfAttributeSet,
	            Items = cache.LightList.Count(i => i.Type == t),
	            Fields = ((ContentType)t).AttributeDefinitions.Count,
                Metadata = ser.Prepare(metadata)
	        };
	        return jsonReady;
	    }

	    public IEntity GetMetadata(IContentType t, ICache cache = null)
	    {
	        if (cache == null) throw new NotImplementedException("cache-null not implemented");
            // find metadata, if provided
            // get all content-type metadata
            var metaDataSource = (IMetaDataSource)cache;
	        return metaDataSource.GetAssignedEntities(
	            Constants.MetadataForContentType, t.AttributeSetId)
	            .FirstOrDefault();
	    }

        [HttpGet]
	    public dynamic GetSingle(int appId, string contentTypeStaticName, string scope = null)
	    {
            SetAppIdAndUser(appId);
            // var source = InitialDS;
            var cache = DataSource.GetCache(null, appId);
            var ct = cache.GetContentType(contentTypeStaticName);
            return ContentTypeForJson(ct, cache);
	    }

        [HttpGet]
	    [HttpDelete]
	    public bool Delete(int appId, string staticName)
	    {
            SetAppIdAndUser(appId);
            CurrentContext.ContentType.Delete(staticName);
	        return true;
	    }

	    [HttpPost]
	    public bool Save(int appId, Dictionary<string, string> item)
	    {
            SetAppIdAndUser(appId);
	        bool changeStaticName;
            bool.TryParse(item["ChangeStaticName"], out changeStaticName);
            CurrentContext.ContentType.AddOrUpdate(
                item["StaticName"], 
                item["Scope"], 
                item["Name"], 
                item["Description"],
                null, false, 
                changeStaticName, 
                changeStaticName ? item["NewStaticName"] : item["StaticName"]);
	        return true;
	    }
        #endregion

        [HttpGet]
	    public bool CreateGhost(int appId, string sourceStaticName)
	    {
            SetAppIdAndUser(appId);
            CurrentContext.ContentType.CreateGhost(sourceStaticName);
            return true;
	    }

        #region Fields - Get, Reorder, Data-Types (for dropdown), etc.
        /// <summary>
        /// Returns the configuration for a content type
        /// </summary>
        [HttpGet]
        public IEnumerable<dynamic> GetFields(int appId, string staticName)
        {
            SetAppIdAndUser(appId);

            var fields =
                CurrentContext.ContentType.GetContentTypeConfiguration(staticName)
                    .OrderBy(ct => ((AttributeBase)ct.Item1).SortOrder);

            var appDef = new BetaFullApi(null, appId, CurrentContext);
            var appInputTypes = appDef.GetInputTypes(true).ToList();
            var noTitleCount = 0;
            string fldName = "";

            // assemble a list of all input-types (like "string-default", "string-wysiwyg..."
            Dictionary<string, IEntity> inputTypesDic;
            try
            {
                inputTypesDic =
                    appInputTypes.ToDictionary(
                        a => (fldName = a.GetBestValue("EntityTitle")?.ToString() ?? "error-no-title" + noTitleCount++),
                        a => a);
            }
            catch (Exception ex)
            {
                throw new Exception("Error on " + fldName + "; note: noTitleCount " + noTitleCount, ex);
            }

            var ser = new Serializer();
            return fields.Select(a =>
            {
                var inputtype = findInputType(a.Item2);
                return new
                {
                    Id = a.Item1.AttributeId,
                    ((AttributeBase)a.Item1).SortOrder,
                    a.Item1.Type,
                    InputType = inputtype,
                    StaticName = a.Item1.Name,
                    a.Item1.IsTitle,
                    a.Item1.AttributeId,
                    Metadata = a.Item2.ToDictionary(e => e.Key, e => ser.Prepare(e.Value)),
                    InputTypeConfig =
                        inputTypesDic.ContainsKey(inputtype) ? ser.Prepare(inputTypesDic[inputtype]) : null
                };
            });
        }

	    private string findInputType(Dictionary<string, IEntity> definitions)
	    {
	        if (!definitions.ContainsKey("All"))
	            return "unknown";

	        var inputType = definitions["All"]?.GetBestValue("InputType");

	        if (string.IsNullOrEmpty(inputType as string))
	            return "unknown";
	        return inputType.ToString();


	    }

        [HttpGet]
        public bool Reorder(int appId, int contentTypeId, string newSortOrder)
        {
            SetAppIdAndUser(appId);

            var sortOrderList = newSortOrder.Trim('[', ']').Split(',').Select(int.Parse).ToList();
            CurrentContext.ContentType.Reorder(contentTypeId, sortOrderList);
            return true;
        }

	    [HttpGet]
	    public string[] DataTypes(int appId)
	    {
            SetAppIdAndUser(appId);
	        return CurrentContext.SqlDb.AttributeTypes.OrderBy(a => a.Type).Select(a => a.Type).ToArray();
	    }

	    [HttpGet]
	    // public IEnumerable<Dictionary<string, object>> InputTypes(int appId)
	    public IEnumerable<Dictionary<string, object>> InputTypes(int appId)
	    {
            SetAppIdAndUser(appId);
            var appDef = new BetaFullApi(null, appId, CurrentContext);
	        var appInputTypes = appDef.GetInputTypes(true).ToList();
            
	        return Serializer.Prepare(appInputTypes);

	    }

           
            
        [HttpGet]
        [SuppressMessage("ReSharper", "RedundantArgumentDefaultValue")]
        public int AddField(int appId, int contentTypeId, string staticName, string type, string inputType, int sortOrder)
	    {
            SetAppIdAndUser(appId);
	        return CurrentContext.Attributes.AddAttribute(contentTypeId, staticName, type, inputType, sortOrder, 1, false, true).AttributeID;
	    }

        [HttpGet]
        public bool UpdateInputType(int appId, int attributeId, string inputType)
        {
            SetAppIdAndUser(appId);
            return CurrentContext.Attributes.UpdateInputType(attributeId, inputType);
        }

        [HttpGet]
        [HttpDelete]
	    public bool DeleteField(int appId, int contentTypeId, int attributeId)
	    {
            SetAppIdAndUser(appId);
            return CurrentContext.Attributes.RemoveAttribute(attributeId);
	    }

        [HttpGet]
	    public void SetTitle(int appId, int contentTypeId, int attributeId)
	    {
            SetAppIdAndUser(appId);
            CurrentContext.Attributes.SetTitleAttribute(attributeId, contentTypeId);
	    }

        [HttpGet]
        public bool Rename(int appId, int contentTypeId, int attributeId, string newName)
        {
            SetAppIdAndUser(appId);
            CurrentContext.Attributes.RenameStaticName(attributeId, contentTypeId, newName);
            return true;
        }


        #endregion

    }
}