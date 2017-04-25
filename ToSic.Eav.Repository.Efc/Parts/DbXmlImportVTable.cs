﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Practices.Unity;
using ToSic.Eav.ImportExport;
using ToSic.Eav.ImportExport.Interfaces;
using ToSic.Eav.ImportExport.Logging;
using ToSic.Eav.ImportExport.Models;
using ToSic.Eav.ImportExport.Options;
using ToSic.Eav.ImportExport.Validation;
using ToSic.Eav.ImportExport.Xml;
using ToSic.Eav.Interfaces;
using ToSic.Eav.Persistence.Efc.Models;

namespace ToSic.Eav.Repository.Efc.Parts
{

    // todo:
    // if possible, split appart into
    // 1. xml > import entity
    // 2. import-entity > db
    // core dependencies are the data-structure of the content-type, which is used to build the import-entity
    // it also looks up data in the DB to validate if they already exist - to see if it's a new/update scenario

    /// <summary>
    /// Import a virtual table of content-items
    /// </summary>
    public class DbXmlImportVTable
    {
        #region properties like _appId, Document, etc.

        #region Timing / Debuging infos

        /// <summary>
        /// Helper to measure time used for stuff
        /// </summary>
        public Stopwatch Timer { get; set; } = new Stopwatch();

        public long TimeForMemorySetup;
        public long TimeForDbImport;
        #endregion

        private readonly int _appId;

        private readonly int _zoneId;

        private readonly ToSicEavAttributeSets _contentType;

        /// <summary>
        /// The xml document to imported.
        /// </summary>
        public XDocument Document  { get; private set; }

        /// <summary>
        /// The elements of the xml document.
        /// </summary>
        public IEnumerable<XElement> DocumentElements { get; private set; }

        private readonly string _documentLanguageFallback;

        private IEnumerable<string> _languages;

        private readonly ImportResourceReferenceMode _importResourceReference;

        private readonly ImportDeleteUnmentionedItems _entityClear;

        /// <summary>
        /// The entities created from the document. They will be saved to the repository.
        /// </summary>
        public List<ImpEntity> Entities {get; }

        /// <summary>
        /// Errors found while importing the document to memory.
        /// </summary>
        public ImportErrorLog ErrorLog { get; }
        #endregion

        private ImpEntity GetEntity(Guid entityGuid)
        {
            return Entities.FirstOrDefault(entity => entity.EntityGuid == entityGuid);
        }

        [Dependency]
        public ISystemConfiguration Configuration { get; }

        private ImpEntity AppendEntity(Guid entityGuid)
        {
            var entity = new ImpEntity
            {
                AttributeSetStaticName = _contentType.StaticName,
                // KeyTypeId = Configuration.KeyTypeDefault,// Configuration.AssignmentObjectTypeIdDefault,
                EntityGuid = entityGuid,
                KeyNumber = null,
                Values = new Dictionary<string, List<IImpValue>>()
            };
            Entities.Add(entity);
            return entity;
        }

        /// <summary>
        /// Create a xml import. The data stream passed will be imported to memory, and checked 
        /// for errors. If no error could be found, the data can be persisted to the repository.
        /// </summary>
        /// <param name="zoneId">ID of 2SexyContent zone</param>
        /// <param name="applicationId">ID of 2SexyContent application</param>
        /// <param name="contentTypeId">ID of 2SexyContent type</param>
        /// <param name="dataStream">Xml data stream to import</param>
        /// <param name="languages">Languages that can be imported (2SexyContent languages enabled)</param>
        /// <param name="documentLanguageFallback">Fallback document language</param>
        /// <param name="entityClear">How to handle entities already in the repository</param>
        /// <param name="importResourceReference">How value references to files and pages are handled</param>
        public DbXmlImportVTable(int zoneId, int applicationId, int contentTypeId, Stream dataStream, IEnumerable<string> languages, string documentLanguageFallback, ImportDeleteUnmentionedItems entityClear, ImportResourceReferenceMode importResourceReference)
        {
            Entities = new List<ImpEntity>();
            ErrorLog = new ImportErrorLog();

            _appId = applicationId;
            _zoneId = zoneId;
            _contentType = DbDataController.Instance(zoneId, applicationId).AttribSet.GetAttributeSet(contentTypeId);
            _languages = languages;
            _documentLanguageFallback = documentLanguageFallback;
            _entityClear = entityClear;
            _importResourceReference = importResourceReference;

            ValidateAndImportToMemory(dataStream);
        }

