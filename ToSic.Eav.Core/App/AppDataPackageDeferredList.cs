﻿using System.Collections.Generic;
using ToSic.Eav.Interfaces;

namespace ToSic.Eav.App
{
    public class AppDataPackageDeferredList: IDeferredEntitiesList
    {
        public void AttachApp(AppDataPackage app) => _app = app;
        private AppDataPackage _app;

        public IDictionary<int, IEntity> List => _app.Entities;

        public IEnumerable<IEntity> LightList => _app.List;

        public IMetadataProvider Metadata => _app;
    }
}
