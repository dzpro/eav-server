﻿using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ToSic.Eav.Persistence.EFC11.Models;
using Microsoft.Practices.Unity;
using ToSic.Eav.Data;

namespace ToSic.Eav.Persistence.EFC11.Tests
{
    [TestClass]
    public class Efc11LoadTests
    {
        #region test preparations

        private EavDbContext _db;
        private Efc11Loader _loader;

        [TestInitialize]
        public void Init()
        {
            Trace.Write("initializing DB & loader");
            _db = Factory.Container.Resolve<EavDbContext>();
            _loader = new Efc11Loader(_db);
        }
        #endregion

        [TestMethod]
        public void GetSomething()
        {
            var results = _db.ToSicEavZones.Single(z => z.ZoneId == 1);
            Assert.IsTrue(results.ZoneId == 1, "zone doesn't fit - it is " + results.ZoneId);
        }

        [TestMethod]
        public void TestLoadXAppBlog()
        {
            var results = TestLoadApp(2);

            Assert.AreEqual(1063, results.Entities.Count, "tried counting");
        }

        [TestMethod]
        public void LoadContentTypesOf2Once()
        {
            var results = TestLoadCts(2);
            Assert.AreEqual(61, results.Count, "dummy test: ");
        }

        [TestMethod]
        public void LoadContentTypesOf2TenXCached()
        {
            _loader.ResetCacheForTesting();
            var results = TestLoadCts(2);
            for (var x = 0; x < 9; x++)
                results = TestLoadCts(2);
            Assert.AreEqual(61, results.Count, "dummy test: ");
        }

        [TestMethod]
        public void LoadContentTypesOf2TenXCleared()
        {
            var results = TestLoadCts(2);
            for (var x = 0; x < 9; x++)
            {
                results = TestLoadCts(2);
                _loader.ResetCacheForTesting();
            }
            // var str = results.ToString();
            Assert.AreEqual(61, results.Count, "dummy test: ");
        }

        private AppDataPackage TestLoadApp(int appId)
        {
            return _loader.GetAppDataPackage(null, appId, null);
        }

        private IDictionary<int, IContentType> TestLoadCts(int appId)
        {
            return _loader.GetEavContentTypes(appId);
        }

    }
}