        /// <summary>
        /// Deserialize a 2sxc data xml stream to the memory. The data will also be checked for 
        /// errors.
        /// </summary>
        /// <param name="dataStream">Data stream</param>
        private void ValidateAndImportToMemory(Stream dataStream)
        {
            Timer.Start();
            try
            {
                if (_languages == null || !_languages.Any())
                {
                    _languages = new[] { string.Empty };
                }

                if (_contentType == null)
                {
                    ErrorLog.AppendError(ImportErrorCode.InvalidContentType);
                    return;
                }

                Document = XDocument.Load(dataStream);
                dataStream.Position = 0;
                if (Document == null)
                {
                    ErrorLog.AppendError(ImportErrorCode.InvalidDocument);
                    return;
                }

                var documentRoot = Document.Element(XmlConstants.Root);

                DocumentElements = documentRoot.Elements(XmlConstants.Entity);
                if (!DocumentElements.Any())
                {
                    ErrorLog.AppendError(ImportErrorCode.InvalidDocument);
                    return;
                }

                // Check the content type of the document (it can be found on each element in the Type attribute)
                var documentTypeAttribute = DocumentElements.First().Attribute(XmlConstants.EntityTypeAttribute);
                if (documentTypeAttribute?.Value == null || documentTypeAttribute.Value != _contentType.Name.RemoveSpecialCharacters())
                {
                    ErrorLog.AppendError(ImportErrorCode.InvalidRoot);
                    return;
                }

                var documentElementNumber = 0;

                // Assure that each element has a GUID and language child element
                foreach (var element in DocumentElements)
                {
                    if (element.Element(XmlConstants.EntityGuid) == null)
                    {
                        element.Add(new XElement(XmlConstants.EntityGuid, ""));
                        //element.Append(DocumentNodeNames.EntityGuid, "");
                    }
                    if (element.Element(XmlConstants.EntityLanguage) == null)
                    {
                        element.Add(new XElement(XmlConstants.EntityLanguage, ""));
                        //element.Append(DocumentNodeNames.EntityLanguage, "");
                    }
                }
                var documentElementLanguagesAll = DocumentElements.GroupBy(element => element.Element(XmlConstants.EntityGuid).Value).Select(group => group.Select(element => element.Element(XmlConstants.EntityLanguage).Value).ToList());
                var documentElementLanguagesCount = documentElementLanguagesAll.Select(item => item.Count);
                if (documentElementLanguagesCount.Any(count => count != 1))
                {
                    // It is an all language import, so check if all languages are specified for all entities
                    foreach (var documentElementLanguages in documentElementLanguagesAll)
                    {   
                        if (_languages.Except(documentElementLanguages).Any())
                        {
                            ErrorLog.AppendError(ImportErrorCode.MissingElementLanguage, "Langs=" + string.Join(", ", _languages));
                            return;
                        }
                    }
                }
 
                var entityGuidManager = new ImportItemGuidManager();
                foreach (var documentElement in DocumentElements)
                {
                    documentElementNumber++;

                    var documentElementLanguage = documentElement.Element(XmlConstants.EntityLanguage)?.Value;
                    if (!_languages.Any(language => language == documentElementLanguage))
                    {   // DNN does not support the language
                        ErrorLog.AppendError(ImportErrorCode.InvalidLanguage, "Lang=" + documentElementLanguage, documentElementNumber);
                        continue;
                    }

                    var entityGuid = entityGuidManager.GetGuid(documentElement, _documentLanguageFallback);
                    var entity = GetEntity(entityGuid) ?? AppendEntity(entityGuid);

                    var attributes = _contentType.GetAttributes();
                    foreach (var attribute in attributes)
                    {   
                        var valueType = attribute.Type;
                        var valueName = attribute.StaticName;
                        var value = documentElement.Element(valueName)?.Value;
                        if (value == null || value == "[]")// value.IsValueNull())
                        {
                            continue;
                        }

                        if (value == "[\"\"]")//value.IsValueEmpty())
                        {   // It is an empty string
                            entity.AppendAttributeValue(valueName, "", attribute.Type, documentElementLanguage, false, _importResourceReference== ImportResourceReferenceMode.Resolve);
                            continue;
                        }

                        var valueReferenceLanguage = value.GetValueReferenceLanguage();
                        if (valueReferenceLanguage == null)
                        {   // It is not a value reference.. it is a normal text
                            try
                            {
                                entity.AppendAttributeValue(valueName, value, valueType, documentElementLanguage, false, _importResourceReference == ImportResourceReferenceMode.Resolve);
                            }
                            catch (FormatException)
                            {
                                ErrorLog.AppendError(ImportErrorCode.InvalidValueFormat, valueName + ":" + valueType + "=" + value, documentElementNumber);
                            }
                            continue;
                        }

                        var valueReferenceProtection = value.GetValueReferenceProtection();
                        if (valueReferenceProtection != "rw" && valueReferenceProtection != "ro")
                        {
                            ErrorLog.AppendError(ImportErrorCode.InvalidValueReferenceProtection, value, documentElementNumber);
                            continue;
                        }
                        var valueReadOnly = valueReferenceProtection == "ro";

                        var entityValue = entity.GetValueItemOfLanguage(valueName, valueReferenceLanguage);
                        if (entityValue != null)
                        {
                            entityValue.AppendLanguageReference(documentElementLanguage, valueReadOnly);
                            continue;
                        }

                        // We do not have the value referenced in memory, so search for the 
                        // value in the database 
                        var dbEntity = _contentType.EntityByGuid(entityGuid);
                        if (dbEntity == null)
                        {
                            ErrorLog.AppendError(ImportErrorCode.InvalidValueReference, value, documentElementNumber);
                            continue;
                        }

                        var dbEntityValue = dbEntity.GetValueOfExactLanguage(attribute, valueReferenceLanguage);
                        if(dbEntityValue == null)
                        {
                            ErrorLog.AppendError(ImportErrorCode.InvalidValueReference, value, documentElementNumber);
                            continue;
                        }

                        entity.AppendAttributeValue(valueName, dbEntityValue.Value, valueType, valueReferenceLanguage, dbEntityValue.IsLanguageReadOnly(valueReferenceLanguage), _importResourceReference == ImportResourceReferenceMode.Resolve)
                              .AppendLanguageReference(documentElementLanguage, valueReadOnly);       
                    }
                }                
            }
            catch (Exception exception)
            {
                ErrorLog.AppendError(ImportErrorCode.Unknown, exception.ToString());
            }
            Timer.Stop();
            TimeForMemorySetup = Timer.ElapsedMilliseconds;
        }

