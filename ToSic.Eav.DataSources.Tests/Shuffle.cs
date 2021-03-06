﻿using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ToSic.Eav.UnitTests.DataSources;

namespace ToSic.Eav.DataSources.Tests
{
    // Todo
    // Create tests with language-parameters as well, as these tests ignore the language and always use default

    [TestClass]
    public class Shuffle
    {
        //private const int TestVolume = 10000;
        //private ValueFilter _testDataGeneratedOutsideTimer;
        //public ValueFilterBoolean()
        //{
        //    //_testDataGeneratedOutsideTimer = ValueFilter_Test.CreateValueFilterForTesting(TestVolume);
        //}
        

        #region shuffle tests


        private static DataSources.Shuffle GenerateShuffleDS(int desiredFinds)
        {
            var ds = DataTableDataSourceTest.GeneratePersonSourceWithDemoData(desiredFinds, 1001, true);
            var sf = DataSource.GetDataSource<DataSources.Shuffle>(0, 0, ds);
            return sf;
        }


        [TestMethod]
        public void Shuffle_CountShuffle100()
        {
            var desiredFinds = 100;
            var sf = GenerateShuffleDS(desiredFinds);
            var found = sf.LightList.Count();
            Assert.AreEqual(desiredFinds, found, "Should find exactly this amount people");

        }

        [TestMethod]
        public void Shuffle5_ValidateNotOrdered()
        {
            var items = 5;
            var sf = GenerateShuffleDS(items);

            var origSeqSorted = AreAllItemsSorted(sf.In["Default"]);
            var seqConsistent = AreAllItemsSorted(sf.Out["Default"]);

            // after checking all, it should NOT be consistent
            Assert.IsTrue(origSeqSorted, "original sequence SHOULD be sorted");
            Assert.IsFalse(seqConsistent, "sequence should NOT be not-sorted");

        }

        private static bool AreAllItemsSorted(IDataStream sf)
        {
            // now the IDs shouldn't be incrementing one after another
            var seqConsistent = true;
            var lastId = 0;
            foreach (var itm in sf.LightList)
            {
                var newId = itm.EntityId;
                if (newId < lastId)
                    seqConsistent = false;
                lastId = newId;
            }
            return seqConsistent;
        }

        #endregion

    }
}
