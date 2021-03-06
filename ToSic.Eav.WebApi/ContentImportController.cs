﻿using System;
using System.IO;
using System.Linq;
using System.Web.Http;
using ToSic.Eav.Apps;
using ToSic.Eav.Apps.ImportExport;
using ToSic.Eav.ImportExport.Options;
using ToSic.Eav.Repository.Efc.Parts;


namespace ToSic.Eav.WebApi
{
    public class ContentImportController : Eav3WebApiBase
    {
        public class ContentImportArgs
        {
            public int AppId;

            public string DefaultLanguage;

            public ImportResourceReferenceMode ImportResourcesReferences;

            public ImportDeleteUnmentionedItems ClearEntities;

            public string ContentType;

            public string ContentBase64;
        }

        public class ContentImportResult
        {
            public bool Succeeded;

            public dynamic Detail;

            public ContentImportResult(bool succeeded, dynamic detail)
            {
                Succeeded = succeeded;
                Detail = detail;
            }
        }


        [HttpPost]
        public ContentImportResult EvaluateContent(ContentImportArgs args)
        {
            AppId = args.AppId;

            var import = GetXmlImport(args);
            return import.ErrorLog.HasErrors 
                ? new ContentImportResult(!import.ErrorLog.HasErrors, import.ErrorLog.Errors) 
                : new ContentImportResult(!import.ErrorLog.HasErrors, new {
                    import.AmountOfEntitiesCreated,
                    import.AmountOfEntitiesDeleted,
                    import.AmountOfEntitiesUpdated,
                    import.AttributeNamesInDocument,
                    import.AttributeNamesInContentType,
                    import.AttributeNamesNotImported,
                    DocumentElementsCount = import.DocumentElements.Count(),
                    LanguagesInDocumentCount = import.LanguagesInDocument.Count()
                });
        }

        [HttpPost]
        public ContentImportResult ImportContent(ContentImportArgs args)
        {
            AppId = args.AppId;

            var import = GetXmlImport(args);
            if (!import.ErrorLog.HasErrors)
            {
                import.PersistImportToRepository(CurrentContext.UserName);
                SystemManager.Purge(AppId);
            }
            return new ContentImportResult(!import.ErrorLog.HasErrors, null);
        }


        private ToRefactorXmlImportVTable GetXmlImport(ContentImportArgs args)
        {
            var contentTypeId = CurrentContext.AttribSet.GetIdWithEitherName(args.ContentType);//.AttributeSetId;//.AttributeSetID;// GetContentTypeId(args.ContentType);
            var contextLanguages = AppManager.Read.Zone.Languages().Select(l => l.EnvironmentKey).ToArray();// CurrentContext.Dimensions.GetLanguages().Select(language => language.EnvironmentKey).ToArray();

            using (var contentSteam = new MemoryStream(Convert.FromBase64String(args.ContentBase64)))
            {
                return new ToRefactorXmlImportVTable(CurrentContext.ZoneId, args.AppId, contentTypeId, contentSteam, contextLanguages, args.DefaultLanguage, args.ClearEntities, args.ImportResourcesReferences);
            }
        }

    }
}