        /// <summary>
        /// Save the data in memory to the repository.
        /// </summary>
        /// <param name="userId">ID of the user doing the import</param>
        /// <returns>True if succeeded</returns>
        public bool PersistImportToRepository(string userId)
        {
            if (ErrorLog.HasErrors)
                return false;

            if (_entityClear == ImportDeleteUnmentionedItems.All)
            {
                var entityDeleteGuids = GetEntityDeleteGuids();
                foreach(var entityGuid in entityDeleteGuids)
                {
                    var entityId = _contentType.EntityByGuid(entityGuid).EntityId;
                    var context = DbDataController.Instance(_zoneId, _appId);
                    if (context.Entities.CanDeleteEntity(entityId)/* context.EntCommands.CanDeleteEntity(entityId)*/.Item1)
                        context.Entities.DeleteEntity(entityId);
                }
            }

            Timer.Start();
            var import = new DbImport(_zoneId, _appId, /*userId,*/ dontUpdateExistingAttributeValues: false, keepAttributesMissingInImport: true);
            import.ImportIntoDb(null, Entities);
            // important note: don't purge cache here, but the caller MUST do this!

            Timer.Stop();
            TimeForDbImport = Timer.ElapsedMilliseconds;
            return true;
        }


        #region Deserialize statistics methods
        private List<Guid> GetExistingEntityGuids()
        {
            var existingGuids = _contentType.ToSicEavEntities.Where(entity => entity.ChangeLogDeleted == null).Select(entity => entity.EntityGuid).ToList();
            return existingGuids;
        }

        private List<Guid> GetCreatedEntityGuids()
        {
            var newGuids = Entities.Select(entity => entity.EntityGuid.Value).ToList();
            return newGuids;
        }

        /// <summary>
        /// Get the languages found in the xml document.
        /// </summary>
        public IEnumerable<string> LanguagesInDocument => DocumentElements.Select(element => element.Element(XmlConstants.EntityLanguage).Value).Distinct();

        /// <summary>
        /// Get the attribute names in the xml document.
        /// </summary>
        public IEnumerable<string> AttributeNamesInDocument => DocumentElements.SelectMany(element => element.Elements())
            .GroupBy(attribute => attribute.Name.LocalName)
            .Select(group => @group.Key)
            .Where(name => name != XmlConstants.EntityGuid && name != XmlConstants.EntityLanguage)
            .ToList();

        /// <summary>
        /// The amount of enities created in the repository on data import.
        /// </summary>
        public int AmountOfEntitiesCreated
        {
            get
            {
                var existingGuids = GetExistingEntityGuids();
                var createdGuids = GetCreatedEntityGuids();
                return createdGuids.Except(existingGuids).Count();
            }          
        }

        /// <summary>
        /// The amount of enities updated in the repository on data import.
        /// </summary>
        public int AmountOfEntitiesUpdated
        {
           get 
           {
               var existingGuids = GetExistingEntityGuids();
               var createdGuids = GetCreatedEntityGuids();
               return createdGuids.Count(guid => existingGuids.Contains(guid));
           }
        }

        private List<Guid> GetEntityDeleteGuids()
        {
            var existingGuids = GetExistingEntityGuids();
            var createdGuids = GetCreatedEntityGuids();
            return existingGuids.Except(createdGuids).ToList();
        }
        
        /// <summary>
        /// The amount of enities deleted in the repository on data import.
        /// </summary>
        public int AmountOfEntitiesDeleted => _entityClear == ImportDeleteUnmentionedItems.None ? 0 : GetEntityDeleteGuids().Count;

        /// <summary>
        /// Get the attribute names in the content type.
        /// </summary>
        public IEnumerable<string> AttributeNamesInContentType 
            => _contentType.ToSicEavAttributesInSets.Select(item => item.Attribute.StaticName).ToList();
        //_contentType.GetStaticNames();

        /// <summary>
        /// Get the attributes not imported (ignored) from the document to the repository.
        /// </summary>
        public IEnumerable<string> AttributeNamesNotImported
        {
            get
            {
                var existingAttributes = AttributeNamesInContentType;//_contentType.GetStaticNames();
                var creatdAttributes = AttributeNamesInDocument;
                return existingAttributes.Except(creatdAttributes);
            }            
        }

        #endregion Deserialize statistics methods
        
    }




    internal static class StringExtension
    {
        /// <summary>
        /// Get for example en-US from [ref(en-US,ro)].
        /// </summary>
        public static string GetValueReferenceLanguage(this string valueString)
        {
            var match = Regex.Match(valueString, @"\[ref\((?<language>.+),(?<readOnly>.+)\)\]");
            return match.Success ? match.Groups["language"].Value : null;
        }

        /// <summary>
        /// Get for example ro from [ref(en-US,ro)].
        /// </summary>
        public static string GetValueReferenceProtection(this string valueString, string defaultValue = "")
        {
            var match = Regex.Match(valueString, @"\[ref\((?<language>.+),(?<readOnly>.+)\)\]");
            return match.Success ? match.Groups["readOnly"].Value : defaultValue;
        }

    }
}